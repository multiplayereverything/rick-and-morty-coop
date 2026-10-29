using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "garage": the computer's parts, made by the game's own BrainControlledModule_Computer.SpawnAndAttachComputerParts
    // (broken ones when the garage loads, working ones when the story repairs it).
    // 1. Right after the host arrives, its game makes a loose working RAM stick (from the part's prefab): the client, which
    //    has none out, must still get one.
    // 2. Both copies repair the computer (what the shared story does on each), the client 4 s after the host (a slower
    //    machine): each keeps its own attached parts; the mod copies and removes none of them.
    public sealed partial class TogetherRuntime
    {
        const BindingFlags AnyField = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const float RepairAt = 170;   // host time of the shared repair: after both copies are in and the copied part is here
        static readonly Vector3 LoosePart = new Vector3(0.6f, 1.0f, -0.6f);   // process-scoped (a constant)

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
            yield return StartCoroutine(Pause(15));
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
            Check("garage.client.errors", errorCount == errorsAtStart, (errorCount - errorsAtStart) + " game errors since the copy");
            Check("tour.done", tourFailure == null, "garage");
        }
    }
}
