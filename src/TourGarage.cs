using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OwlchemyVR;
using OwlchemyVR.Teleportation;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "garage": the computer's parts, made by the game's own BrainControlledModule_Computer.SpawnAndAttachComputerParts
    // (broken ones when the garage loads, working ones when the story repairs it).
    // 1. Right after the host arrives, its game makes a loose working RAM stick (from the part's prefab): the client, which
    //    has none out, must still get one.
    // 2. Both copies repair the computer (what the shared story does on each), the client 4 s after the host (a slower
    //    machine): each keeps its own attached parts; the mod copies and removes none of them.
    // 3. The host pulls the working RAM stick out of the computer and puts the loose one in; the client must show the
    //    same (the part the game made there is known on both by its point). Then the client takes the loose one back out,
    //    and the host must show that.
    // 4. On the client, an item leaves the laundry zone while still its child (what a client's copy following the host's
    //    dumped laundry does when its own Rick dumps later): the game's handler must not throw.
    public sealed partial class TogetherRuntime
    {
        const BindingFlags AnyField = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const float RepairAt = 170;   // host time of the shared repair: after both copies are in and the copied part is here
        static readonly Vector3 LoosePart = new Vector3(0.6f, 1.0f, -0.6f);   // process-scoped (a constant)
        const float SwapAt = 200, PullAt = 225;   // host times of the host's swap and the client's pull (step 3)

        static AttachablePoint RamPoint() { BrainControlledModule_Computer c; System.Array p = ComputerPoints(out c); return p != null ? PointField<AttachablePoint>(p.GetValue(1), "attachPoint") : null; }

        GrabbableItem SyncedLoose(string name)
        {
            foreach (GrabbableItem g in Object.FindObjectsOfType<GrabbableItem>())
                if (BaseName(g.name) == name && !AttachedPart(g) && (g.transform.position - LoosePart).magnitude < 1f && ItemIdOf(g) != 0) return g;
            return null;
        }

        static string Where(NetItem item) { return item == null ? "unknown" : item.Part != null && item.Part.CurrentlyAttachedTo != null ? item.Part.CurrentlyAttachedTo.name : "out"; }

        // What a hand's grab does for an item here, without a hand (an attached part has no body to grab in the lab).
        void ClaimForTour(NetItem item)
        {
            item.Owner = LocalId; item.Confirmed = false;
            StopFollowing(item, true);
            Send(0, Wire.Packet(Op.ItemClaim, w => { w.Write(item.Id); w.Write(item.Epoch); }));
        }

        static System.Array ComputerPoints(out BrainControlledModule_Computer computer)
        {
            computer = Object.FindObjectOfType<BrainControlledModule_Computer>();
            if (computer == null) return null;
            return (System.Array)typeof(BrainControlledModule_Computer).GetField("_vitialComputerAttachPoints", AnyField).GetValue(computer);
        }

        static T PointField<T>(object entry, string field) { return (T)entry.GetType().GetField(field, AnyField).GetValue(entry); }

        // "RamStick=RamStick ...": each attach point's working part and what it holds now.
        static string PointsHold(System.Array points, out int holdingWorking)
        {
            holdingWorking = 0;
            List<string> s = new List<string>();
            foreach (object p in points)
            {
                string working = PointField<AttachableItem>(p, "workingPart").name;
                List<AttachableItem> held = PointField<AttachablePoint>(p, "attachPoint").AttachedItems;
                string holds = held.Count == 1 && held[0] != null ? BaseName(held[0].name) : held.Count + " items";
                if (holds == working) holdingWorking++;
                s.Add(working + "=" + holds);
            }
            return string.Join(" ", s.ToArray());
        }

        // Loose (not attached) grabbables out in the scene with this name, within 1 m of a spot.
        static int LooseNear(string name, Vector3 at)
        {
            int n = 0;
            foreach (GrabbableItem g in Object.FindObjectsOfType<GrabbableItem>())
                if (BaseName(g.name) == name && !AttachedPart(g) && (g.transform.position - at).magnitude < 1f) n++;
            return n;
        }

        // The pad the game starts a player on in this scene: the first of its start pads (TeleportationController's
        // initialZoneDatas) that is a pad here.
        static string StartPad()
        {
            TeleportationController tc = GlobalStorage.Instance.TeleportationController;
            UniqueObjectData[] initial = (UniqueObjectData[])typeof(TeleportationController).GetField("initialZoneDatas", AnyField).GetValue(tc);
            foreach (TeleportZone z in Object.FindObjectsOfType<TeleportZone>())
                foreach (UniqueObjectData d in initial) if (d != null && z.UniqueObjectData == d) return d.name;
            return "none";
        }

        static string PadHere()
        {
            TeleportZone z = GlobalStorage.Instance.TeleportationController.CurrentZone;
            return z != null && z.UniqueObjectData != null ? z.UniqueObjectData.name : "none";
        }

        IEnumerator OnStartPad(string check)
        {
            yield return StartCoroutine(Wait(() => PadHere() != "none" && StartPad() != "none", 30, check, "no pad yet"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(5));   // after the mod's own area and zone following has run
            Check(check, PadHere() == StartPad(), "on " + PadHere() + ", the game's start pad here is " + StartPad());
        }

        string laundryResult = "";   // session-scoped: what the laundry step saw, for its check

        // Puts an item in the laundry zone as its child and has the zone see it leave (the zone's own trigger handlers).
        bool LeaveLaundryZone()
        {
            UnpartentOnExitItemCollectionZone unparent = Object.FindObjectOfType<UnpartentOnExitItemCollectionZone>();
            if (unparent == null) { laundryResult = "no laundry zone here"; return false; }
            ItemCollectionZone zone = (ItemCollectionZone)typeof(UnpartentOnExitItemCollectionZone).GetField("itemCollectionZone", AnyField).GetValue(unparent);
            PickupableItem item = null;
            foreach (PickupableItem p in Object.FindObjectsOfType<PickupableItem>())
                if (p.GetComponent<Rigidbody>() != null && !p.IsCurrInHand && (item == null || ItemIdOf(item) != 0)) item = p;   // an unsynced one if any
            if (item == null) { laundryResult = "no loose item to move"; return false; }
            Transform before = item.transform.parent;
            Rigidbody body = item.GetComponent<Rigidbody>();
            BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                if (!zone.ItemsInCollection.Contains(item)) typeof(ItemCollectionZone).GetMethod("RigidbodyEnteredTrigger", any).Invoke(zone, new object[] { body });
                item.transform.parent = unparent.transform;
                typeof(ItemCollectionZone).GetMethod("RigidbodyExitedTrigger", any).Invoke(zone, new object[] { body });
            }
            catch (TargetInvocationException e) { laundryResult = item.name + ": " + e.InnerException.GetType().Name; item.transform.parent = before; return false; }
            bool moved = item.transform.parent != unparent.transform;
            laundryResult = item.name + " left " + unparent.name + ", now under " + (item.transform.parent != null ? item.transform.parent.name : "the scene");
            item.transform.parent = before;
            return moved;
        }

        IEnumerator GarageHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(4));
            yield return StartCoroutine(IntoGarage("garage.host.loose"));
            if (tourFailure != null) yield break;
            BrainControlledModule_Computer computer = null; System.Array points = null;
            yield return StartCoroutine(Wait(() => (points = ComputerPoints(out computer)) != null, 60, "garage.host.loose", "no computer in the garage"));
            LogGarage();
            StartCoroutine(OnStartPad("garage.host.start"));
            if (tourFailure != null) yield break;
            // 1. Only the host's game makes it.
            AttachableItem ram = PointField<AttachableItem>(points.GetValue(1), "workingPart");
            GameObject loose = (GameObject)Object.Instantiate(ram.gameObject, LoosePart, Quaternion.identity);
            Check("garage.host.loose", loose != null, "made a loose " + BaseName(loose.name));
            yield return StartCoroutine(Wait(() => { Peer p = FirstPeer(); return p != null && p.Pose.Scene == "RicksGarage"; }, 120, "garage.host.repaired", "the client never reached the garage"));
            if (tourFailure != null) yield break;
            // 2. Both repair at the agreed host time.
            if (HostTime > RepairAt - 5) { Check("garage.host.repaired", false, "too late for the shared repair: host time " + HostTime.ToString("0")); yield break; }
            yield return StartCoroutine(UntilHostTime(RepairAt));
            computer.SpawnAndAttachComputerParts(null, false);
            yield return StartCoroutine(Pause(6));
            int holding;
            string hold = PointsHold(ComputerPoints(out computer), out holding);
            Check("garage.host.repaired", holding == points.Length, hold);
            // 3. Swap the RAM stick in the computer for the loose one.
            yield return StartCoroutine(UntilHostTime(SwapAt));
            AttachablePoint point = RamPoint();
            AttachableItem was = point.AttachedItems.Count > 0 ? point.AttachedItems[0] : null;
            GrabbableItem looseGrab = SyncedLoose(BaseName(ram.name));
            if (was == null || looseGrab == null) { Check("garage.host.attach", false, (was == null ? "nothing in " + point.name : "") + (looseGrab == null ? " no synced loose " + ram.name : "")); yield break; }
            NetItem wasItem = ItemById(ItemIdOf(was.GetComponent<GrabbableItem>())), looseItem = ItemById(ItemIdOf(looseGrab));
            was.Detach();
            yield return StartCoroutine(Pause(1));
            looseItem.Part.AttachTo(point);
            yield return StartCoroutine(Pause(3));
            Check("garage.host.attach", wasItem != null && Where(wasItem) == "out" && Where(looseItem) == point.name,
                "the game's part (id " + (wasItem != null ? wasItem.Id.ToString("x8") : "none") + ") " + Where(wasItem) + ", the loose one (" + looseItem.Id.ToString("x8") + ") in " + Where(looseItem));
            yield return StartCoroutine(Wait(() => Where(looseItem) == "out", PullAt + 25 - HostTime, "garage.host.detached", () => "the loose part is still in " + Where(looseItem) + ", owner " + looseItem.Owner));
            if (tourFailure != null) yield break;
            Check("garage.host.detached", true, "the client took it out; owner " + looseItem.Owner);
            yield return StartCoroutine(Pause(5));
            Check("tour.done", tourFailure == null, "garage");
        }

        IEnumerator GarageClient()
        {
            RigHead = new Vector3(1.0f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "RicksGarage", 300, "garage.client.loose", "never followed the host into the garage"));
            if (tourFailure != null) yield break;
            BrainControlledModule_Computer computer = null; System.Array points = null;
            yield return StartCoroutine(Wait(() => (points = ComputerPoints(out computer)) != null, 60, "garage.client.loose", "no computer in the garage"));
            if (tourFailure != null) yield break;
            LogGarage();
            StartCoroutine(OnStartPad("garage.client.start"));
            string ram = PointField<AttachableItem>(points.GetValue(1), "workingPart").name;
            int removedAtStart = SpawnsRemoved;
            // 1. The host's loose part (a copy waits up to 30 s for this game's own before using the prefab).
            yield return StartCoroutine(Wait(() => LooseNear(ram, LoosePart) == 1, 60, "garage.client.loose", () => LooseNear(ram, LoosePart) + " loose " + ram + " near the host's"));
            if (tourFailure != null) yield break;
            Check("garage.client.loose", true, "the host's loose " + ram + " is here");
            // Errors from here on (the headless scene load's fade errors come before).
            int errorsAtStart = errorCount;
            // 2. Both repair.
            if (HostTime > RepairAt - 5) { Check("garage.client.repaired", false, "too late for the shared repair: host time " + HostTime.ToString("0")); yield break; }
            int copiedAtStart = SpawnsCopied;
            yield return StartCoroutine(UntilHostTime(RepairAt + 4));
            computer.SpawnAndAttachComputerParts(null, false);
            yield return StartCoroutine(Pause(8));
            int holding;
            string hold = PointsHold(ComputerPoints(out computer), out holding);
            Check("garage.client.repaired", holding == points.Length && SpawnsRemoved == removedAtStart && SpawnsCopied == copiedAtStart, hold + ", copied by the mod " + (SpawnsCopied - copiedAtStart) + ", removed " + (SpawnsRemoved - removedAtStart));
            // 3. The host's swap shows here; then this player takes the loose one out.
            AttachablePoint point = RamPoint();
            AttachableItem was = point.AttachedItems.Count > 0 ? point.AttachedItems[0] : null;
            GrabbableItem looseGrab = SyncedLoose(ram);
            NetItem wasItem = was != null ? ItemById(ItemIdOf(was.GetComponent<GrabbableItem>())) : null, looseItem = looseGrab != null ? ItemById(ItemIdOf(looseGrab)) : null;
            if (wasItem == null || looseItem == null) { Check("garage.client.attach", false, (wasItem == null ? "the part in " + point.name + " is not synced" : "") + (looseItem == null ? " no synced loose " + ram : "")); yield break; }
            yield return StartCoroutine(Wait(() => Where(wasItem) == "out" && Where(looseItem) == point.name, SwapAt + 20 - HostTime, "garage.client.attach", () => "the game's part " + Where(wasItem) + ", the loose one " + Where(looseItem)));
            if (tourFailure != null) yield break;
            Check("garage.client.attach", true, "the game's part out, the loose one in " + point.name + ", as on the host");
            yield return StartCoroutine(UntilHostTime(PullAt));
            ClaimForTour(looseItem);
            yield return StartCoroutine(Wait(() => looseItem.Confirmed && looseItem.Owner == LocalId, 10, "garage.client.detach", "the host never granted the claim"));
            if (tourFailure != null) yield break;
            looseItem.Part.Detach();
            Check("garage.client.detach", Where(looseItem) == "out", "took the loose one out here");
            yield return StartCoroutine(Pause(4));
            Check("garage.client.laundry", LeaveLaundryZone(), laundryResult);
            Check("garage.client.errors", errorCount == errorsAtStart, (errorCount - errorsAtStart) + " game errors since the copy");
            Check("tour.done", tourFailure == null, "garage");
        }
    }
}
