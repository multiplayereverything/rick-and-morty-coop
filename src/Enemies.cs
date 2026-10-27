using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Enemies (the egg shoot-out's gunmen): a fixed pool under EnemyManager, so an enemy's index in that pool is the
    // same on every copy. The host's game runs them (spawning, moving, shooting); it streams every enemy's state at
    // 15 Hz (Op.Enemies). A client spawns and moves none itself: it switches its pool copies on and off and places them
    // where the host's are. A client's hit on an enemy (its gun, a thrown item) goes to the host (Op.EnemyHit), which
    // applies it; the death then plays here when the host's state says that enemy is dead, which also counts the kill
    // for the client's own story (the Brain waits for a kill count).
    public sealed partial class TogetherRuntime
    {
        static readonly FieldInfo enemyListField = typeof(EnemyManager).GetField("enemyControllers", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        public static readonly FieldInfo enemyTargetField = typeof(EnemyController).GetField("playerHead", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        static readonly FieldInfo headTargetField = typeof(PlayerHeadDeathController).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)

        // What enemy bullets hit on this player's head (its HurtTarget, which kills the player).
        public static HurtTarget OwnHurtTarget()
        {
            PlayerHeadDeathController head = Master != null ? Master.GetComponentInChildren<PlayerHeadDeathController>(true) : null;
            return head != null ? headTargetField.GetValue(head) as HurtTarget : null;
        }
        float nextRetarget;
        public int PlayerHitsSent, PlayerHitsTaken;   // lab checks read these
        public static bool ReplayingEnemy;   // call-scoped
        EnemyManager enemies;
        float nextEnemySend, nextEnemyFind;
        public int EnemyHitsSent, EnemyHitsApplied, EnemyDeathsShown;   // lab checks read these
        // The host's last word on each enemy (client side): out, alive, where.
        public bool[] HostEnemyOn = new bool[0], HostEnemyAlive = new bool[0];
        public Vector3[] HostEnemyAt = new Vector3[0];

        public static EnemyController[] EnemyList(EnemyManager m) { return m != null ? enemyListField.GetValue(m) as EnemyController[] : null; }

        public EnemyManager FindEnemies()
        {
            if (enemies == null || !enemies.isActiveAndEnabled)
            {
                float now = Time.realtimeSinceStartup;
                if (now < nextEnemyFind) return null;
                nextEnemyFind = now + 3f;
                enemies = Object.FindObjectOfType<EnemyManager>();
            }
            return enemies;
        }

        public bool ClientRunsEnemies { get { return Active && !Hosting && !Mutant("noenemysync"); } }

        void EnemiesUpdate(float now)
        {
            if (!Active || !Hosting || now < nextEnemySend) return;
            EnemyManager m = FindEnemies();
            EnemyController[] list = EnemyList(m);
            if (list == null) return;
            bool any = false;
            foreach (EnemyController e in list) if (e != null && e.gameObject.activeInHierarchy) any = true;
            if (any && now >= nextRetarget) { nextRetarget = now + 1f; Retarget(list); }
            // Nothing out: a slow heartbeat only.
            nextEnemySend = now + (any ? 1f / 15f : 2f);
            Broadcast(Wire.Packet(Op.Enemies, w =>
            {
                w.Write((byte)Mathf.Min(list.Length, 255));
                for (int i = 0; i < list.Length && i < 255; i++)
                {
                    EnemyController e = list[i];
                    bool on = e != null && e.gameObject.activeInHierarchy;
                    w.Write((byte)((on ? 1 : 0) | (on && e.IsAlive ? 2 : 0)));
                    if (on) { Wire.Vec(w, e.transform.position); Wire.Rot(w, e.transform.rotation); }
                }
            }), -1);
        }

        void ClientEnemies(BinaryReader r)
        {
            int count = r.ReadByte();
            EnemyManager m = FindEnemies();
            EnemyController[] list = EnemyList(m);
            if (HostEnemyOn.Length != count) { HostEnemyOn = new bool[count]; HostEnemyAlive = new bool[count]; HostEnemyAt = new Vector3[count]; }
            for (int i = 0; i < count; i++)
            {
                byte flags = r.ReadByte();
                bool on = (flags & 1) != 0, alive = (flags & 2) != 0;
                Vector3 position = Vector3.zero; Quaternion rotation = Quaternion.identity;
                if (on) { position = Wire.Vec(r); rotation = Wire.Rot(r); }
                HostEnemyOn[i] = on; HostEnemyAlive[i] = alive; HostEnemyAt[i] = position;
                if (list == null || i >= list.Length || list[i] == null || !ClientRunsEnemies) continue;
                EnemyController e = list[i];
                bool here = e.gameObject.activeInHierarchy;
                if (on && !here) { e.gameObject.SetActive(true); here = true; }
                if (!on && here) { m.ReleaseEnemy(e.gameObject); continue; }
                if (!on) continue;
                if (alive && e.IsAlive) { e.transform.position = position; e.transform.rotation = rotation; }
                if (!alive && e.IsAlive)
                {
                    // Dead in the host's game: the same death here (effect, ragdoll, kill count).
                    ReplayingEnemy = true;
                    try { e.Hit(float.PositiveInfinity, position); } finally { ReplayingEnemy = false; }
                    EnemyDeathsShown++;
                }
            }
        }

        // Each enemy aims at the nearest player: this one's head, or another player's drawn head (which bullets can hit,
        // see RemoteHead).
        void Retarget(EnemyController[] list)
        {
            if (Mutant("noenemytarget") || Master == null || Master.HMDCamera == null) return;
            foreach (EnemyController e in list)
            {
                if (e == null || !e.gameObject.activeInHierarchy || !e.IsAlive) continue;
                Transform best = Master.HMDCamera.transform;
                float bestDistance = (best.position - e.transform.position).sqrMagnitude;
                foreach (Peer p in peers.Values)
                {
                    if (p.Avatar == null || !p.Avatar.Root.activeSelf) continue;
                    float d = (p.Avatar.Head.position - e.transform.position).sqrMagnitude;
                    if (d < bestDistance) { best = p.Avatar.Head; bestDistance = d; }
                }
                enemyTargetField.SetValue(e, best);
            }
        }

        // A bullet in the host's game hit another player's drawn head: that player's own head takes it.
        public void RemoteHeadHit(int peer, float damage)
        {
            if (!Active || !Hosting) return;
            PlayerHitsSent++;
            Send(peer, Wire.Packet(Op.PlayerHit, w => w.Write(Mathf.Clamp(damage, 0, 100000))));
            Log("Player " + (peer + 1) + " hit by an enemy in the host's game");
        }

        void ClientPlayerHit(BinaryReader r)
        {
            float damage = Wire.Number(r);
            HurtTarget head = OwnHurtTarget();
            if (head == null) return;
            PlayerHitsTaken++;
            head.Hit(damage, head.transform.position);
        }

        // Harmony prefix on EnemyController.Hit. False: a client sends the hit to the host instead of applying it.
        public bool OnEnemyHit(EnemyController e, float damage, Vector3 point)
        {
            if (!ClientRunsEnemies || ReplayingEnemy || damage == 0f || !e.IsAlive) return true;
            EnemyController[] list = EnemyList(FindEnemies());
            int index = list != null ? System.Array.IndexOf(list, e) : -1;
            if (index < 0) return true;
            EnemyHitsSent++;
            float d = float.IsInfinity(damage) ? 100000f : Mathf.Min(damage, 100000f);
            Send(0, Wire.Packet(Op.EnemyHit, w => { w.Write((byte)index); w.Write(d); Wire.Vec(w, point); }));
            return false;
        }

        void HostEnemyHit(int from, BinaryReader r)
        {
            int index = r.ReadByte(); float damage = Mathf.Clamp(Wire.Number(r), 0, 100000); Vector3 point = Wire.Vec(r);
            EnemyController[] list = EnemyList(FindEnemies());
            if (list == null || index >= list.Length || list[index] == null || !list[index].gameObject.activeInHierarchy) return;
            list[index].Hit(damage, point);
            EnemyHitsApplied++;
        }
    }

    // A client's own enemy waves would put enemies only it sees: the host's waves are the ones shown.
    [HarmonyPatch(typeof(EnemyManager), "RunPhase1")]
    static class EnemyPhase1Patch { static bool Prefix() { TogetherRuntime rt = TogetherRuntime.Instance; return rt == null || !rt.ClientRunsEnemies; } }

    [HarmonyPatch(typeof(EnemyManager), "RunPhase2")]
    static class EnemyPhase2Patch { static bool Prefix() { TogetherRuntime rt = TogetherRuntime.Instance; return rt == null || !rt.ClientRunsEnemies; } }

    // A mirrored enemy switched on here must not walk or shoot on its own.
    [HarmonyPatch(typeof(EnemyController), "OnEnable")]
    static class EnemyEnablePatch
    {
        static void Postfix(EnemyController __instance) { TogetherRuntime rt = TogetherRuntime.Instance; if (rt != null && rt.ClientRunsEnemies) __instance.StopAllCoroutines(); }
    }

    [HarmonyPatch(typeof(EnemyController), "Hit")]
    static class EnemyHitPatch
    {
        static bool Prefix(EnemyController __instance, float damage, Vector3 hitPoint) { TogetherRuntime rt = TogetherRuntime.Instance; return rt == null || rt.OnEnemyHit(__instance, damage, hitPoint); }
    }

    // On the host, another player's drawn head can be shot: the same layer and trigger setting as this player's own
    // head collider (what enemy bullets look for), and a hit goes to that player (TogetherRuntime.RemoteHeadHit).
    public sealed class RemoteHead : MonoBehaviour, IDamageable
    {
        public int Peer;

        public static void Attach(Avatar avatar, int peer)
        {
            if (avatar.Head.GetComponent<RemoteHead>() != null) return;
            HurtTarget own = TogetherRuntime.OwnHurtTarget();
            Collider like = own != null ? own.GetComponent<Collider>() : null;
            SphereCollider c = avatar.Head.gameObject.AddComponent<SphereCollider>();
            c.radius = 0.13f;
            c.isTrigger = like == null || like.isTrigger;
            if (own != null) avatar.Head.gameObject.layer = own.gameObject.layer;
            avatar.Head.gameObject.AddComponent<RemoteHead>().Peer = peer;
            TogetherRuntime.Log("Shootable head for player " + (peer + 1) + ": layer " + LayerMask.LayerToName(avatar.Head.gameObject.layer) + ", trigger " + c.isTrigger);
        }

        public void Hit(float damage, Vector3 hitPoint, DamageInstigator instigator = null)
        {
            TogetherRuntime rt = TogetherRuntime.Instance;
            if (rt != null && damage > 0) rt.RemoteHeadHit(Peer, damage);
        }
    }
}
