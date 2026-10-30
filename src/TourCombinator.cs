using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "combinator": the garage's combinator (CombinatorController) fuses the items on its two pads when its Go button
    // is pushed. Both copies pick the same pair (the first synced pair it can combine, by id), the host puts them on the
    // pads, the client's copies must land in the client's pads too, and the host pushes Go with its hand. The button
    // press is shared, so both games combine their own copies: the client must adopt the host's result (one result, not
    // two, none removed) and show no game errors.
    public sealed partial class TogetherRuntime
    {
        const float CombineAt = 150;   // host time of the push: both copies are in the garage and the pair is on the pads

        static CombinatorController Combinator()
        {
            foreach (CombinatorController c in Resources.FindObjectsOfTypeAll<CombinatorController>())
                if (c.gameObject.scene.isLoaded && c.gameObject.activeInHierarchy) return c;
            return null;
        }

        static T CombinatorField<T>(CombinatorController c, string field) { return (T)typeof(CombinatorController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c); }

        // The same pair on every copy: synced, loose, not something the game made, ordered by id.
        bool CombinablePair(CombinatorController c, out NetItem left, out NetItem right)
        {
            left = right = null;
            List<NetItem> loose = new List<NetItem>();
            foreach (NetItem item in items.Values)
                if (item.Grab != null && item.Body != null && item.Control == null && !item.Spawned && !item.PointPart && item.Grab.GetComponent<WorldItem>() != null && !item.Grab.IsCurrInHand) loose.Add(item);
            loose.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (NetItem a in loose)
                foreach (NetItem b in loose)
                    if (a != b && c.TestCombination(a.Grab.GetComponent<WorldItem>().Data, b.Grab.GetComponent<WorldItem>().Data)) { left = a; right = b; return true; }
            return false;
        }

        static Vector3 PadSpot(ItemCollectionZone zone) { Collider col = zone.GetComponent<Collider>(); return col != null ? col.bounds.center : zone.transform.position; }

        static bool OnPad(ItemCollectionZone zone, NetItem item) { return item.Grab != null && zone.ItemsInCollection.Contains(item.Grab.GetComponent<PickupableItem>()); }

        IEnumerator CombinatorHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(4));
            yield return StartCoroutine(IntoGarage("combinator.host.found"));
            if (tourFailure != null) yield break;
            CombinatorController c = null;
            yield return StartCoroutine(Wait(() => (c = Combinator()) != null, 60, "combinator.host.found", "no combinator out in the garage"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(5));
            NetItem a, b;
            if (!CombinablePair(c, out a, out b)) { Check("combinator.host.found", false, "no synced pair here combines"); yield break; }
            Check("combinator.host.found", true, a.Grab.name + " + " + b.Grab.name + " at " + V(c.transform.position));
            labWatched.Add(a.Id); labWatched.Add(b.Id);
            yield return StartCoroutine(Wait(() => { Peer p = FirstPeer(); return p != null && p.Pose.Scene == "RicksGarage"; }, 120, "combinator.host.result", "the client never reached the garage"));
            if (tourFailure != null) yield break;
            // Onto the pads, as a hand would drop them.
            ItemCollectionZone leftZone = CombinatorField<ItemCollectionZone>(c, "collectionZoneLeft"), rightZone = CombinatorField<ItemCollectionZone>(c, "collectionZoneRight");
            foreach (KeyValuePair<NetItem, ItemCollectionZone> put in new[] { new KeyValuePair<NetItem, ItemCollectionZone>(a, leftZone), new KeyValuePair<NetItem, ItemCollectionZone>(b, rightZone) })
            {
                put.Key.Body.velocity = Vector3.zero; put.Key.Body.position = PadSpot(put.Value) + Vector3.up * 0.1f;
                put.Key.LastMoved = Time.realtimeSinceStartup; put.Key.SentRest = false;
            }
            yield return StartCoroutine(Wait(() => OnPad(leftZone, a) && OnPad(rightZone, b), 10, "combinator.host.result", () => "on the pads: " + OnPad(leftZone, a) + " " + OnPad(rightZone, b)));
            if (tourFailure != null) yield break;
            if (HostTime > CombineAt - 5) { Check("combinator.host.result", false, "too late for the push: host time " + HostTime.ToString("0")); yield break; }
            yield return StartCoroutine(UntilHostTime(CombineAt));
            int announced = SpawnsAnnounced;
            PushableButton go = CombinatorField<PushableButton>(c, "goButton");
            RigHead = go.transform.position + new Vector3(0, 0.3f, -0.4f);
            yield return StartCoroutine(PushButton(Handedness.Right, go, "combinator.host.result"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(8));
            Check("combinator.host.result", SpawnsAnnounced > announced || a.Grab == null || b.Grab == null,
                "announced " + (SpawnsAnnounced - announced) + " new; " + a.Key + (a.Grab == null ? " used up" : " left") + ", " + b.Key + (b.Grab == null ? " used up" : " left"));
            yield return StartCoroutine(Pause(10));
            Check("tour.done", tourFailure == null, "combinator");
        }

        IEnumerator CombinatorClient()
        {
            RigHead = new Vector3(1.0f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "RicksGarage", 300, "combinator.client.pads", "never followed the host into the garage"));
            if (tourFailure != null) yield break;
            CombinatorController c = null;
            yield return StartCoroutine(Wait(() => (c = Combinator()) != null, 60, "combinator.client.pads", "no combinator out in the garage"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(5));
            NetItem a, b;
            if (!CombinablePair(c, out a, out b)) { Check("combinator.client.pads", false, "no synced pair here combines"); yield break; }
            ItemCollectionZone leftZone = CombinatorField<ItemCollectionZone>(c, "collectionZoneLeft"), rightZone = CombinatorField<ItemCollectionZone>(c, "collectionZoneRight");
            // The host's copies land on its pads: this copy's followers must land on this copy's pads.
            yield return StartCoroutine(Wait(() => OnPad(leftZone, a) && OnPad(rightZone, b), CombineAt - 2 - HostTime, "combinator.client.pads", () => a.Grab.name + " on the left pad " + OnPad(leftZone, a) + ", " + b.Grab.name + " on the right " + OnPad(rightZone, b)));
            if (tourFailure != null) yield break;
            Check("combinator.client.pads", true, a.Grab.name + " + " + b.Grab.name + " on this copy's pads");
            int errorsAtStart = errorCount, adopted = SpawnsAdopted, copied = SpawnsCopied, removed = SpawnsRemoved;
            yield return StartCoroutine(UntilHostTime(CombineAt + 14));
            Check("combinator.client.result", SpawnsAdopted + SpawnsCopied > adopted + copied || a.Grab == null || b.Grab == null,
                "adopted " + (SpawnsAdopted - adopted) + ", copied " + (SpawnsCopied - copied) + ", removed " + (SpawnsRemoved - removed) + "; " + a.Key + (a.Grab == null ? " used up" : " left") + ", " + b.Key + (b.Grab == null ? " used up" : " left"));
            yield return StartCoroutine(Pause(4));
            Check("combinator.client.single", SpawnsRemoved == removed && localSpawns.Count == 0, (SpawnsRemoved - removed) + " removed as made only here, " + localSpawns.Count + " still unmatched");
            Check("combinator.client.errors", errorCount == errorsAtStart, (errorCount - errorsAtStart) + " game errors since the pads");
            Check("tour.done", tourFailure == null, "combinator");
        }
    }
}
