using System;
using System.Collections;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "controls": the two entertainment-centre doors and the DVD player's eject button in the living room.
    // The host swings the left door open, the client swings the right door open and pushes eject, then the host
    // closes the left door again. Each copy checks what it sees of the other's doors and button.
    public sealed partial class TogetherRuntime
    {
        const string LeftDoor = "/Door_L_Pivot#", RightDoor = "/Door_R_Pivot#", EjectButton = "/EjectButton#";

        NetItem FindControl(string path)
        {
            foreach (NetItem item in items.Values) if (item.Control != null && item.Key.Contains(path)) return item;
            return null;
        }

        static float Amount(NetItem item) { return Mathf.Clamp01(item.Control.NormalizedAxisValue); }

        // Where a hand takes hold of a control: the middle of its colliders on the game's Interactable layer (the
        // ones the hand's grab trigger looks for).
        static Vector3 PanelCentre(NetItem item)
        {
            int layer = LayerMask.NameToLayer("Interactable");
            Bounds b = new Bounds(); bool any = false;
            System.Text.StringBuilder seen = new System.Text.StringBuilder();
            foreach (Collider c in item.Grab.GetComponentsInChildren<Collider>())
            {
                seen.Append(" " + c.name + "@" + LayerMask.LayerToName(c.gameObject.layer) + (c.isTrigger ? "(trigger)" : "") + " " + V(c.bounds.center) + " size " + V(c.bounds.size));
                if (!c.enabled || c.gameObject.layer != layer) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return any ? b.center : item.Control.transform.position;
        }

        // Takes hold of a control from the front (the side facing the room), retrying a little closer each time.
        IEnumerator GrabControl(Handedness side, NetItem item, string check)
        {
            Vector3 centre = PanelCentre(item);
            Vector3 front = new Vector3(0, 0, -1);
            yield return StartCoroutine(Reach(side, () => centre + front * 0.15f, 0.03f, 15, check));
            if (tourFailure != null) yield break;
            for (int attempt = 0; attempt < 5 && !item.Grab.IsCurrInHand; attempt++)
            {
                float off = 0.05f - attempt * 0.02f;
                RigHandSpeed = 0.3f;
                yield return StartCoroutine(Reach(side, () => centre + front * off, 0.02f, 8, check));
                RigHandSpeed = 1.2f;
                if (tourFailure != null) yield break;
                RigGrab(side, true);
                yield return StartCoroutine(Pause(0.4f));
                if (item.Grab.IsCurrInHand) break;
                Log("Control grab attempt " + (attempt + 1) + " missed: " + DescribeHand(side));
                RigGrab(side, false);
                yield return StartCoroutine(Pause(0.3f));
            }
            yield return StartCoroutine(Wait(() => item.Grab.IsCurrInHand, 2, check, () => "the game did not grab " + item.Grab.name + ": " + DescribeHand(side)));
        }

        // Swings the held door around its hinge until its travel passes the goal (up: amount >= goal, else <= goal).
        // The hand leads the door's handle by 20 degrees: a hand that runs ahead of a lagging door loses it.
        IEnumerator Swing(Handedness side, NetItem item, bool up, float goal, string check)
        {
            Transform hinge = item.Control.transform;
            Vector3 pivot = hinge.position;
            // A hinge's travel grows with its angle about its own up axis (GrabbableHinge.ForceOrientTransformToAxisValue).
            float sign = (up ? 1 : -1) * Mathf.Sign(Vector3.Dot(hinge.up, Vector3.up));
            float until = Time.realtimeSinceStartup + 25;
            Func<bool> done = () => up ? Amount(item) >= goal : Amount(item) <= goal;
            while (!done())
            {
                if (Time.realtimeSinceStartup > until || !item.Grab.IsCurrInHand) { tourFailure = check; Check(check, false, "door at " + Amount(item).ToString("0.00") + " (wanted " + (up ? ">= " : "<= ") + goal + "), held " + item.Grab.IsCurrInHand); yield break; }
                Vector3 handle = PanelCentre(item);
                Vector3 next = pivot + Quaternion.AngleAxis(sign * 20f, Vector3.up) * (handle - pivot);
                next.y = handle.y;
                RigSetHand(side, next + (LocalHand(side).TrackedTransform.position - GrabPoint(side)));
                yield return StartCoroutine(Pause(0.1f));
            }
        }

        // Pushes a button with the hand, along the axis it travels in.
        IEnumerator PushButton(Handedness side, PushableButton button, string check)
        {
            bool restsUp = (PushableButton.RestingPositionType)buttonRestField.GetValue(button) == PushableButton.RestingPositionType.UpperLimit;
            Vector3 outward = button.GetAxisVector().normalized * (restsUp ? 1 : -1);
            Vector3 face = button.transform.position;
            yield return StartCoroutine(Reach(side, () => face + outward * 0.12f, 0.03f, 15, check));
            if (tourFailure != null) yield break;
            RigHandSpeed = 0.25f;
            yield return StartCoroutine(Reach(side, () => face - outward * 0.05f, 0.02f, 6, check));
            RigHandSpeed = 1.2f;
            if (tourFailure != null) { Log("Button push: " + DescribeHand(side)); yield break; }
            yield return StartCoroutine(Pause(0.5f));
            yield return StartCoroutine(Reach(side, () => face + outward * 0.15f, 0.03f, 10, check));
        }

        IEnumerator ControlsHost()
        {
            RigHead = new Vector3(-0.3f, 1.35f, -0.4f); RigYaw = 0;
            RigSetHand(Handedness.Right, new Vector3(-0.2f, 1.1f, -0.2f)); RigSetHand(Handedness.Left, new Vector3(-0.6f, 1.1f, -0.2f));
            yield return StartCoroutine(Wait(() => AcceptedCount >= Mathf.Max(1, expectPlayers - 1), 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            NetItem left = null, right = null;
            yield return StartCoroutine(Wait(() => (left = FindControl(LeftDoor)) != null && (right = FindControl(RightDoor)) != null, 20, "controls.found", "the cabinet doors are not synced"));
            if (tourFailure != null) yield break;
            labWatched.Add(left.Id); labWatched.Add(right.Id);
            yield return StartCoroutine(Pause(4));
            // 1. Open the left door and let go.
            yield return StartCoroutine(GrabControl(Handedness.Left, left, "controls.host.grabbed"));
            if (tourFailure != null) yield break;
            Check("controls.host.grabbed", left.Owner == 0, "left door in hand, owner " + left.Owner);
            yield return StartCoroutine(Swing(Handedness.Left, left, true, 0.98f, "controls.host.opened"));
            if (tourFailure != null) yield break;
            // Hold it open a moment (this door swings slowly shut by itself once let go).
            yield return StartCoroutine(Pause(4));
            float held = Amount(left);
            RigGrab(Handedness.Left, false);
            RigSetHand(Handedness.Left, new Vector3(-0.6f, 1.1f, -0.3f));
            yield return StartCoroutine(Pause(1));
            Check("controls.host.opened", held > 0.9f && !left.Grab.IsCurrInHand, "left door held open at " + held.ToString("0.00") + ", " + Amount(left).ToString("0.00") + " after letting go");
            // 2. The client opens the right door: it swings here too.
            float rightStart = Amount(right);
            yield return StartCoroutine(Wait(() => right.Owner != 0 && Mathf.Abs(Amount(right) - rightStart) > 0.9f, 90, "controls.host.sees.door", () => "right door here at " + Amount(right).ToString("0.00") + " (started " + rightStart.ToString("0.00") + "), owner " + right.Owner));
            if (tourFailure != null) yield break;
            Check("controls.host.sees.door", right.Following, "right door here at " + Amount(right).ToString("0.00") + ", locks " + LockBits(right.Control) + ", owner " + right.Owner + " epoch " + right.Epoch);
            // 3. The client pushes eject: the press is replayed here, so the DVD player's own listener runs.
            NetTrigger eject = TriggerByPath(EjectButton);
            yield return StartCoroutine(Wait(() => eject != null && eject.Replays > 0, 60, "controls.host.button", () => eject == null ? "eject button not hooked" : "no press arrived (fires " + eject.Fires + ")"));
            if (tourFailure != null) yield break;
            Check("controls.host.button", eject.Fires > 0, "eject replayed " + eject.Replays + "x, game listener fired " + eject.Fires + "x");
            // 4. The left door swings shut by itself and latches (the game's own spring) unless it latched open when let
            // go; then close it by hand. Either way the client sees it latch shut.
            float shutBy = Time.realtimeSinceStartup + 10;
            while (Time.realtimeSinceStartup < shutBy && !(Amount(left) < 0.05f && (LockBits(left.Control) & 10) != 0)) yield return null;
            if (!(Amount(left) < 0.05f))
            {
                // A hand can slip off a swinging door; like a player, take hold again (three tries).
                for (int attempt = 1; attempt <= 3 && Amount(left) > 0.05f; attempt++)
                {
                    Log("Left door open (" + Amount(left).ToString("0.00") + ", locks " + LockBits(left.Control) + "): closing it by hand, try " + attempt);
                    tourFailure = null;
                    yield return StartCoroutine(GrabControl(Handedness.Left, left, "~controls.host.closed"));
                    if (tourFailure == null) yield return StartCoroutine(Swing(Handedness.Left, left, false, 0.02f, "~controls.host.closed"));
                    yield return StartCoroutine(Pause(1));
                    RigGrab(Handedness.Left, false);
                    yield return StartCoroutine(Pause(1));
                    RigSetHand(Handedness.Left, new Vector3(-0.6f, 1.1f, -0.3f));
                    yield return StartCoroutine(Pause(1));
                }
                tourFailure = null;
            }
            yield return StartCoroutine(Wait(() => Amount(left) < 0.05f && (LockBits(left.Control) & 10) != 0, 60, "controls.host.closed", () => "left door at " + Amount(left).ToString("0.00") + ", locks " + LockBits(left.Control)));
            if (tourFailure != null) yield break;
            Check("controls.host.closed", Amount(left) < 0.05f && (LockBits(left.Control) & 10) != 0, "left door at " + Amount(left).ToString("0.00") + ", locks " + LockBits(left.Control));
            yield return StartCoroutine(Pause(6));
            Check("tour.done", tourFailure == null, "controls");
        }

        IEnumerator ControlsClient()
        {
            RigHead = new Vector3(0.4f, 1.35f, -0.4f); RigYaw = 0;
            RigSetHand(Handedness.Right, new Vector3(0.6f, 1.1f, -0.2f)); RigSetHand(Handedness.Left, new Vector3(0.2f, 1.1f, -0.2f));
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            NetItem left = null, right = null;
            yield return StartCoroutine(Wait(() => (left = FindControl(LeftDoor)) != null && (right = FindControl(RightDoor)) != null, 20, "controls.found", "the cabinet doors are not synced"));
            if (tourFailure != null) yield break;
            labWatched.Add(left.Id); labWatched.Add(right.Id);
            // 1. The host opens the left door: it swings open here too.
            yield return StartCoroutine(Wait(() => Amount(left) > 0.9f && left.Following, 90, "controls.client.sees.door", () => "left door here at " + Amount(left).ToString("0.00") + ", following " + left.Following));
            if (tourFailure != null) yield break;
            Check("controls.client.sees.door", true, "left door here at " + Amount(left).ToString("0.00") + ", locks " + LockBits(left.Control));
            yield return StartCoroutine(Wait(() => !left.Following || FirstPeer() == null || FirstPeer().Pose.Left.Held == 0, 20, "controls.client.sees.release", "the host never let go of the left door"));
            if (tourFailure != null) yield break;
            // 2. Open the right door.
            yield return StartCoroutine(GrabControl(Handedness.Right, right, "controls.client.grabbed"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => right.Owner == LocalId && right.Epoch >= 1, 5, "controls.client.grabbed", () => "the host never confirmed the claim (owner " + right.Owner + ")"));
            if (tourFailure != null) yield break;
            Check("controls.client.grabbed", true, "right door in hand, owner " + right.Owner + " epoch " + right.Epoch);
            yield return StartCoroutine(Swing(Handedness.Right, right, false, 0.02f, "controls.client.opened"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(4));
            float held = Amount(right);
            RigGrab(Handedness.Right, false);
            RigSetHand(Handedness.Right, new Vector3(0.6f, 1.1f, -0.3f));
            yield return StartCoroutine(Pause(1));
            Check("controls.client.opened", held < 0.1f && !right.Grab.IsCurrInHand, "right door held open at " + held.ToString("0.00") + ", " + Amount(right).ToString("0.00") + " after letting go");
            // 3. Push the DVD player's eject button.
            NetTrigger eject = TriggerByPath(EjectButton);
            if (eject == null) { tourFailure = "controls.client.button"; Check("controls.client.button", false, "eject button not hooked"); yield break; }
            PushableButton button = (PushableButton)eject.Source;
            for (int attempt = 0; attempt < 3 && eject.Fires == 0 && tourFailure == null; attempt++) yield return StartCoroutine(PushButton(Handedness.Right, button, "controls.client.button"));
            if (tourFailure != null) yield break;
            Check("controls.client.button", eject.Fires > 0, "pushed eject here: game listener fired " + eject.Fires + "x");
            // 4. The host closes the left door: it latches here too.
            yield return StartCoroutine(Wait(() => Amount(left) < 0.05f && (LockBits(left.Control) & 10) != 0, 90, "controls.client.sees.lock", () => "left door here at " + Amount(left).ToString("0.00") + ", locks " + LockBits(left.Control)));
            if (tourFailure != null) yield break;
            Check("controls.client.sees.lock", true, "left door here at " + Amount(left).ToString("0.00") + ", locks " + LockBits(left.Control));
            yield return StartCoroutine(Pause(4));
            Check("tour.done", tourFailure == null, "controls");
        }
    }
}
