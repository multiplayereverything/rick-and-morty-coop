using System.Collections;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "spectator": the host turns spectator mode on, puts its spectator camera out in the room (a room spot if the
    // room has one, else the game's own JumpTo to a spot in front of it), and the client sees that camera float there.
    // Then the host cycles cameras until it watches through the client's eyes, and saves what its window shows.
    public sealed partial class TogetherRuntime
    {
        static readonly Vector3 CameraSpot = new Vector3(0.7f, 1.6f, 0.1f);   // process-scoped (a constant)

        IEnumerator SpectatorHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.6f); RigYaw = 90;
            yield return StartCoroutine(Wait(() => FirstPeer() != null && FirstPeer().Avatar != null, 120, "spectator.host.on", "no client avatar"));
            if (tourFailure != null) yield break;
            SpectatorInterfaceController ui = SpectatorUi();
            yield return StartCoroutine(Wait(() => (ui = SpectatorUi()) != null, 20, "spectator.host.on", "the game has no spectator interface"));
            if (tourFailure != null) yield break;
            ui.SetSpectating(true);
            yield return StartCoroutine(Pause(2));
            PlayerPose me;
            Check("spectator.host.on", ui.IsSpectating && CaptureLocalPose(out me) && me.Spectating, "spectating " + ui.IsSpectating);
            // 1. The camera out in the room.
            SpectatorModeLogic logic = ui.SpectatorModeLogic;
            SpectatorCameraController cam = logic.SpectatorCameraController;
            logic.CycleTarget();
            yield return StartCoroutine(Pause(1));
            if (SpectatorView >= 0 || cam.IsInFirstPersonView)
            {
                // No camera spots in this room: put it out with the game's own call.
                StopWatching();
                GameObject spot = new GameObject("RickAndMortyTogether spot");
                spot.transform.position = CameraSpot; spot.transform.rotation = Quaternion.Euler(10, -90, 0);
                cam.JumpTo(spot.transform);
                Object.Destroy(spot);
                Log("Spectator: no room spots here, camera put at " + V(CameraSpot));
            }
            yield return StartCoroutine(Pause(1));
            Check("spectator.host.camera", !cam.IsInFirstPersonView, "camera out at " + V(cam.transform.position));
            yield return StartCoroutine(Pause(8));
            // 2. Cycle to the client's view.
            Peer client = FirstPeer();
            for (int i = 0; i < 12 && SpectatorView != client.Id; i++) { logic.CycleTarget(); yield return StartCoroutine(Pause(0.5f)); }
            yield return StartCoroutine(Pause(2));
            ExternalCameraController ext = GlobalStorage.Instance.ExternalCameraController;
            bool watching = SpectatorView == client.Id && ext.Target == client.Avatar.Head && client.Avatar.HeadHidden;
            float off = (ext.ExternalCamera.transform.position - client.Pose.HeadPosition).magnitude;
            Check("spectator.host.watch", watching && off < 0.2f, "watching " + SpectatorView + ", window camera " + off.ToString("0.000") + " m from the client's head, head hidden " + client.Avatar.HeadHidden);
            string file = System.IO.Path.Combine(Root, "spectator-host.png");
            Application.CaptureScreenshot(file, 3);
            yield return StartCoroutine(Wait(() => System.IO.File.Exists(file), 10, "spectator.host.saved", "no screenshot"));
            // 3. Cycle once more: back to its own head, the client's head shown again.
            logic.CycleTarget();
            yield return StartCoroutine(Pause(1));
            Check("spectator.host.back", SpectatorView < 0 && !client.Avatar.HeadHidden && ext.Target != client.Avatar.Head, "view " + SpectatorView + ", head hidden " + client.Avatar.HeadHidden);
            ui.SetSpectating(false);
            yield return StartCoroutine(Pause(4));
            Check("tour.done", tourFailure == null, "spectator");
        }

        IEnumerator SpectatorClient()
        {
            RigHead = new Vector3(1.3f, 1.35f, -0.6f); RigYaw = -90;
            yield return StartCoroutine(Wait(() => FirstPeer() != null && FirstPeer().Pose.Spectating, 120, "spectator.client.sees.on", "the host never spectated"));
            if (tourFailure != null) yield break;
            Check("spectator.client.sees.on", true, "host is spectating");
            Peer host = FirstPeer();
            yield return StartCoroutine(Wait(() => host.Pose.CameraShown && host.Avatar.Camera != null && host.Avatar.Camera.gameObject.activeInHierarchy && (host.Avatar.Camera.position - host.Pose.CameraPosition).magnitude < 0.1f, 60, "spectator.client.sees.camera", () => "camera shown " + host.Pose.CameraShown + ", copy " + (host.Avatar.Camera != null)));
            if (tourFailure != null) yield break;
            Check("spectator.client.sees.camera", true, "host's spectator camera here at " + V(host.Avatar.Camera.position) + " (" + host.Avatar.Camera.GetComponentsInChildren<Renderer>().Length + " renderers)");
            string file = System.IO.Path.Combine(Root, "spectator-c1.png");
            yield return StartCoroutine(Pause(1));
            Application.CaptureScreenshot(file, 3);
            yield return StartCoroutine(Wait(() => !host.Pose.Spectating, 90, "spectator.client.sees.off", "the host never stopped spectating"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(1));
            Check("spectator.client.sees.off", host.Avatar.Camera == null || !host.Avatar.Camera.gameObject.activeSelf, "camera hidden after the host stopped");
            yield return StartCoroutine(Pause(3));
            Check("tour.done", tourFailure == null, "spectator");
        }
    }
}
