using System;
using System.Collections;
using OwlchemyVR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // Scripted lab tours. Each copy runs its own half from what it can see (no tour messages on the wire), drives the
    // lab rig like a player would, and logs RT-CHECK lines. A wait that times out fails its check with the reason.
    public sealed partial class TogetherRuntime
    {
        bool tourStarted;
        string tourFailure;

        void TourUpdate()
        {
            if (tourStarted || labTour.Length == 0) return;
            if (SceneManager.GetActiveScene().name != "LivingRoom" || Master == null || !(RigActive || FlatMode)) return;
            tourStarted = true;
            if (labTour == "survey") StartCoroutine(Survey());
            else if (labTour == "portrait") StartCoroutine(Portrait());
            else if (labTour == "full") StartCoroutine(FullGame());
            else if (labTour == "flat") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? FlatHost() : FlatClient());
            else if (labTour == "enemies") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? EnemiesHost() : EnemiesClient());
            else if (labTour == "death") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? DeathHost() : DeathClient());
            else if (labTour == "playthrough") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? PlayHost() : PlayClient());
            else if (labTour == "boss") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? BossHost() : BossClient());
            else if (labTour == "latejoin") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? LateHost() : LateClient());
            else if (labTour == "garage") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? GarageHost() : GarageClient());
            else if (labTour == "spawns") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? SpawnsHost() : SpawnsClient());
            else if (labTour == "spectator") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? SpectatorHost() : SpectatorClient());
            else if (labTour == "story") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? StoryHost() : StoryClient());
            else if (labTour == "controls") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? ControlsHost() : ControlsClient());
            else if (labTour == "handoff") StartCoroutine(Hosting || autoRole == "host" || autoRole == "steamhost" ? HandoffHost() : HandoffClient());
            else Log("Unknown tour " + labTour);
        }

        // The item the hand-off tour passes: a comic book lying on the entertainment centre.
        const string HandoffItem = "ComicBook_RickAndMortyVol1";
        static readonly Vector3 MeetPoint = new Vector3(1.0f, 1.15f, 0.1f);   // process-scoped (a constant)
        static readonly Vector3 DropPoint = new Vector3(1.7f, 1.0f, 0.3f);   // process-scoped (a constant)

        NetItem FindItem(string name)
        {
            foreach (NetItem item in items.Values) if (item.Grab != null && item.Grab.name == name) return item;
            return null;
        }

        // Waits for a condition; on timeout logs a failed check with the reason and stops the tour.
        IEnumerator Wait(Func<bool> condition, float seconds, string check, string why) { return Wait(condition, seconds, check, () => why); }

        IEnumerator Wait(Func<bool> condition, float seconds, string check, Func<string> why)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until) { tourFailure = check; Check(check, false, "timed out after " + seconds + " s: " + why()); yield break; }
                yield return null;
            }
        }

        IEnumerator Pause(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }

        // Moves the rig's hand so that its grab point (not the wrist) reaches a world point.
        IEnumerator Reach(Handedness side, Func<Vector3> target, float tolerance, float seconds, string check)
        {
            HandController hand = LocalHand(side);
            float until = Time.realtimeSinceStartup + seconds;
            while (true)
            {
                Vector3 goal = target();
                Vector3 offset = hand.TrackedTransform.position - GrabPoint(side);
                RigSetHand(side, goal + offset);
                if ((GrabPoint(side) - goal).magnitude <= tolerance) yield break;
                if (Time.realtimeSinceStartup > until) { tourFailure = check; Check(check, false, "hand did not reach " + V(goal) + " (grab point at " + V(GrabPoint(side)) + ")"); yield break; }
                yield return null;
            }
        }

        // What the game's hand sees: for a failed grab, the reason is usually here.
        string DescribeHand(Handedness side)
        {
            HandController hand = LocalHand(side);
            if (hand == null) return "no hand";
            InteractionHandController ih = hand.InteractionHandController;
            System.Reflection.BindingFlags f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object closest = typeof(InteractionHandController).GetField("currClosestGrabblableItem", f).GetValue(ih);
            object failed = typeof(InteractionHandController).GetField("hasDoneFailedGrab", f).GetValue(ih);
            object active = typeof(InteractionHandController).GetField("isInteractionActive", f).GetValue(ih);
            OnStayTriggerRecorder trigger = (OnStayTriggerRecorder)typeof(InteractionHandController).GetField("handGrabbleTrigger", f).GetValue(ih);
            System.Text.StringBuilder s = new System.Text.StringBuilder();
            s.Append("closest=" + (closest != null ? ((Component)closest).name : "none") + " failedGrab=" + failed + " active=" + active + " input=" + hand.TrackedController.GetType().Name + " button=" + ih.IsGrabInputButton() + " grab point " + V(GrabPoint(side)) + " in trigger:");
            foreach (Collider c in trigger.GetCollidersWithinTrigger()) s.Append(" " + (c != null ? c.name + "@" + LayerMask.LayerToName(c.gameObject.layer) : "null"));
            Debug_TrackedInputController dbg = hand.GetDebugTrackedController();
            if (dbg != null) s.Append(" dbg.isGrabbing=" + dbg.isGrabbing + " dbg.enabled=" + dbg.enabled + " current=" + typeof(Debug_TrackedInputController).GetField("currentIsGrabbing", f).GetValue(dbg) + " ready=" + dbg.IsControllerReady() + " same=" + (dbg == hand.TrackedController));
            foreach (Collider c in Physics.OverlapSphere(GrabPoint(side), 0.06f, ~0, QueryTriggerInteraction.Collide)) s.Append(" near:" + c.name + "@" + LayerMask.LayerToName(c.gameObject.layer) + (c.isTrigger ? "(trigger)" : "") + (Physics.GetIgnoreLayerCollision(c.gameObject.layer, trigger.gameObject.layer) ? "(ignored)" : ""));
            s.Append(" trigger " + trigger.name + " layer " + LayerMask.LayerToName(trigger.gameObject.layer) + " enabled " + trigger.GetComponent<Collider>().enabled);
            return s.ToString();
        }

        // Picks an item up like a player: hover above it, come down slowly, close the hand; if the game finds nothing
        // in reach, open, go 2 cm lower and try again (a hand that sweeps in sideways shoves small items away).
        IEnumerator PickUp(Handedness side, NetItem item, string check)
        {
            yield return StartCoroutine(Reach(side, () => item.Body.worldCenterOfMass + Vector3.up * 0.15f, 0.03f, 15, check));
            if (tourFailure != null) yield break;
            for (int attempt = 0; attempt < 5 && !item.Grab.IsCurrInHand; attempt++)
            {
                float above = 0.06f - attempt * 0.02f;
                RigHandSpeed = 0.3f;
                yield return StartCoroutine(Reach(side, () => item.Body.worldCenterOfMass + Vector3.up * above, 0.02f, 8, check));
                RigHandSpeed = 1.2f;
                if (tourFailure != null) yield break;
                RigGrab(side, true);
                yield return StartCoroutine(Pause(0.4f));
                if (item.Grab.IsCurrInHand) break;
                Log("Grab attempt " + (attempt + 1) + " missed: " + DescribeHand(side));
                RigGrab(side, false);
                yield return StartCoroutine(Pause(0.3f));
            }
        }

        Peer FirstPeer() { foreach (Peer p in peers.Values) if (p.Accepted && p.HasPose) return p; return null; }

        IEnumerator CheckAvatar(string role)
        {
            yield return StartCoroutine(Wait(() => FirstPeer() != null && FirstPeer().Avatar != null, 60, "avatar.seen", "no remote player pose arrived"));
            if (tourFailure != null) yield break;
            Peer p = FirstPeer();
            Check("avatar.morty", p.Avatar.HeadIsMorty && p.Avatar.HandsAreMorty, "Morty head " + p.Avatar.HeadIsMorty + ", Morty hands " + p.Avatar.HandsAreMorty);
            yield return StartCoroutine(Pause(1.5f));
            float head = (p.Avatar.Head.position - p.Pose.HeadPosition).magnitude;
            float right = (p.Avatar.RightHand.position - p.Pose.Right.Position).magnitude;
            Check("avatar.follow", head < 0.15f && right < 0.15f, "head " + head.ToString("0.000") + " m and right hand " + right.ToString("0.000") + " m from the pose");
            Vector3 before = p.Avatar.Head.position;
            yield return StartCoroutine(Pause(1.5f));
            float moved = (p.Avatar.Head.position - before).magnitude;
            Check("avatar.moves", moved > 0.005f, "remote head moved " + moved.ToString("0.000") + " m in 1.5 s");
        }

        IEnumerator HandoffHost()
        {
            RigHead = new Vector3(0.6f, 1.35f, -0.5f); RigYaw = 10;
            RigSetHand(Handedness.Right, new Vector3(0.8f, 1.1f, -0.2f)); RigSetHand(Handedness.Left, new Vector3(0.4f, 1.1f, -0.2f));
            yield return StartCoroutine(Wait(() => AcceptedCount >= Mathf.Max(1, expectPlayers - 1), 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(CheckAvatar("host"));
            NetItem item = null;
            yield return StartCoroutine(Wait(() => (item = FindItem(HandoffItem)) != null, 20, "handoff.item", HandoffItem + " not found"));
            if (tourFailure != null) yield break;
            labWatched.Add(item.Id);
            yield return StartCoroutine(Pause(3));
            // 1. Pick it up and carry it to the meeting point.
            yield return StartCoroutine(PickUp(Handedness.Right, item, "handoff.host.reach"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => item.Grab.IsCurrInHand, 3, "handoff.host.grabbed", () => "the game did not grab it: " + DescribeHand(Handedness.Right) + " item at " + V(item.Body.worldCenterOfMass)));
            if (tourFailure != null) yield break;
            Check("handoff.host.grabbed", item.Owner == 0, "owner " + item.Owner + " epoch " + item.Epoch);
            yield return StartCoroutine(Reach(Handedness.Right, () => MeetPoint, 0.04f, 10, "handoff.host.carried"));
            if (tourFailure != null) yield break;
            float inHand = (item.Body.worldCenterOfMass - GrabPoint(Handedness.Right)).magnitude;
            Check("handoff.host.carried", item.Grab.IsCurrInHand && inHand < 0.15f, "at the meeting point with the item " + inHand.ToString("0.000") + " m from the grab point");
            // 2. The client takes it out of this hand.
            yield return StartCoroutine(Wait(() => item.Owner != 0, 40, "handoff.host.owner", "no client claimed it"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(0.5f));
            Check("handoff.host.released", !item.Grab.IsCurrInHand && item.Following, "held here " + item.Grab.IsCurrInHand + ", following " + item.Following + ", owner " + item.Owner + " epoch " + item.Epoch);
            RigGrab(Handedness.Right, false);
            RigSetHand(Handedness.Right, new Vector3(0.8f, 1.1f, -0.2f));
            // 3. It follows the client's hand here.
            Peer p = FirstPeer();
            yield return StartCoroutine(Wait(() => p.Pose.Right.Held == item.Id && (item.Body.position - p.Avatar.RightHand.position).magnitude < 0.3f, 15, "handoff.host.sees.carry", "the item is not in the client's hand here"));
            if (tourFailure != null) yield break;
            Check("handoff.host.sees.carry", true, "item " + (item.Body.position - p.Avatar.RightHand.position).magnitude.ToString("0.000") + " m from the client's hand, remote grip closed " + p.Avatar.AnimatorClosed(Handedness.Right));
            Check("handoff.host.grip", p.Avatar.AnimatorClosed(Handedness.Right), "client's right hand is drawn closed while holding");
            // 4. The client drops it; it comes to rest at the same place here.
            yield return StartCoroutine(Wait(() => p.Pose.Right.Held == 0, 20, "handoff.host.dropped", "the client never let go"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(5));
            Check("handoff.host.rest", item.Following && item.Owner != 0, "at " + V(item.Body.position));
            yield return StartCoroutine(Pause(4));
            Check("tour.done", tourFailure == null, "handoff");
        }

        IEnumerator HandoffClient()
        {
            RigHead = new Vector3(1.6f, 1.35f, -0.5f); RigYaw = -20;
            RigSetHand(Handedness.Right, new Vector3(1.8f, 1.1f, -0.2f)); RigSetHand(Handedness.Left, new Vector3(1.4f, 1.1f, -0.2f));
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(CheckAvatar("client"));
            NetItem item = null;
            yield return StartCoroutine(Wait(() => (item = FindItem(HandoffItem)) != null, 20, "handoff.item", HandoffItem + " not found"));
            if (tourFailure != null) yield break;
            labWatched.Add(item.Id);
            Peer host = FirstPeer();
            // 1. The host picks it up: here it is drawn in the host's closed hand.
            yield return StartCoroutine(Wait(() => host.Pose.Right.Held == item.Id && (item.Body.position - host.Avatar.RightHand.position).magnitude < 0.3f, 40, "handoff.client.sees.host", "the item is not in the host's hand here"));
            if (tourFailure != null) yield break;
            Check("handoff.client.sees.host", true, "item " + (item.Body.position - host.Avatar.RightHand.position).magnitude.ToString("0.000") + " m from the host's hand");
            Check("handoff.client.grip", host.Avatar.AnimatorClosed(Handedness.Right), "host's right hand is drawn closed while holding");
            // It arrives at the meeting point here and stays there: the host is holding it still.
            float near = -1;
            yield return StartCoroutine(Wait(() => { if ((item.Body.worldCenterOfMass - MeetPoint).magnitude >= 0.2f) near = -1; else if (near < 0) near = Time.realtimeSinceStartup; return near > 0 && Time.realtimeSinceStartup - near > 2f; }, 20, "handoff.client.followed", () => "the item never settled at the meeting point here (at " + V(item.Body.worldCenterOfMass) + ")"));
            if (tourFailure != null) yield break;
            Check("handoff.client.followed", true, "item " + (item.Body.worldCenterOfMass - MeetPoint).magnitude.ToString("0.000") + " m from the meeting point for 2 s");
            // 2. Take it out of the host's hand.
            // From the side the host is not holding it: approach the item, not the host's hand.
            RigHandSpeed = 0.5f;
            yield return StartCoroutine(Reach(Handedness.Right, () => item.Body.worldCenterOfMass + new Vector3(0.04f, 0, 0), 0.03f, 15, "handoff.client.reach"));
            RigHandSpeed = 1.2f;
            if (tourFailure != null) yield break;
            RigGrab(Handedness.Right, true);
            yield return StartCoroutine(Wait(() => item.Grab.IsCurrInHand, 3, "handoff.client.grabbed", () => "the game did not grab it: " + DescribeHand(Handedness.Right) + " item at " + V(item.Body.worldCenterOfMass)));
            if (tourFailure != null) yield break;
            Check("handoff.client.grabbed", true, "grabbed with owner " + item.Owner);
            yield return StartCoroutine(Wait(() => item.Owner == LocalId && item.Epoch >= 2, 5, "handoff.client.owner", "the host never confirmed the claim (owner " + item.Owner + ", epoch " + item.Epoch + ")"));
            if (tourFailure != null) yield break;
            Check("handoff.client.owner", true, "owner " + item.Owner + " epoch " + item.Epoch);
            // 3. Carry it over and drop it.
            RigSetHand(Handedness.Right, DropPoint + (LocalHand(Handedness.Right).TrackedTransform.position - GrabPoint(Handedness.Right)));
            yield return StartCoroutine(Pause(4));
            Check("handoff.client.carried", item.Grab.IsCurrInHand && (item.Body.position - DropPoint).magnitude < 0.25f, "item " + (item.Body.position - DropPoint).magnitude.ToString("0.000") + " m from the drop point, held " + item.Grab.IsCurrInHand);
            RigGrab(Handedness.Right, false);
            RigSetHand(Handedness.Right, new Vector3(1.8f, 1.1f, -0.2f));
            yield return StartCoroutine(Pause(8));
            Check("handoff.client.rest", !item.Grab.IsCurrInHand && item.Owner == LocalId, "at " + V(item.Body.position));
            Check("tour.done", tourFailure == null, "handoff");
        }
    }
}
