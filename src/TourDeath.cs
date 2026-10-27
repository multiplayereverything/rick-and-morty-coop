using System.Collections;
using OwlchemyVR.Teleportation;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "death": in the garage the client dies (DeathController.PlayerDeath, what a bullet or poison does). It must
    // go to its own death area and stay there (not pulled back to the host), while the host stays alive with no death
    // area on; then the client respawns (PlayerRespawn, what the death scene's revive does) and is back in the garage.
    public sealed partial class TogetherRuntime
    {
        static string ZoneScene()
        {
            TeleportationController tc = GlobalStorage.no_instantiate_instance != null ? GlobalStorage.no_instantiate_instance.TeleportationController : null;
            return tc != null && tc.CurrentZone != null ? tc.CurrentZone.gameObject.scene.name : "";
        }

        IEnumerator DeathHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(4));
            yield return StartCoroutine(IntoGarage("death.host.garage"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => { Peer p = FirstPeer(); return p != null && p.Pose.Scene == "RicksGarage"; }, 120, "death.host.alive", "the client never reached the garage"));
            if (tourFailure != null) yield break;
            // Watch for 50 s while the client dies and respawns: nothing of that may happen here.
            bool died = false, deathArea = false;
            float until = Time.realtimeSinceStartup + 50;
            while (Time.realtimeSinceStartup < until)
            {
                died |= DeathController.Instance != null && DeathController.Instance.PlayerIsDead;
                deathArea |= ActiveAreas().Contains("Death");
                yield return null;
            }
            Check("death.host.alive", !died && !deathArea, "host died " + died + ", Death area on here " + deathArea + ", zone " + ZoneScene());
            Check("tour.done", tourFailure == null, "death");
        }

        IEnumerator DeathClient()
        {
            RigHead = new Vector3(1.0f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(IntoGarage("death.client.dead"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => DeathController.Instance != null, 30, "death.client.dead", "no DeathController"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(5));
            string before = ZoneScene();
            DeathController.Instance.PlayerInvincible = false;
            DeathController.Instance.PlayerDeath();
            yield return StartCoroutine(Wait(() => DeathController.Instance.PlayerIsDead && ZoneScene() == "Death", 30, "death.client.dead", () => "dead " + DeathController.Instance.PlayerIsDead + ", zone in " + ZoneScene()));
            if (tourFailure != null) yield break;
            Check("death.client.dead", true, "died: from " + before + " to the Death area");
            // Several area snapshots from the host go by: this player must stay dead where it is.
            yield return StartCoroutine(Pause(10));
            Check("death.client.stays", DeathController.Instance.PlayerIsDead && ZoneScene() == "Death" && ActiveAreas().Contains("Death"), "after 10 s: dead " + DeathController.Instance.PlayerIsDead + ", zone in " + ZoneScene());
            DeathController.Instance.PlayerRespawn();
            yield return StartCoroutine(Wait(() => !DeathController.Instance.PlayerIsDead && ZoneScene() != "Death" && ZoneScene().Length > 0, 30, "death.client.back", () => "zone in " + ZoneScene()));
            if (tourFailure != null) yield break;
            Check("death.client.back", true, "respawned in " + ZoneScene());
            yield return StartCoroutine(Pause(4));
            Check("tour.done", tourFailure == null, "death");
        }
    }
}
