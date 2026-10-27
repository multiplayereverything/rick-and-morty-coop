using System.Collections;
using OwlchemyVR.SceneManagement;
using OwlchemyVR.Teleportation;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "playthrough": every mission area of the garage, entered the way the game's blast-shield portal does it: the
    // garage stays on, the next mission area is switched on and the previous mission switched off, and the player goes
    // through to the mission's pad. The client walks into the first mission itself; after that it does nothing: when
    // the area it stands in goes off it must be brought to the host. Last, the host goes back to the garage and switches
    // the last mission off. Both lab rigs stand in their play area (they move with teleports), so "in sight" is real.
    public sealed partial class TogetherRuntime
    {
        static readonly string[] Missions = { "EggShootOut", "Bathroom", "PetriDish", "Troy", "Satellite" };   // process-scoped (a constant)

        static TeleportZone PadIn(string area)
        {
            foreach (TeleportZone z in Object.FindObjectsOfType<TeleportZone>()) if (z.gameObject.scene.name == area && z.isActiveAndEnabled) return z;
            return null;
        }

        float SeesOther()
        {
            Peer p = FirstPeer();
            return p == null || p.Avatar == null || !p.Avatar.Root.activeSelf ? float.MaxValue : (p.Pose.HeadPosition - Master.HMDCamera.transform.position).magnitude;
        }

        IEnumerator GoTo(TeleportZone pad, string check)
        {
            TeleportationController tc = GlobalStorage.Instance.TeleportationController;
            tc.Teleport(pad, false, true, false, true);
            yield return StartCoroutine(Wait(() => tc.CurrentZone == pad && !tc.IsTeleporting, 20, check, "never reached the pad " + pad.name));
        }

        IEnumerator PlayHost()
        {
            RigRelative = true; RigHead = new Vector3(-0.3f, 1.35f, 0); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(4));
            yield return StartCoroutine(IntoGarage("play.host.garage"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => { Peer p = FirstPeer(); return p != null && p.Pose.Scene == "RicksGarage"; }, 120, "play.host.garage", "the client never reached the garage"));
            if (tourFailure != null) yield break;
            TeleportZone garagePad = GlobalStorage.Instance.TeleportationController.CurrentZone;
            yield return StartCoroutine(Pause(6));
            string previous = null;
            foreach (string area in Missions)
            {
                SceneController next = Areas().GetSceneController(area);
                if (next == null) { tourFailure = "play.host." + area; Check(tourFailure, false, "no area " + area); yield break; }
                // The portal: next mission on, previous mission off, the garage stays.
                next.BeginDelayedActivation();
                if (previous != null) Areas().GetSceneController(previous).BeginDelayedDeactivation();
                TeleportZone pad = null;
                yield return StartCoroutine(Wait(() => next.IsActivated && (pad = PadIn(area)) != null, 60, "play.host." + area, "area " + area + " or its pad never came up"));
                if (tourFailure != null) yield break;
                yield return StartCoroutine(GoTo(pad, "play.host." + area));
                if (tourFailure != null) yield break;
                previous = area;
                Check("play.host." + area, ActiveAreas().Contains("RicksGarage"), "in " + area + " at " + pad.name + ", areas on: " + string.Join(",", ActiveAreas().ToArray()));
                yield return StartCoroutine(Wait(() => SeesOther() < 4f, 90, "play.host.sees." + area, () => "the client is " + SeesOther().ToString("0.0") + " m away"));
                if (tourFailure != null) yield break;
                Check("play.host.sees." + area, true, "client " + SeesOther().ToString("0.0") + " m away");
                yield return StartCoroutine(Pause(12));
            }
            // Home: back through the portal to the garage, the last mission off.
            yield return StartCoroutine(GoTo(garagePad, "play.host.home"));
            if (tourFailure != null) yield break;
            Areas().GetSceneController(previous).BeginDelayedDeactivation();
            yield return StartCoroutine(Wait(() => SeesOther() < 4f, 90, "play.host.home", () => "the client is " + SeesOther().ToString("0.0") + " m away"));
            if (tourFailure != null) yield break;
            Check("play.host.home", true, "back in the garage with the client " + SeesOther().ToString("0.0") + " m away");
            yield return StartCoroutine(Pause(6));
            Check("tour.done", tourFailure == null, "playthrough");
        }

        IEnumerator PlayClient()
        {
            RigRelative = true; RigHead = new Vector3(0.6f, 1.35f, 0); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(IntoGarage("play.client.garage"));
            if (tourFailure != null) yield break;
            bool walkedIn = false;
            foreach (string area in Missions)
            {
                if (!walkedIn)
                {
                    // The first mission: through the portal after the host, like a player.
                    TeleportZone pad = null;
                    yield return StartCoroutine(Wait(() => ActiveAreas().Contains(area) && (pad = PadIn(area)) != null, 120, "play.client." + area, "area " + area + " never came on here"));
                    if (tourFailure != null) yield break;
                    yield return StartCoroutine(Pause(2));
                    yield return StartCoroutine(GoTo(pad, "play.client." + area));
                    if (tourFailure != null) yield break;
                    walkedIn = true;
                }
                // Later missions: the host switches this area off and this player is brought along.
                yield return StartCoroutine(Wait(() => ZoneScene() == area && SeesOther() < 4f, 120, "play.client." + area, () => "zone in " + ZoneScene() + ", host " + SeesOther().ToString("0.0") + " m away, areas here " + string.Join(",", ActiveAreas().ToArray())));
                if (tourFailure != null) yield break;
                Check("play.client." + area, ActiveAreas().Contains("RicksGarage"), "in " + area + ", host " + SeesOther().ToString("0.0") + " m away, areas " + string.Join(",", ActiveAreas().ToArray()));
            }
            yield return StartCoroutine(Wait(() => ZoneScene() == "RicksGarage" && SeesOther() < 4f, 120, "play.client.home", () => "zone in " + ZoneScene()));
            if (tourFailure != null) yield break;
            Check("play.client.home", true, "brought back to the garage with the host");
            yield return StartCoroutine(Pause(6));
            Check("tour.done", tourFailure == null, "playthrough");
        }
    }
}
