using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OwlchemyVR;
using OwlchemyVR.Teleportation;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "laundry": the garage intro's laundry task (stage LaundryTask). Rick dumps the laundry (the game's LaundrySpawner
    // on each copy, adopted as one set); the host puts every piece in the washing machine, closes its door and pushes its
    // button. The client's copies must land in its own washer, its door must close, and its washer must run on the
    // shared press: the same pieces wet on both, the same stage, no game errors on the client.
    public sealed partial class TogetherRuntime
    {
        const float WashAt = 260;   // host time of the push: the laundry is out and in both washers
        const float ReviveAt = 300;
        const float TeleportAt = 345;
        const float WatchAt = 395;   // host time the client puts Rick's smartwatch on its own wrist
        const float FixAt = 440;   // host time of the first part swap in the computer   // host time the client teleports to the desk pad (the teleport lesson)   // host time the host answers its death phone (the intro's death follows the wash)

        static DeathPhoneController DeathPhone()
        {
            foreach (DeathPhoneController d in Resources.FindObjectsOfTypeAll<DeathPhoneController>()) if (d.gameObject.scene.isLoaded) return d;
            return null;
        }

        // The death phone's respawn button and handset: this player's own, not synced.
        string DeathPhoneOwn(out PushableButton button)
        {
            DeathPhoneController phone = DeathPhone();
            button = phone != null ? (PushableButton)typeof(DeathPhoneController).GetField("pushableButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(phone) : null;
            if (button == null) return "no death phone";
            NetTrigger t;
            DeathPhoneHandset handset = Object.FindObjectOfType<DeathPhoneHandset>();
            uint handsetId = handset != null ? ItemIdOf(handset.GetComponent<GrabbableItem>()) : 0;
            bool buttonSynced = triggerOf.TryGetValue(button, out t) && t != null;
            return buttonSynced || handsetId != 0 ? "synced: button " + buttonSynced + ", handset id " + handsetId.ToString("x8") : "";
        }

        static string ComputerHolds()
        {
            BrainControlledModule_Computer c;
            System.Array points = ComputerPoints(out c);
            if (points == null) return "none";
            List<string> s = new List<string>();
            foreach (object p in points) { List<AttachableItem> held = PointField<AttachablePoint>(p, "attachPoint").AttachedItems; s.Add(held.Count > 0 && held[0] != null ? BaseName(held[0].name) : "empty"); }
            return string.Join(" ", s.ToArray());
        }

        // A part for the computer's point: synced, loose or in a holder outside the computer, fits, not a broken one.
        // The same choice on every copy (by id).
        NetItem PartFor(AttachablePoint point, System.Array points, WorldItemData[] invalid)
        {
            List<AttachablePoint> vital = new List<AttachablePoint>();
            foreach (object p in points) vital.Add(PointField<AttachablePoint>(p, "attachPoint"));
            NetItem best = null;
            foreach (NetItem item in items.Values)
            {
                if (item.Grab == null || item.Part == null || item.Grab.IsCurrInHand) continue;
                if (item.Part.CurrentlyAttachedTo != null && vital.Contains(item.Part.CurrentlyAttachedTo)) continue;
                WorldItem w = item.Grab.GetComponent<WorldItem>();
                if (w == null || System.Array.IndexOf(invalid, w.Data) >= 0 || !item.Part.HasCompatibleDataFor(point)) continue;
                if (best == null || item.Id < best.Id) best = item;
            }
            return best;
        }

        // Takes the broken part out of the computer's point and puts a working one in, as this copy's player.
        IEnumerator SwapPart(int index, float at, string check)
        {
            yield return StartCoroutine(UntilHostTime(at));
            BrainControlledModule_Computer c;
            System.Array points = ComputerPoints(out c);
            WorldItemData[] invalid = (WorldItemData[])typeof(BrainControlledModule_Computer).GetField("_invalidAttachableItems", AnyField).GetValue(c);
            AttachablePoint point = PointField<AttachablePoint>(points.GetValue(index), "attachPoint");
            AttachableItem brokenPart = point.AttachedItems.Count > 0 ? point.AttachedItems[0] : null;
            NetItem broken = brokenPart != null ? ItemById(ItemIdOf(brokenPart.GetComponent<GrabbableItem>())) : null;
            NetItem part = null;
            // The working parts may come later in the task (the story hands them out): wait for one.
            float until = Time.realtimeSinceStartup + 60;
            while ((part = PartFor(point, points, invalid)) == null && Time.realtimeSinceStartup < until) yield return StartCoroutine(Pause(1));
            if (part == null)
            {
                foreach (AttachableItem a in Resources.FindObjectsOfTypeAll<AttachableItem>())
                {
                    if (!a.gameObject.scene.isLoaded || !a.HasCompatibleDataFor(point)) continue;
                    WorldItem w = a.GetComponent<WorldItem>();
                    Log("Computer: fits " + point.name + ": " + KeyOf(a.transform) + " active " + a.gameObject.activeInHierarchy + " id " + ItemIdOf(a.GetComponent<GrabbableItem>()).ToString("x8") + " body " + (a.GetComponent<Rigidbody>() != null) + " invalid " + (w != null && System.Array.IndexOf(invalid, w.Data) >= 0) + (a.CurrentlyAttachedTo != null ? " in " + a.CurrentlyAttachedTo.name : ""));
                }
                // Where one could come from: loaded prefabs that fit, and combinator recipes that make one.
                foreach (AttachableItem a in Resources.FindObjectsOfTypeAll<AttachableItem>())
                    if (!a.gameObject.scene.isLoaded && a.HasCompatibleDataFor(point)) Log("Computer: prefab fits " + point.name + ": " + a.name);
                CombinatorController comb = Combinator();
                if (comb != null)
                {
                    CombinatorDataMatrix matrix = CombinatorField<CombinatorDataMatrix>(comb, "combinatorDataMatrix");
                    foreach (RecipeCombination r in CombinatorField<List<RecipeCombination>>(comb, "recipes"))
                    {
                        AttachableItem made = r.Prefab != null ? r.Prefab.GetComponentInChildren<AttachableItem>(true) : null;
                        if (made == null || !made.HasCompatibleDataFor(point)) continue;
                        List<string> pairs = new List<string>();
                        foreach (WorldItemData x in comb.AllWorldItemData)
                            foreach (WorldItemData y in comb.AllWorldItemData)
                            {
                                if (x == null || y == null || pairs.Count >= 5) continue;
                                CombinatorResult res = matrix.GetCombinatorResult(x, y);
                                if (res != null && res.outcome == r.OutcomeData) pairs.Add(x.name + " + " + y.name);
                            }
                        Log("Computer: combinator makes " + r.Prefab.name + " for " + point.name + " from " + string.Join("; ", pairs.ToArray()));
                    }
                }
                Check(check, false, "nothing here fits " + point.name); tourFailure = check; yield break;
            }
            Log("Computer: " + point.name + " gets " + part.Grab.name + " (" + part.Id.ToString("x8") + ") for " + (brokenPart != null ? brokenPart.name : "nothing"));
            if (!Hosting)
            {
                if (broken != null) ClaimForTour(broken);
                ClaimForTour(part);
                yield return StartCoroutine(Wait(() => part.Confirmed && (broken == null || broken.Confirmed), 10, check, "the host never granted the parts"));
                if (tourFailure != null) yield break;
            }
            if (brokenPart != null) { brokenPart.Detach(); brokenPart.transform.position += Vector3.up * 0.3f + Vector3.left * 0.4f; }
            yield return StartCoroutine(Pause(1));
            if (part.Part.CurrentlyAttachedTo != null) part.Part.Detach();
            part.Part.AttachTo(point);
            yield return StartCoroutine(Pause(2));
        }

        static SmartWatchController Watch() { return Object.FindObjectOfType<SmartWatchController>(); }

        AttachablePoint OwnWrist()
        {
            foreach (HandController h in Master.Hands) if (h.Handedness == Handedness.Left) return h.GetComponent<SmartWatchAttachPointPointer>().AttachPoint;
            return null;
        }

        // The wearer answers each of Rick's calls (looking at the watch runs the game's JoinCall).
        IEnumerator AnswerCalls(SmartWatchController watch)
        {
            FieldInfo ringing = typeof(SmartWatchController).GetField("_notificationCoroutine", AnyField);
            while (watch != null && tourFailure == null)
            {
                if (!watch.IsInCall && ringing.GetValue(watch) != null) { Log("Watch: answering Rick's call"); watch.JoinCall(); }
                yield return StartCoroutine(Pause(2));
            }
        }

        static bool Dead() { return DeathController.Instance != null && DeathController.Instance.PlayerIsDead; }

        // After the wash the story has Rick shoot the player (each copy's own death); each answers its own death phone.
        IEnumerator ReviveAlone(string role, float at, bool otherStillDead)
        {
            yield return StartCoroutine(Wait(Dead, 40, "laundry." + role + ".died", "the intro's death never came"));
            if (tourFailure != null) yield break;
            PushableButton button;
            string own = DeathPhoneOwn(out button);
            Check("laundry." + role + ".died", own.Length == 0, own.Length == 0 ? "dead, with this player's own death phone" : own);
            if (own.Length > 0) yield break;
            yield return StartCoroutine(UntilHostTime(at));
            // The other player answered first: this one must still be dead.
            if (otherStillDead) Check("laundry." + role + ".own", Dead(), Dead() ? "still dead after the other player respawned" : "respawned with the other player");
            PressButton(button);
            yield return StartCoroutine(Wait(() => !Dead(), 20, "laundry." + role + ".revived", "still dead after the death phone"));
            if (tourFailure != null) yield break;
            Check("laundry." + role + ".revived", true, "back from the death phone");
        }

        static WashingMachineController Washer()
        {
            foreach (WashingMachineController w in Resources.FindObjectsOfTypeAll<WashingMachineController>())
                if (w.gameObject.scene.isLoaded && w.gameObject.activeInHierarchy) return w;
            return null;
        }

        static T WasherField<T>(WashingMachineController w, string field) { return (T)typeof(WashingMachineController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w); }

        // The synced laundry pieces out now, by id (the same list on every copy once adopted).
        List<NetItem> Laundry()
        {
            List<NetItem> list = new List<NetItem>();
            foreach (NetItem item in items.Values) if (item.Grab != null && item.Body != null && item.Grab.GetComponent<LaundryItem>() != null) list.Add(item);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        static int InWasher(ItemCollectionZone zone, List<NetItem> laundry) { int n = 0; foreach (NetItem item in laundry) if (OnPad(zone, item)) n++; return n; }

        static string States(List<NetItem> laundry)
        {
            List<string> s = new List<string>();
            foreach (NetItem item in laundry) s.Add(item.Grab != null ? item.Grab.GetComponent<LaundryItem>().state.ToString() : "gone");
            return string.Join(" ", s.ToArray());
        }

        // Lab: what the washer still wants for the intro, and the story's laundry and washer triggers.
        void LogLaundryStory(WashingMachineController w)
        {
            List<string> left = new List<string>();
            foreach (WorldItemData d in WasherField<List<WorldItemData>>(w, "worldItemsRequiredToCompleteIntro")) left.Add(d != null ? d.name : "-");
            List<string> causes = new List<string>();
            foreach (string k in CauseKeys()) if (k.ToUpperInvariant().Contains("LAUNDRY") || k.ToUpperInvariant().Contains("WASH") || k.Contains("ADDED_TO")) causes.Add(k);
            Log("Laundry: washer still wants [" + string.Join(", ", left.ToArray()) + "]; story causes: " + string.Join(" | ", causes.ToArray()));
        }

        IEnumerator LaundryHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(4));
            yield return StartCoroutine(IntoGarage("laundry.host.out"));
            if (tourFailure != null) yield break;
            WashingMachineController w = null;
            yield return StartCoroutine(Wait(() => (w = Washer()) != null, 60, "laundry.host.out", "no washing machine out in the garage"));
            if (tourFailure != null) yield break;
            Log("Laundry: stage " + CurrentStageName());
            // Rick dumps it during his opening lines.
            yield return StartCoroutine(Wait(() => Laundry().Count > 0, 180, "laundry.host.out", () => "no laundry out yet, stage " + CurrentStageName()));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(10));   // the rest of the pile, and the client's adoption
            List<NetItem> laundry = Laundry();
            Check("laundry.host.out", true, laundry.Count + " pieces, stage " + CurrentStageName());
            ItemCollectionZone zone = WasherField<ItemCollectionZone>(w, "itemCollectionZone");
            GrabbableHinge door = w.DoorHinge;
            // In through the open door, one by one, picked up and dropped by the hand as a player does.
            if (door.IsUpperLocked) door.LockLower();
            Vector3 drop = PadSpot(zone) + Vector3.up * 0.25f;
            foreach (NetItem item in laundry)
            {
                RigHead = item.Body.worldCenterOfMass + new Vector3(0, 0.45f, -0.45f);
                yield return StartCoroutine(PickUp(Handedness.Right, item, "laundry.host.washed"));
                if (tourFailure != null) yield break;
                if (!item.Grab.IsCurrInHand) { Check("laundry.host.washed", false, "could not pick up " + item.Grab.name + ": " + DescribeHand(Handedness.Right)); yield break; }
                RigHead = drop + new Vector3(0, 0.3f, -0.5f);
                yield return StartCoroutine(Reach(Handedness.Right, () => drop, 0.05f, 15, "laundry.host.washed"));
                if (tourFailure != null) yield break;
                RigGrab(Handedness.Right, false);
                yield return StartCoroutine(Pause(1.5f));
                if (!OnPad(zone, item)) { item.Body.velocity = Vector3.zero; item.Body.position = PadSpot(zone) + Vector3.up * 0.05f; item.LastMoved = Time.realtimeSinceStartup; item.SentRest = false; Log("Laundry: " + item.Grab.name + " missed the washer; put in"); }
            }
            yield return StartCoroutine(Wait(() => InWasher(zone, laundry) == laundry.Count, 10, "laundry.host.washed", () => InWasher(zone, laundry) + " of " + laundry.Count + " in the washer"));
            if (tourFailure != null) yield break;
            door.LockUpper();
            yield return StartCoroutine(Pause(2));
            if (HostTime > WashAt - 5) { Check("laundry.host.washed", false, "too late for the push: host time " + HostTime.ToString("0")); yield break; }
            yield return StartCoroutine(UntilHostTime(WashAt));
            PushableButton button = WasherField<PushableButton[]>(w, "buttons")[0];
            RigHead = button.transform.position + new Vector3(0, 0.3f, -0.4f);
            yield return StartCoroutine(PushButton(Handedness.Right, button, "laundry.host.washed"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(8));
            Check("laundry.host.washed", States(laundry).Contains("Wet") && !States(laundry).Contains("Dirty"), States(laundry) + ", stage " + CurrentStageName());
            LogLaundryStory(w);
            yield return StartCoroutine(ReviveAlone("host", ReviveAt, false));
            if (tourFailure != null) yield break;
            // What a player does next: the story takes it from the wash (Rick's lines, then the next stage).
            yield return StartCoroutine(Wait(() => CurrentStageName() != "LaundryTask", 120, "laundry.host.next", () => "still on " + CurrentStageName()));
            LogLaundryStory(w);
            if (tourFailure != null) yield break;
            Check("laundry.host.next", true, "stage " + CurrentStageName());
            // The client's respawn (out of its own death area) is its own, not a story event here.
            yield return StartCoroutine(UntilHostTime(ReviveAt + 15));
            Check("laundry.host.ownrespawn", DeathTeleportsFromClients == 0, DeathTeleportsFromClients + " death-area teleports from the client ran here");
            yield return StartCoroutine(Wait(() => CurrentStageName() != "TeleportTask", TeleportAt + 60 - HostTime, "laundry.host.teleport", () => "still on " + CurrentStageName()));
            if (tourFailure != null) yield break;
            Check("laundry.host.teleport", true, "the client's teleport moved the story here to " + CurrentStageName());
            // 6. The client's watch: not on this player's wrist, with the client's hand.
            yield return StartCoroutine(UntilHostTime(WatchAt + 8));
            SmartWatchController hostWatch = Watch();
            Peer wearer = FirstPeer();
            float fromHand = hostWatch != null && wearer != null ? (hostWatch.Attachable.transform.position - wearer.Pose.Left.Position).magnitude : -1;
            Check("laundry.host.watch", hostWatch != null && hostWatch.Attachable.CurrentlyAttachedTo == null && fromHand >= 0 && fromHand < 0.4f,
                "watch " + (hostWatch == null ? "missing" : hostWatch.Attachable.CurrentlyAttachedTo != null ? "in " + hostWatch.Attachable.CurrentlyAttachedTo.name : "loose") + ", " + fromHand.ToString("0.00") + " m from the client's left hand");
            // 7. The computer: the host swaps in the crystal and the fuse, the client the RAM stick (any part that fits and is
            //    not a broken one). The story's COMPUTER_REPAIRED moves both on.
            yield return StartCoroutine(SwapPart(0, FixAt, "laundry.host.fix"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(SwapPart(2, FixAt + 20, "laundry.host.fix"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => CurrentStageName() != "FixComputerTask", 120, "laundry.host.fix", () => "still on " + CurrentStageName() + ", computer " + ComputerHolds()));
            if (tourFailure != null) yield break;
            Check("laundry.host.fix", true, "computer " + ComputerHolds() + ": stage " + CurrentStageName());
            yield return StartCoroutine(Pause(15));
            Check("tour.done", tourFailure == null, "laundry");
        }

        IEnumerator LaundryClient()
        {
            RigHead = new Vector3(1.0f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "RicksGarage", 300, "laundry.client.in", "never followed the host into the garage"));
            if (tourFailure != null) yield break;
            WashingMachineController w = null;
            yield return StartCoroutine(Wait(() => (w = Washer()) != null, 60, "laundry.client.in", "no washing machine out in the garage"));
            if (tourFailure != null) yield break;
            ItemCollectionZone zone = WasherField<ItemCollectionZone>(w, "itemCollectionZone");
            List<NetItem> laundry = null;
            // The host's pieces land in its washer: this copy's must land in this copy's, and its door must close.
            yield return StartCoroutine(Wait(() => (laundry = Laundry()).Count > 0 && InWasher(zone, laundry) == laundry.Count && w.DoorHinge.IsUpperLocked, WashAt - 2 - HostTime, "laundry.client.in",
                () => (laundry != null ? InWasher(zone, laundry) + " of " + laundry.Count : "no laundry") + " in the washer, door closed " + w.DoorHinge.IsUpperLocked));
            if (tourFailure != null) yield break;
            Check("laundry.client.in", true, laundry.Count + " pieces in this copy's washer, door closed");
            int errorsAtStart = errorCount, removed = SpawnsRemoved;
            yield return StartCoroutine(UntilHostTime(WashAt + 14));
            // Washed here (wet), or already taken away by the story's next step (the game removes the washed laundry).
            Check("laundry.client.washed", !States(laundry).Contains("Dirty") && !States(laundry).Contains("Clean"), States(laundry));
            yield return StartCoroutine(ReviveAlone("client", ReviveAt + 6, true));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => CurrentStageName() != "LaundryTask" && CurrentStageName() == hostStage, 130, "laundry.client.stage", () => "here " + CurrentStageName() + ", host " + hostStage));
            if (tourFailure != null) yield break;
            Check("laundry.client.stage", true, "here " + CurrentStageName() + ", as on the host");
            // 5. The teleport lesson, done by this player: teleport to the desk pad; the story moves on for both.
            yield return StartCoroutine(UntilHostTime(TeleportAt));
            TeleportZone desk = null;
            foreach (TeleportZone z in Object.FindObjectsOfType<TeleportZone>()) if (z.UniqueObjectData != null && z.UniqueObjectData.name == "TeleportZone_GarageDesk") desk = z;
            if (desk == null) { Check("laundry.client.teleport", false, "no desk pad here"); yield break; }
            GlobalStorage.Instance.TeleportationController.Teleport(desk, false, true, true, true);
            yield return StartCoroutine(Wait(() => CurrentStageName() != "TeleportTask" && CurrentStageName() == hostStage, 90, "laundry.client.teleport", () => "here " + CurrentStageName() + ", host " + hostStage));
            if (tourFailure != null) yield break;
            Check("laundry.client.teleport", true, "teleported to the desk: stage " + CurrentStageName() + " on both");
            // 6. Rick's smartwatch onto this player's own wrist (what the computer task waits for).
            yield return StartCoroutine(UntilHostTime(WatchAt));
            SmartWatchController watch = Watch();
            NetItem watchItem = watch != null ? ItemById(ItemIdOf(watch.Attachable.GetComponent<GrabbableItem>())) : null;
            if (watchItem == null) { Check("laundry.client.watch", false, watch == null ? "no smartwatch here" : "the smartwatch is not synced"); yield break; }
            ClaimForTour(watchItem);
            yield return StartCoroutine(Wait(() => watchItem.Confirmed, 10, "laundry.client.watch", "the host never granted the watch"));
            if (tourFailure != null) yield break;
            if (watch.Attachable.CurrentlyAttachedTo != null) watch.Attachable.Detach();
            watch.Attachable.AttachTo(OwnWrist());
            yield return StartCoroutine(Pause(2));
            Check("laundry.client.watch", watch.Attachable.CurrentlyAttachedTo == OwnWrist(), "on " + (watch.Attachable.CurrentlyAttachedTo != null ? watch.Attachable.CurrentlyAttachedTo.name : "nothing"));
            // Rick calls the watch; its wearer answers by looking at it (the game's own JoinCall, which the gaze runs).
            yield return StartCoroutine(Wait(() => typeof(SmartWatchController).GetField("_callState", AnyField).GetValue(watch) != null || typeof(SmartWatchController).GetField("_nextCallState", AnyField).GetValue(watch) != null, 30, "laundry.client.call", "Rick never called"));
            if (tourFailure != null) yield break;
            Check("laundry.client.call", true, "Rick is calling");
            StartCoroutine(AnswerCalls(watch));
            yield return StartCoroutine(SwapPart(1, FixAt + 10, "laundry.client.fix"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => CurrentStageName() != "FixComputerTask" && CurrentStageName() == hostStage, 240, "laundry.client.fix", () => "here " + CurrentStageName() + ", host " + hostStage + ", computer " + ComputerHolds()));
            if (tourFailure != null) yield break;
            Check("laundry.client.fix", true, "computer " + ComputerHolds() + ": stage " + CurrentStageName() + " on both");
            Check("laundry.client.errors", errorCount == errorsAtStart && SpawnsRemoved == removed, (errorCount - errorsAtStart) + " game errors, " + (SpawnsRemoved - removed) + " removed as made only here");
            Check("tour.done", tourFailure == null, "laundry");
        }
    }
}
