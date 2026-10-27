using System;
using System.Collections.Generic;
using OwlchemyVR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // The local player's head and hands as the game tracks them, and (lab only) a scripted rig that stands in for a
    // headset: it moves the head camera and both debug hands and presses grab through Debug_TrackedInputController.
    public sealed partial class TogetherRuntime
    {
        readonly Dictionary<HandController, AnimatedHandController> handVisuals = new Dictionary<HandController, AnimatedHandController>();

        public HandController LocalHand(Handedness side)
        {
            MasterHMDAndInputController m = Master;
            if (m == null) return null;
            foreach (HandController h in m.Hands) if (h != null && h.Handedness == side) return h;
            return null;
        }

        AnimatedHandController VisualOf(HandController hand)
        {
            AnimatedHandController v;
            if (!handVisuals.TryGetValue(hand, out v) || v == null) { v = hand.GetComponentInChildren<AnimatedHandController>(true); handVisuals[hand] = v; }
            return v;
        }

        HandPose CaptureHand(HandController hand)
        {
            HandPose h = new HandPose { Rotation = Quaternion.identity };
            if (hand == null) return h;
            AnimatedHandController visual = VisualOf(hand);
            Transform t = visual != null ? visual.transform : hand.transform;
            h.Position = t.position; h.Rotation = t.rotation;
            InteractionHandController interaction = hand.InteractionHandController;
            bool grabbing = interaction != null && interaction.IsGrabInputButton();
            // The debug controller (lab rig) has no analog axes; only real controllers report a raw trigger.
            float trigger = 0f;
            if (hand.TrackedController != null && !hand.DebugModeEnabled)
            {
                try { trigger = hand.TrackedController.GetButtonRaw(TrackedController.ControllerButton.Trigger); }
                catch (NotImplementedException) { }   // a controller type without an analog trigger
            }
            h.Trigger = trigger;
            h.Grip = grabbing ? 1f : Mathf.Clamp01(trigger);
            h.Held = interaction != null && interaction.CurrGrabbedItem != null ? ItemIdOf(interaction.CurrGrabbedItem) : 0u;
            return h;
        }

        bool CaptureLocalPose(out PlayerPose pose)
        {
            pose = new PlayerPose();
            MasterHMDAndInputController m = Master;
            if (m == null || m.HMDCamera == null) return false;
            Transform head = m.HMDCamera.transform;
            pose.Scene = SceneManager.GetActiveScene().name;
            pose.HeadPosition = head.position; pose.HeadRotation = head.rotation;
            pose.Left = CaptureHand(LocalHand(Handedness.Left));
            pose.Right = CaptureHand(LocalHand(Handedness.Right));
            CaptureSpectator(ref pose);
            return true;
        }

        // --- Lab rig -----------------------------------------------------------------------------------------------
        // Targets in world space, set by tours. The head sways a little so remote heads visibly move.
        public bool RigActive;
        public Vector3 RigHead = new Vector3(0, 1.4f, 0);
        public float RigYaw;
        readonly Vector3[] rigHand = { new Vector3(-0.2f, 1.1f, 0.3f), new Vector3(0.2f, 1.1f, 0.3f) };
        readonly bool[] rigGrab = new bool[2];
        public float RigHandSpeed = 1.2f;   // m/s toward the target, like a person reaching
        // Tours that teleport: the head and hand targets are in the play area (they move with teleports), not the world.
        public bool RigRelative;

        public void RigSetHand(Handedness side, Vector3 target) { rigHand[side == Handedness.Left ? 0 : 1] = target; }
        public void RigGrab(Handedness side, bool grab) { rigGrab[side == Handedness.Left ? 0 : 1] = grab; }
        public Vector3 RigHandTarget(Handedness side) { return rigHand[side == Handedness.Left ? 0 : 1]; }

        void RigUpdate()
        {
            if (!RigActive) return;
            MasterHMDAndInputController m = Master;
            if (m == null || m.HMDCamera == null || !m.DebugModeEnabled) return;
            float t = Time.time;
            Transform head = m.HMDCamera.transform;
            Transform root = m.transform;
            Vector3 headAt = RigHead + new Vector3(Mathf.Sin(t * 0.7f) * 0.05f, Mathf.Sin(t * 1.3f) * 0.02f, 0);
            head.position = RigRelative ? root.TransformPoint(headAt) : headAt;
            head.rotation = Quaternion.Euler(10f + Mathf.Sin(t * 0.9f) * 5f, RigYaw + Mathf.Sin(t * 0.5f) * 15f, 0);
            foreach (HandController hand in m.Hands)
            {
                if (hand == null) continue;
                int i = hand.Handedness == Handedness.Left ? 0 : 1;
                Transform tracked = hand.TrackedTransform;
                tracked.position = Vector3.MoveTowards(tracked.position, RigRelative ? root.TransformPoint(rigHand[i]) : rigHand[i], RigHandSpeed * Time.deltaTime);
                tracked.rotation = Quaternion.Euler(0, RigYaw, hand.Handedness == Handedness.Left ? 20f : -20f);
                Debug_TrackedInputController input = hand.GetDebugTrackedController();
                if (input != null) input.isGrabbing = rigGrab[i];
            }
        }

        // Where the grab point of a hand is now (what has to touch an item for the game to grab it).
        public Vector3 GrabPoint(Handedness side)
        {
            HandController hand = LocalHand(side);
            return hand != null && hand.InteractionHandController != null ? hand.InteractionHandController.GetGrabPointPos() : Vector3.zero;
        }
    }
}
