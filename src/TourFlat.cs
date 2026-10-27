using System.Collections;
using System.Collections.Generic;
using OwlchemyVR;
using OwlchemyVR.Teleportation;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "flat": the host plays in flat-screen mode (-rt-flat), driven through FlatSim the way the keyboard and mouse
    // drive it: look around, walk, look at the comic book and click to grab it, press the trigger and
    // menu keys, teleport to a pad in view. The client (a normal lab rig) sees the host holding the comic.
    public sealed partial class TogetherRuntime
    {
        static float SignedYaw(Vector3 from, Vector3 to) { from.y = 0; to.y = 0; return Vector3.Angle(from, to) * Mathf.Sign(Vector3.Cross(from, to).y); }

        // Turn the view onto a point with the mouse, walking up to within the given distance of it.
        IEnumerator FlatLookAt(FlatInput i, System.Func<Vector3> target, float within, float seconds)
        {
            Transform head = Master.HMDCamera.transform;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                Vector3 to = target() - head.position;
                float yaw = SignedYaw(head.forward, to);
                float pitch = Mathf.Atan2(-to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - Mathf.DeltaAngle(0, head.eulerAngles.x);
                float far = new Vector2(to.x, to.z).magnitude - within;
                i.Look = new Vector2(Mathf.Clamp(yaw * 0.3f, -3, 3), Mathf.Clamp(pitch * 0.3f, -3, 3));
                i.Move = far > 0 && Mathf.Abs(yaw) < 20 ? Vector3.forward * Mathf.Clamp01(far * 2) : Vector3.zero;
                if (far <= 0 && Mathf.Abs(yaw) < 1f && Mathf.Abs(pitch) < 1f) break;
                yield return null;
            }
            i.Look = Vector2.zero; i.Move = Vector3.zero;
        }

        IEnumerator FlatHost()
        {
            FlatInput i = FlatSim = new FlatInput();
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(3));
            Check("flat.on", FlatMode && !RigActive, "flat mode " + FlatMode);
            Transform head = Master.HMDCamera.transform;
            // 1. Look: a quarter turn to the right with the mouse.
            float yaw0 = head.eulerAngles.y;
            for (int f = 0; f < 30; f++) { i.Look = new Vector2(3, 0); yield return null; }
            i.Look = Vector2.zero;
            yield return null;
            Check("flat.look", Mathf.Abs(Mathf.DeltaAngle(yaw0, head.eulerAngles.y) - 90f) < 3f, "turned " + Mathf.DeltaAngle(yaw0, head.eulerAngles.y).ToString("0.0") + " degrees");
            for (int f = 0; f < 30; f++) { i.Look = new Vector2(-3, 0); yield return null; }
            i.Look = Vector2.zero;
            // 2. Walk forward one second.
            Vector3 p0 = head.position;
            i.Move = Vector3.forward;
            yield return StartCoroutine(Pause(1));
            i.Move = Vector3.zero;
            yield return null;
            Vector3 walked = head.position - p0; walked.y = 0;
            Check("flat.walk", walked.magnitude > 0.8f, "walked " + walked.magnitude.ToString("0.00") + " m in 1 s");
            // 3. Look at the comic book, step within reach, click.
            NetItem comic = null;
            yield return StartCoroutine(Wait(() => (comic = FindItem(HandoffItem)) != null, 20, "flat.grab", HandoffItem + " not found"));
            if (tourFailure != null) yield break;
            labWatched.Add(comic.Id);
            yield return StartCoroutine(FlatLookAt(i, () => comic.Body.worldCenterOfMass, 1.2f, 20));
            i.GrabRight = true;
            yield return StartCoroutine(Wait(() => comic.Grab.IsCurrInHand, 8, "flat.grab", () => "the click did not grab it: " + DescribeHand(Handedness.Right)));
            if (tourFailure != null) yield break;
            Check("flat.grab", comic.Grab.IsCurrInHand && comic.Owner == 0, "comic in the right hand " + comic.Grab.IsCurrInHand + ", owner " + comic.Owner);
            // Lift it into view and hold it for the client to see.
            for (int f = 0; f < 20; f++) { i.Look = new Vector2(0, -1.5f); yield return null; }
            i.Look = Vector2.zero;
            yield return StartCoroutine(Pause(4));
            // 4. Trigger and menu keys reach the game's hand controllers.
            TrackedController right = LocalHand(Handedness.Right).TrackedController, left = LocalHand(Handedness.Left).TrackedController;
            i.TriggerRight = true; i.Menu = true;
            yield return null; yield return null;
            bool trigger = right.GetButton(TrackedController.ControllerButton.Trigger) && right.GetButton(TrackedController.ControllerButton.InteractCustom) && !left.GetButton(TrackedController.ControllerButton.Trigger);
            bool menu = right.GetButton(TrackedController.ControllerButton.Menu);
            i.TriggerRight = false; i.Menu = false;
            yield return null; yield return null;
            Check("flat.buttons", trigger && menu && !right.GetButton(TrackedController.ControllerButton.Trigger), "trigger " + trigger + ", menu " + menu);
            // 5. Let go, then teleport to a pad in view if this room has another one.
            i.GrabRight = false;
            yield return StartCoroutine(Pause(1));
            List<TeleportZone> pads = FlatPads();
            if (pads.Count == 0) Check("flat.teleport", true, "this room has no other teleport pad");
            else
            {
                TeleportZone pad = pads[0];
                for (int f = 0; f < 120; f++)
                {
                    Vector3 to = pad.transform.position - head.position;
                    float yaw = SignedYaw(head.forward, to);
                    float pitch = Mathf.Atan2(-to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - Mathf.DeltaAngle(0, head.eulerAngles.x);
                    i.Look = new Vector2(Mathf.Clamp(yaw * 0.3f, -3, 3), Mathf.Clamp(pitch * 0.3f, -3, 3));
                    yield return null;
                }
                i.Look = Vector2.zero;
                i.Teleport = true; yield return null; i.Teleport = false;
                yield return StartCoroutine(Pause(3));
                TeleportZone now = GlobalStorage.Instance.TeleportationController.CurrentZone;
                Check("flat.teleport", FlatTeleports == 1 && now == pad, "teleported to " + (now != null ? now.name : "none") + ", aimed at " + pad.name);
            }
            yield return StartCoroutine(Pause(4));
            Check("tour.done", tourFailure == null, "flat");
        }

        IEnumerator FlatClient()
        {
            RigHead = new Vector3(1.6f, 1.35f, -0.6f); RigYaw = -60;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            NetItem comic = null;
            yield return StartCoroutine(Wait(() => (comic = FindItem(HandoffItem)) != null && FirstPeer() != null, 30, "flat.client.sees.grab", "no comic or no host"));
            if (tourFailure != null) yield break;
            Peer host = FirstPeer();
            yield return StartCoroutine(Wait(() => host.Pose.Right.Held == comic.Id && (comic.Body.position - host.Avatar.RightHand.position).magnitude < 0.3f, 120, "flat.client.sees.grab", "the flat player's hand never held the comic here"));
            if (tourFailure != null) yield break;
            Check("flat.client.sees.grab", true, "comic " + (comic.Body.position - host.Avatar.RightHand.position).magnitude.ToString("0.000") + " m from the flat player's hand, hand drawn closed " + host.Avatar.AnimatorClosed(Handedness.Right));
            yield return StartCoroutine(Wait(() => host.Pose.Right.Held == 0, 60, "flat.client.sees.release", "the flat player never let go"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(6));
            Check("tour.done", tourFailure == null, "flat");
        }
    }
}
