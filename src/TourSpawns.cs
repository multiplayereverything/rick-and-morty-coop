using System.Collections;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "spawns": things the game makes while playing, made here with Object.Instantiate of a comic book (what a
    // machine or dispenser does). The two copies act at agreed moments of the shared host clock:
    // 1. only the host makes one: the client makes a copy where the host's is;
    // 2. both make one at the same spot: the client adopts its own, so there is one there, not two;
    // 3. only the client makes one: it is removed (the host's game did not make it);
    // 4. the host destroys the first one: it goes on the client too.
    public sealed partial class TogetherRuntime
    {
        static readonly Vector3 SpawnA = new Vector3(0.2f, 0.6f, -0.3f), SpawnB = new Vector3(0.9f, 0.6f, -0.3f), SpawnC = new Vector3(1.6f, 0.6f, -0.3f);   // process-scoped (constants)

        GameObject MakeLikeComic(Vector3 at)
        {
            NetItem comic = FindItem(HandoffItem);
            return comic == null ? null : (GameObject)Object.Instantiate(comic.Grab.gameObject, at, Quaternion.identity);
        }

        // Made comics within 30 cm of a spot, seen from above (they fall to the floor after being made).
        int ComicsNear(Vector3 at)
        {
            int n = 0;
            foreach (GrabbableItem g in Object.FindObjectsOfType<GrabbableItem>())
            {
                Vector3 d = g.transform.position - at; d.y = 0;
                if (BaseName(g.name) == HandoffItem && g.name.Contains("(Clone)") && d.magnitude < 0.3f) n++;
            }
            return n;
        }

        IEnumerator UntilHostTime(float t) { while (HostTime < t) yield return null; }

        static float Slot(float after) { return Mathf.Round(after / 10f) * 10f + 10f; }

        IEnumerator SpawnsHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.9f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1 && FindItem(HandoffItem) != null, 120, "spawns.host.announced", "no client, or no comic book"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(5));
            // 1. Only the host.
            GameObject a = MakeLikeComic(SpawnA);
            yield return StartCoroutine(Wait(() => SpawnsAnnounced >= 1, 5, "spawns.host.announced", "never announced"));
            if (tourFailure != null) yield break;
            // Both copies time the next steps from the announcement (the client gets it within milliseconds).
            float t1 = LastSpawnAnnounce;
            Check("spawns.host.announced", true, "announced at host time " + t1.ToString("0.00"));
            // 2. Both, at the agreed moment.
            float t2 = Slot(t1 + 6);
            yield return StartCoroutine(UntilHostTime(t2));
            MakeLikeComic(SpawnB);
            // 3. The client alone (nothing to do here), then 4. destroy the first.
            float t4 = t2 + 20;
            yield return StartCoroutine(UntilHostTime(t4));
            Object.Destroy(a);
            yield return StartCoroutine(Pause(1.5f));
            Check("spawns.host.despawned", Despawns >= 1, "despawns sent " + Despawns);
            Check("spawns.host.one", ComicsNear(SpawnB) == 1, ComicsNear(SpawnB) + " made comics at the shared spot here");
            yield return StartCoroutine(Pause(6));
            Check("tour.done", tourFailure == null, "spawns");
        }

        IEnumerator SpawnsClient()
        {
            RigHead = new Vector3(1.0f, 1.35f, -0.9f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0 && FindItem(HandoffItem) != null, 120, "tour.joined", "never welcomed, or no comic book"));
            if (tourFailure != null) yield break;
            // 1. The host's alone: a copy appears here.
            yield return StartCoroutine(Wait(() => SpawnsCopied >= 1, 60, "spawns.client.copied", "the host's new item never showed up here"));
            if (tourFailure != null) yield break;
            float t1 = LastSpawnAnnounce;
            Check("spawns.client.copied", ComicsNear(SpawnA) == 1, "copy of the host's item, " + ComicsNear(SpawnA) + " near its spot");
            // 2. Both at the agreed moment: this copy's own is adopted.
            float t2 = Slot(t1 + 6);
            yield return StartCoroutine(UntilHostTime(t2));
            MakeLikeComic(SpawnB + new Vector3(0.05f, 0, 0));
            yield return StartCoroutine(Pause(6));
            Check("spawns.client.adopted", SpawnsAdopted >= 1 && ComicsNear(SpawnB) == 1, "adopted " + SpawnsAdopted + ", " + ComicsNear(SpawnB) + " at the shared spot");
            // 3. This copy alone: removed.
            yield return StartCoroutine(UntilHostTime(t2 + 10));
            MakeLikeComic(SpawnC);
            yield return StartCoroutine(Pause(AdoptWindow + 2));
            Check("spawns.client.removed", SpawnsRemoved >= 1 && ComicsNear(SpawnC) == 0, "removed " + SpawnsRemoved + ", " + ComicsNear(SpawnC) + " left at its spot");
            // 4. The host destroys the first one.
            yield return StartCoroutine(Wait(() => ComicsNear(SpawnA) == 0, 30, "spawns.client.despawned", () => ComicsNear(SpawnA) + " still at the first spot"));
            if (tourFailure != null) yield break;
            Check("spawns.client.despawned", Despawns >= 1, "removed after the host destroyed it");
            yield return StartCoroutine(Pause(4));
            Check("tour.done", tourFailure == null, "spawns");
        }
    }
}
