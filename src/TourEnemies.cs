using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OwlchemyVR;
using OwlchemyVR.SceneManagement;
using OwlchemyVR.Teleportation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // Tour "enemies": into the garage, the egg shoot-out area on, its first enemy wave started with the game's own
    // EnemyManager.RunPhase1 (what the Brain does when the mission starts). The client must show the same enemies at
    // the same places; then it shoots one (EnemyController.Hit, what its gun does) and the host's game kills it.
    public sealed partial class TogetherRuntime
    {
        IEnumerator IntoGarage(string check)
        {
            if (SceneManager.GetActiveScene().name != "RicksGarage" && Hosting) GlobalStorage.Instance.FadeAndLoadScene("RicksGarage", 1f);
            yield return StartCoroutine(Wait(() => SceneManager.GetActiveScene().name == "RicksGarage" && ActiveAreas().Count > 0, 300, check, () => "in " + SceneManager.GetActiveScene().name));
        }

        // Every garage area and the teleport pads in it.
        void LogGarage()
        {
            MultiSceneController m = Areas();
            if (m == null) return;
            StringBuilder s = new StringBuilder("RT-SURVEY areas:");
            foreach (string n in m.SceneNames) s.Append(" " + n + (m.GetSceneController(n) != null ? "" : "(none)"));
            Log(s.ToString());
            foreach (TeleportZone z in Resources.FindObjectsOfTypeAll<TeleportZone>())
                if (z.gameObject.scene.isLoaded) Log("RT-SURVEY pad " + z.gameObject.scene.name + " " + z.name + " " + (z.UniqueObjectData != null ? z.UniqueObjectData.name : "-") + " at " + V(z.transform.position));
            // The pads the game starts a player on (the first of these to register after a scene load) and where it put us.
            TeleportationController tc = GlobalStorage.Instance.TeleportationController;
            UniqueObjectData[] initial = (UniqueObjectData[])typeof(TeleportationController).GetField("initialZoneDatas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tc);
            List<string> names = new List<string>();
            foreach (UniqueObjectData d in initial) names.Add(d != null ? d.name : "-");
            Log("RT-SURVEY start pads: " + string.Join(", ", names.ToArray()) + "; on " + (tc.CurrentZone != null ? tc.CurrentZone.name : "none") + ", stage " + CurrentStageName());
        }

        int EnemiesOutHere(bool aliveOnly)
        {
            int n = 0;
            EnemyController[] list = EnemyList(FindEnemies());
            if (list != null) foreach (EnemyController e in list) if (e != null && e.gameObject.activeInHierarchy && (!aliveOnly || e.IsAlive)) n++;
            return n;
        }

        IEnumerator EnemiesHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(4));
            yield return StartCoroutine(IntoGarage("enemies.host.garage"));
            if (tourFailure != null) yield break;
            LogGarage();
            SceneController shootout = Areas().GetSceneController("EggShootOut");
            if (shootout == null) { tourFailure = "enemies.host.area"; Check("enemies.host.area", false, "no EggShootOut area"); yield break; }
            shootout.BeginDelayedActivation();
            yield return StartCoroutine(Wait(() => shootout.IsActivated && FindEnemies() != null, 60, "enemies.host.area", "the shoot-out area or its EnemyManager never came up"));
            if (tourFailure != null) yield break;
            Check("enemies.host.area", true, "shoot-out on, " + EnemyList(enemies).Length + " enemies in the pool");
            yield return StartCoroutine(Pause(3));
            enemies.RunPhase1();
            yield return StartCoroutine(Wait(() => EnemiesOutHere(true) >= 2, 60, "enemies.host.wave", () => EnemiesOutHere(true) + " enemies out"));
            if (tourFailure != null) yield break;
            Check("enemies.host.wave", true, EnemiesOutHere(true) + " enemies out");
            yield return StartCoroutine(Wait(() => EnemyHitsApplied >= 1, 90, "enemies.host.hit", "no hit from the client"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(1));
            Check("enemies.host.hit", true, "client hits applied " + EnemyHitsApplied + ", alive now " + EnemiesOutHere(true));
            // Phase 2 shoots at players: the client, standing in the arena, is the nearest target.
            yield return StartCoroutine(Pause(8));
            enemies.RunPhase2();
            // Every enemy out aims at the client, the nearest player.
            Peer client = FirstPeer();
            yield return StartCoroutine(Wait(() =>
            {
                int aiming = 0, alive = 0;
                foreach (EnemyController e in EnemyList(enemies)) if (e.gameObject.activeInHierarchy && e.IsAlive) { alive++; if ((Transform)enemyTargetField.GetValue(e) == client.Avatar.Head) aiming++; }
                return alive > 0 && aiming == alive;
            }, 30, "enemies.host.aims", "the enemies do not all aim at the client"));
            if (tourFailure != null) yield break;
            Check("enemies.host.aims", true, "every enemy out aims at the client's head");
            // A shot along the enemy bullets' own ray test hits the client's head, and goes the bullet's way.
            HitScanBullet bullet = null;
            foreach (HitScanBullet h in Resources.FindObjectsOfTypeAll<HitScanBullet>()) if (h.gameObject.scene.isLoaded) bullet = h;
            LayerMask mask = (LayerMask)typeof(HitScanBullet).GetField("layerMask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(bullet);
            EnemyController shooter = null;
            foreach (EnemyController e in EnemyList(enemies)) if (e.gameObject.activeInHierarchy && e.IsAlive) shooter = e;
            // The last half metre of the shot (scenery further out is the level's business, not the mod's).
            Vector3 from = client.Avatar.Head.position + (shooter.transform.position - client.Avatar.Head.position).normalized * 0.5f;
            RaycastHit hit;
            bool reached = Physics.Raycast(from, client.Avatar.Head.position - from, out hit, 1f, mask, QueryTriggerInteraction.Collide) && hit.collider.GetComponent<RemoteHead>() != null;
            Check("enemies.host.raycast", reached, reached ? "the bullet ray from the enemy's side hits the client's head" : "the bullet ray hit " + (hit.collider != null ? hit.collider.name : "nothing"));
            if (reached) ((IDamageable)hit.collider.GetComponent<RemoteHead>()).Hit(1f, hit.point, bullet);
            yield return StartCoroutine(Wait(() => PlayerHitsSent >= 1, 120, "enemies.host.shotclient", "no enemy bullet hit the client's head here"));
            if (tourFailure != null) yield break;
            Check("enemies.host.shotclient", !DeathController.Instance.PlayerIsDead, "client hit " + PlayerHitsSent + "x in this game, host alive " + !DeathController.Instance.PlayerIsDead);
            yield return StartCoroutine(Pause(4));
            enemies.StopSpawningEnemies(); enemies.ReleaseAllEnemies();
            yield return StartCoroutine(Pause(4));
            Check("tour.done", tourFailure == null, "enemies");
        }

        IEnumerator EnemiesClient()
        {
            RigHead = new Vector3(1.0f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(IntoGarage("enemies.client.garage"));
            if (tourFailure != null) yield break;
            // The same enemies out here as in the host's game, at the same places.
            float worst = 0;
            yield return StartCoroutine(Wait(() =>
            {
                EnemyController[] list = EnemyList(FindEnemies());
                if (list == null || HostEnemyOn.Length != list.Length) return false;
                int hostOut = 0, mismatched = 0; worst = 0;
                for (int i = 0; i < list.Length; i++)
                {
                    bool here = list[i].gameObject.activeInHierarchy;
                    if (HostEnemyOn[i]) hostOut++;
                    if (here != HostEnemyOn[i]) mismatched++;
                    else if (here && HostEnemyAlive[i]) worst = Mathf.Max(worst, (list[i].transform.position - HostEnemyAt[i]).magnitude);
                }
                return hostOut >= 2 && mismatched == 0 && worst < 0.3f;
            }, 120, "enemies.client.same", () => EnemiesOutHere(false) + " out here, worst " + worst.ToString("0.00") + " m off"));
            if (tourFailure != null) yield break;
            Check("enemies.client.same", true, EnemiesOutHere(true) + " enemies here, worst " + worst.ToString("0.00") + " m from the host's");
            // Shoot one.
            EnemyController target = null;
            foreach (EnemyController e in EnemyList(enemies)) if (e.gameObject.activeInHierarchy && e.IsAlive) { target = e; break; }
            int shown = EnemyDeathsShown;
            target.Hit(1000f, target.transform.position);
            Check("enemies.client.held", target.IsAlive && EnemyHitsSent >= 1, "the hit went to the host, not applied here (alive " + target.IsAlive + ")");
            yield return StartCoroutine(Wait(() => !target.IsAlive && EnemyDeathsShown > shown, 20, "enemies.client.killed", "the shot enemy never died here"));
            if (tourFailure != null) yield break;
            Check("enemies.client.killed", true, target.name + " died here after the host's game killed it");
            // Into the arena: phase 2 will shoot at this player from the host's game.
            TeleportZone arena = PadIn("EggShootOut");
            if (arena == null) { tourFailure = "enemies.client.shot"; Check("enemies.client.shot", false, "no pad in the shoot-out area"); yield break; }
            GlobalStorage.Instance.TeleportationController.Teleport(arena, false, true, false, true);
            DeathController.Instance.PlayerInvincible = false;
            yield return StartCoroutine(Wait(() => PlayerHitsTaken >= 1 && DeathController.Instance.PlayerIsDead, 150, "enemies.client.shot", () => "hits taken " + PlayerHitsTaken + ", dead " + DeathController.Instance.PlayerIsDead + ", zone in " + ZoneScene()));
            if (tourFailure != null) yield break;
            Check("enemies.client.shot", true, "shot by the host's enemies, died here (zone in " + ZoneScene() + ")");
            DeathController.Instance.PlayerRespawn();
            yield return StartCoroutine(Pause(8));
            Check("tour.done", tourFailure == null, "enemies");
        }
    }
}
