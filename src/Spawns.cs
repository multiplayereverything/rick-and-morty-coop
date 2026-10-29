using System.Collections.Generic;
using System.IO;
using OwlchemyVR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // Things the game makes while playing (machine results, dispenser drops, laundry, Meeseeks...) come from some forty
    // places in its code. Most of them run on every copy anyway, because the story, buttons and items that trigger
    // them are shared, so each copy usually makes the same thing at about the same spot. What they lack is one shared
    // identity and one copy in charge:
    // - Host: every new grabbable it makes (a "(Clone)" in its path) gets an id and is announced (Op.Spawn).
    // - Client: its own new grabbables wait. An announcement adopts the nearest one with the same name made in the last
    //   few seconds (now a follower of the host's). With none to adopt, the client makes a copy of an object with that
    //   name. One of its own that no announcement claims within 3 s is removed: the host's game did not make it.
    // - An item destroyed in the host's game (eaten, recycled, smashed) is removed everywhere (Op.Despawn); one
    //   destroyed in a client's hand is a request to the host.
    public sealed partial class TogetherRuntime
    {
        sealed class LocalSpawn { public GrabbableItem Grab; public string Name; public float Seen; }
        sealed class HostSpawn { public uint Id; public string Name; public Vector3 Position; public Quaternion Rotation; public float At; }

        const float AdoptRange = 1.5f, AdoptWindow = 3f, CopyAfter = 2f, WaitForOwn = 30f;
        readonly List<LocalSpawn> localSpawns = new List<LocalSpawn>();
        readonly List<HostSpawn> hostSpawns = new List<HostSpawn>();
        int spawnCounter;
        float nextSpawnMatch;
        public int SpawnsAnnounced, SpawnsAdopted, SpawnsCopied, SpawnsRemoved, Despawns;   // lab checks read these
        public float LastSpawnAnnounce = -1;   // host time of the last announcement sent or received (lab tours)

        void ResetSpawns() { localSpawns.Clear(); hostSpawns.Clear(); spawnCounter = 0; }

        static bool IsSpawned(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name.Contains("(Clone)")) return true;
            return false;
        }

        static string BaseName(string name) { return name.Replace("(Clone)", "").Trim(); }

        // ScanItems found a grabbable the game made while playing.
        void FoundSpawned(GrabbableItem grab, Rigidbody body, LimitedIndirectGrabbableController control)
        {
            if (Hosting || !Active)
            {
                string scene = SceneManager.GetActiveScene().name;
                uint id = Hash("spawn:" + scene + ":" + (++spawnCounter));
                NetItem item = new NetItem { Id = id, Key = "spawn:" + BaseName(grab.name), Grab = grab, Body = body, Control = control, Owner = 0, OriginalKinematic = body != null && body.isKinematic, Spawned = true };
                items[id] = item; itemOf[grab] = item;
                if (Active) { SpawnsAnnounced++; LastSpawnAnnounce = HostTime; Broadcast(SpawnPacket(item), -1); Log("Spawn " + grab.name + " -> " + id.ToString("x8")); }
                return;
            }
            foreach (LocalSpawn s in localSpawns) if (s.Grab == grab) return;
            localSpawns.Add(new LocalSpawn { Grab = grab, Name = BaseName(grab.name), Seen = Time.realtimeSinceStartup });
        }

        byte[] SpawnPacket(NetItem item)
        {
            Transform t = item.Grab.transform;
            string scene = SceneManager.GetActiveScene().name, name = BaseName(item.Grab.name);
            return Wire.Packet(Op.Spawn, w => { w.Write(item.Id); Wire.Text(w, scene); Wire.Text(w, name); Wire.Vec(w, t.position); Wire.Rot(w, t.rotation); });
        }

        void ClientSpawn(BinaryReader r)
        {
            uint id = r.ReadUInt32(); string scene = Wire.Text(r, 64); string name = Wire.Text(r, 128);
            Vector3 position = Wire.Vec(r); Quaternion rotation = Wire.Rot(r);
            LastSpawnAnnounce = HostTime;
            if (scene != SceneManager.GetActiveScene().name || items.ContainsKey(id)) return;
            foreach (HostSpawn h in hostSpawns) if (h.Id == id) return;
            hostSpawns.Add(new HostSpawn { Id = id, Name = name, Position = position, Rotation = rotation, At = Time.realtimeSinceStartup });
            MatchSpawns();
        }

        void SpawnsUpdate(float now)
        {
            if (now < nextSpawnMatch) return;
            nextSpawnMatch = now + 0.25f;
            DespawnDestroyed();
            if (!Hosting && Active) MatchSpawns();
        }

        void MatchSpawns()
        {
            float now = Time.realtimeSinceStartup;
            localSpawns.RemoveAll(s => s.Grab == null);
            for (int i = hostSpawns.Count - 1; i >= 0; i--)
            {
                HostSpawn h = hostSpawns[i];
                LocalSpawn best = null; float bestDistance = AdoptRange;
                if (!Mutant("nospawnmatch"))
                    foreach (LocalSpawn s in localSpawns)
                    {
                        float d = (s.Grab.transform.position - h.Position).magnitude;
                        if (s.Name == h.Name && d < bestDistance && s.Seen - h.At > -(AdoptWindow + CopyAfter)) { best = s; bestDistance = d; }
                    }
                if (best != null)
                {
                    localSpawns.Remove(best);
                    Adopt(h, best.Grab);
                    SpawnsAdopted++;
                    Log("Spawn " + h.Name + " matched here (" + bestDistance.ToString("0.00") + " m apart)");
                    hostSpawns.RemoveAt(i);
                    continue;
                }
                if (now - h.At < CopyAfter) continue;
                // This copy did not make it: make one like it.
                GrabbableItem template = null;
                foreach (GrabbableItem g in Object.FindObjectsOfType<GrabbableItem>()) if (BaseName(g.name) == h.Name) { template = g; break; }
                // Nothing like it here yet: a slow copy may still be making its own. Wait for it (up to 30 s) rather than
                // drop the announcement and then remove this copy's own as one only it made.
                if (template == null && now - h.At < WaitForOwn && !Mutant("nospawnwait")) continue;
                hostSpawns.RemoveAt(i);
                if (template == null) { Log("Spawn " + h.Name + ": nothing like it here to copy"); continue; }
                GameObject copy = (GameObject)Object.Instantiate(template.gameObject, h.Position, h.Rotation);
                copy.name = h.Name + "(Clone)";
                NetItem made = Adopt(h, copy.GetComponent<GrabbableItem>());
                // The template may be a follower here (kinematic for that reason only): take its real setting.
                NetItem like;
                if (made != null && itemOf.TryGetValue(template, out like)) made.OriginalKinematic = like.OriginalKinematic;
                SpawnsCopied++;
                Log("Spawn " + h.Name + " copied here");
            }
            // Made here only: the host's game did not make it.
            for (int i = localSpawns.Count - 1; i >= 0; i--)
            {
                LocalSpawn s = localSpawns[i];
                if (now - s.Seen < AdoptWindow || Mutant("nospawnremove")) continue;
                localSpawns.RemoveAt(i);
                if (s.Grab.IsCurrInHand) s.Grab.CurrInteractableHand.TryRelease(false);
                Object.Destroy(s.Grab.gameObject);
                SpawnsRemoved++;
                Log("Removed " + s.Name + ": only this copy made it");
            }
        }

        NetItem Adopt(HostSpawn h, GrabbableItem grab)
        {
            if (grab == null) return null;
            Rigidbody body = grab.GetComponent<Rigidbody>();
            NetItem item = new NetItem { Id = h.Id, Key = "spawn:" + h.Name, Grab = grab, Body = body, Control = grab.GetComponent<LimitedIndirectGrabbableController>(), Owner = 0, OriginalKinematic = body != null && body.isKinematic, Spawned = true };
            items[h.Id] = item; itemOf[grab] = item;
            StartFollowing(item);
            item.TargetPosition = h.Position; item.TargetRotation = h.Rotation; item.HasTarget = true;
            return item;
        }

        // Items whose object the game destroyed.
        void DespawnDestroyed()
        {
            List<NetItem> gone = null;
            foreach (NetItem item in items.Values) if (item.Grab == null) { if (gone == null) gone = new List<NetItem>(); gone.Add(item); }
            if (gone == null) return;
            foreach (NetItem item in gone)
            {
                items.Remove(item.Id);
                if (item.Control != null) FollowingControls.Remove(item.Control);
                if (!Active || Mutant("nodespawn")) continue;
                uint id = item.Id;
                byte[] packet = Wire.Packet(Op.Despawn, w => w.Write(id));
                if (Hosting) { Broadcast(packet, -1); Despawns++; Log("Despawn " + item.Key); }
                else if (item.Owner == LocalId) Send(0, packet);   // gone from this player's hand: the host decides
            }
            List<GrabbableItem> stale = new List<GrabbableItem>();
            foreach (GrabbableItem g in itemOf.Keys) if (g == null) stale.Add(g);
            foreach (GrabbableItem g in stale) itemOf.Remove(g);
        }

        void HostDespawn(int from, BinaryReader r)
        {
            uint id = r.ReadUInt32();
            NetItem item;
            if (!items.TryGetValue(id, out item) || item.Owner != from) return;
            RemoveEverywhere(item);
            Broadcast(Wire.Packet(Op.Despawn, w => w.Write(id)), from);
        }

        void ClientDespawn(BinaryReader r)
        {
            uint id = r.ReadUInt32();
            NetItem item;
            if (items.TryGetValue(id, out item)) RemoveEverywhere(item);
        }

        void RemoveEverywhere(NetItem item)
        {
            items.Remove(item.Id);
            if (item.Control != null) FollowingControls.Remove(item.Control);
            if (item.Grab == null) return;
            itemOf.Remove(item.Grab);
            if (item.Grab.IsCurrInHand) item.Grab.CurrInteractableHand.TryRelease(false);
            Object.Destroy(item.Grab.gameObject);
            Despawns++;
            Log("Despawned " + item.Key);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(GrabbableItem), "Start")]
    static class GrabbableStartPatch
    {
        static void Postfix(GrabbableItem __instance) { if (TogetherRuntime.Instance != null && TogetherRuntime.Instance.Active) TogetherRuntime.Started.Add(__instance); }
    }
}
