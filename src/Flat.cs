using System.Collections.Generic;
using HarmonyLib;
using OwlchemyVR;
using OwlchemyVR.Teleportation;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Flat-screen mode: play without a headset, on keyboard and mouse. Start the game with "-vrmode None" (Steam launch
    // options); the headless repairs (Headless.cs) start the game on the game's own debug hands, and this drives them:
    //   mouse          look                         WASD / Shift   walk / faster      Ctrl   crouch
    //   left click     grab with the right hand     right click    grab with the left hand
//                  (the hand reaches for the grabbable item in the middle of the view, up to 2 m away)
    //   scroll wheel   reach nearer / further       E / Q          right / left trigger (fire, use)
    //   T              teleport to the pad you look at              M      menu button (the DVD tutorial's "press it")
    //   Alt            free the mouse cursor (held)                 F5     the co-op panel (frees the cursor)
    // Lab tours drive it through FlatSim instead of the keyboard (the lab never sends input to the desktop).
    public sealed class FlatInput
    {
        public Vector2 Look;          // degrees this frame (yaw, pitch)
        public Vector3 Move;          // x strafe, z forward, -1..1
        public bool Run, Crouch, GrabLeft, GrabRight, TriggerLeft, TriggerRight, Menu, Teleport;
        public float Reach;           // change of reach this frame, metres
    }

    public sealed partial class TogetherRuntime
    {
        public static bool FlatMode;   // process-scoped
        public FlatInput FlatSim;      // lab: set by tours instead of reading the keyboard
        float flatYaw, flatPitch, flatReach = 0.45f, flatEye = 1.6f;
        Vector3 flatLocal = new Vector3(0, 0, 0);
        bool flatPlaced;
        readonly FlatInput flatNow = new FlatInput();
        // The item each hand is reaching for after a click (left, right), and whether that button was down last frame.
        readonly GrabbableItem[] flatReachFor = new GrabbableItem[2];
        readonly bool[] flatGrabBefore = new bool[2];
        readonly bool[] flatInFront = new bool[2];   // the reach has reached the spot in front of the item
        // Buttons the game's debug hands lack: [hand 0 left, 1 right] x [Trigger/InteractCustom, Menu], this and last frame.
        static readonly bool[,] flatButtons = new bool[2, 2], flatButtonsBefore = new bool[2, 2];   // process-scoped (flat mode only)
        public int FlatTeleports;   // lab checks read it

        const float WalkSpeed = 1.4f, RunSpeed = 3f, MouseDegrees = 2.2f, AreaHalf = 2f;

        void InitFlat(string[] args)
        {
            FlatMode = Headless && (!LabMode || System.Array.IndexOf(args, "-rt-flat") >= 0);
            if (FlatMode) Log("Flat-screen mode: keyboard and mouse (no headset)");
        }

        FlatInput ReadFlatInput()
        {
            if (FlatSim != null) return FlatSim;
            FlatInput i = flatNow;
            bool free = panelOpen || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
            i.Look = free ? Vector2.zero : new Vector2(Input.GetAxisRaw("Mouse X"), -Input.GetAxisRaw("Mouse Y")) * MouseDegrees;
            i.Move = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), 0, (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            i.Run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            i.Crouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            i.GrabRight = !free && Input.GetMouseButton(0);
            i.GrabLeft = !free && Input.GetMouseButton(1);
            i.TriggerRight = Input.GetKey(KeyCode.E);
            i.TriggerLeft = Input.GetKey(KeyCode.Q);
            i.Menu = Input.GetKey(KeyCode.M);
            i.Teleport = Input.GetKeyDown(KeyCode.T);
            i.Reach = Input.GetAxis("Mouse ScrollWheel") * 0.5f;
            return i;
        }

        void FlatUpdate()
        {
            if (!FlatMode) return;
            MasterHMDAndInputController m = Master;
            if (m == null || m.HMDCamera == null || !m.DebugModeEnabled) return;
            FlatInput i = ReadFlatInput();
            float dt = Time.deltaTime;
            // The head moves inside the play area: the rig root is where teleports put it.
            Transform root = m.transform;
            if (!flatPlaced) { flatLocal = Vector3.zero; flatYaw = 0; flatPitch = 10; flatPlaced = true; }
            flatYaw = Mathf.Repeat(flatYaw + i.Look.x, 360f);
            flatPitch = Mathf.Clamp(flatPitch + i.Look.y, -80f, 80f);
            Quaternion yaw = Quaternion.Euler(0, flatYaw, 0);
            Vector3 step = yaw * new Vector3(i.Move.x, 0, i.Move.z);
            if (step.sqrMagnitude > 1) step.Normalize();
            flatLocal += step * (i.Run ? RunSpeed : WalkSpeed) * dt;
            flatLocal.x = Mathf.Clamp(flatLocal.x, -AreaHalf, AreaHalf); flatLocal.z = Mathf.Clamp(flatLocal.z, -AreaHalf, AreaHalf);
            flatEye = Mathf.MoveTowards(flatEye, i.Crouch ? 1.0f : 1.6f, 2f * dt);
            flatReach = Mathf.Clamp(flatReach + i.Reach, 0.25f, 0.9f);
            Quaternion look = root.rotation * Quaternion.Euler(flatPitch, flatYaw, 0);
            Transform head = m.HMDCamera.transform;
            head.position = root.TransformPoint(flatLocal + Vector3.up * flatEye);
            head.rotation = look;
            // Hands a little below and to each side of the eyes, out at the reach distance, turned with the view.
            foreach (HandController hand in m.Hands)
            {
                if (hand == null) continue;
                bool left = hand.Handedness == Handedness.Left;
                Transform tracked = hand.TrackedTransform;
                int h = left ? 0 : 1;
                bool grab = left ? i.GrabLeft : i.GrabRight;
                if (grab && !flatGrabBefore[h]) { flatReachFor[h] = Mutant("noflatreach") ? null : ItemInView(head); flatInFront[h] = false; }
                if (!grab) flatReachFor[h] = null;
                flatGrabBefore[h] = grab;
                Vector3 rest = head.position + look * new Vector3(left ? -0.16f : 0.16f, -0.18f, flatReach);
                GrabbableItem want = flatReachFor[h];
                if (want != null && (want.IsCurrInHand || !want.isActiveAndEnabled)) { flatReachFor[h] = null; want = null; }
                // Clicked on an item: the hand goes to it at arm speed, first to a spot 25 cm in front of it at its height,
                // then straight in, so it doesn't clip a shelf above the item and knock it away. The game grabs only when
                // the button goes down with the item already in the hand, so the press waits for arrival.
                bool arrived = true;
                if (want != null)
                {
                    Vector3 at = ItemCentre(want) + Vector3.up * 0.03f;
                    Vector3 toward = head.position - at; toward.y = 0;
                    Vector3 front = at + (toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector3.zero) * 0.25f;
                    if (!flatInFront[h]) { tracked.position = Vector3.MoveTowards(tracked.position, front, 1.5f * dt); flatInFront[h] = (tracked.position - front).sqrMagnitude < 0.0004f; }
                    else tracked.position = Vector3.MoveTowards(tracked.position, at, 0.6f * dt);
                    arrived = flatInFront[h] && (tracked.position - at).sqrMagnitude < 0.0004f;
                }
                else tracked.position = rest;
                // Level, turned with the view's yaw only: a hand tipped down with the view hits shelves above an item.
                tracked.rotation = root.rotation * Quaternion.Euler(0, flatYaw, left ? 20f : -20f);
                Debug_TrackedInputController input = hand.GetDebugTrackedController();
                if (input != null) input.isGrabbing = grab && arrived;
            }
            for (int h = 0; h < 2; h++) for (int b = 0; b < 2; b++) flatButtonsBefore[h, b] = flatButtons[h, b];
            flatButtons[0, 0] = i.TriggerLeft; flatButtons[1, 0] = i.TriggerRight;
            flatButtons[0, 1] = false; flatButtons[1, 1] = i.Menu;
            if (i.Teleport) FlatTeleport(head);
        }

        static Vector3 ItemCentre(GrabbableItem item)
        {
            Rigidbody body = item.GetComponent<Rigidbody>();
            return body != null ? body.worldCenterOfMass : item.transform.position;
        }

        // The nearest grabbable item the middle of the view points at, within 2 m.
        static GrabbableItem ItemInView(Transform head)
        {
            GrabbableItem best = null; float bestDistance = 2f;
            foreach (RaycastHit hit in Physics.SphereCastAll(head.position, 0.05f, head.forward, 2f, ~0, QueryTriggerInteraction.Collide))
            {
                GrabbableItem item = hit.collider.GetComponentInParent<GrabbableItem>();
                if (item == null || !item.isActiveAndEnabled || item.IsCurrInHand || hit.distance >= bestDistance) continue;
                best = item; bestDistance = hit.distance;
            }
            return best;
        }

        // The teleport pad nearest the middle of the view (within 25 degrees), other than the one you stand on.
        void FlatTeleport(Transform head)
        {
            GlobalStorage g = GlobalStorage.no_instantiate_instance;
            TeleportationController tc = g != null ? g.TeleportationController : null;
            if (tc == null || tc.IsTeleporting) return;
            TeleportZone best = null; float bestAngle = 25f;
            foreach (TeleportZone zone in Object.FindObjectsOfType<TeleportZone>())
            {
                if (zone == tc.CurrentZone) continue;
                float angle = Vector3.Angle(head.forward, zone.transform.position - head.position);
                if (angle < bestAngle) { best = zone; bestAngle = angle; }
            }
            if (best == null) { Log("Flat: no teleport pad in view"); return; }
            tc.Teleport(best);
            flatLocal = Vector3.zero;
            FlatTeleports++;
            Log("Flat: teleport to " + best.name);
        }

        public List<TeleportZone> FlatPads()
        {
            List<TeleportZone> pads = new List<TeleportZone>();
            GlobalStorage g = GlobalStorage.no_instantiate_instance;
            TeleportZone here = g != null && g.TeleportationController != null ? g.TeleportationController.CurrentZone : null;
            foreach (TeleportZone zone in Object.FindObjectsOfType<TeleportZone>()) if (zone != here) pads.Add(zone);
            return pads;
        }

        // Harmony postfix on Debug_TrackedInputController.GetButtonDetailedControllerIsReady: the buttons it lacks.
        public static bool FlatButton(Debug_TrackedInputController controller, TrackedController.ControllerButton button, int state)
        {
            int b = button == TrackedController.ControllerButton.Trigger || button == TrackedController.ControllerButton.InteractCustom ? 0 : button == TrackedController.ControllerButton.Menu ? 1 : -1;
            if (b < 0) return false;
            MasterHMDAndInputController m = Master;
            if (m == null) return false;
            int h = -1;
            foreach (HandController hand in m.Hands) if (hand != null && hand.GetDebugTrackedController() == controller) h = hand.Handedness == Handedness.Left ? 0 : 1;
            if (h < 0) return false;
            bool now = flatButtons[h, b], before = flatButtonsBefore[h, b];
            return state == 0 ? now : state == 1 ? (!now && before) : (now && !before);   // Current, Up, Down
        }
    }

    [HarmonyPatch(typeof(Debug_TrackedInputController), "GetButtonDetailedControllerIsReady")]
    static class FlatButtonPatch
    {
        // The state argument is the game's protected ButtonStates enum (Current, Up, Down), read from __args.
        static void Postfix(Debug_TrackedInputController __instance, TrackedController.ControllerButton controllerButton, object[] __args, ref bool __result)
        {
            if (__result || !TogetherRuntime.FlatMode || TogetherRuntime.Mutant("noflatbuttons")) return;
            __result = TogetherRuntime.FlatButton(__instance, controllerButton, System.Convert.ToInt32(__args[1]));
        }
    }
}
