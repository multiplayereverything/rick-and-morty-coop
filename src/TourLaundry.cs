using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OwlchemyVR;
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
            // In through the open door, one by one, as a hand would drop them.
            if (door.IsUpperLocked) door.LockLower();
            foreach (NetItem item in laundry)
            {
                item.Body.velocity = Vector3.zero; item.Body.position = PadSpot(zone) + Vector3.up * 0.05f;
                item.LastMoved = Time.realtimeSinceStartup; item.SentRest = false;
                yield return StartCoroutine(Pause(0.5f));
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
            Check("laundry.client.washed", States(laundry).Contains("Wet") && !States(laundry).Contains("Dirty"), States(laundry));
            Check("laundry.client.stage", hostStage.Length == 0 || CurrentStageName() == hostStage, "here " + CurrentStageName() + ", host " + hostStage);
            Check("laundry.client.errors", errorCount == errorsAtStart && SpawnsRemoved == removed, (errorCount - errorsAtStart) + " game errors, " + (SpawnsRemoved - removed) + " removed as made only here");
            Check("tour.done", tourFailure == null, "laundry");
        }
    }
}
