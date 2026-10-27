using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Spectator mode (the desktop window's camera: on the player's head, or a floating camera at the room's anchor
    // spots). Each player's spectator camera is part of their pose, so a camera that is out in the world floats where it
    // is on everyone's screen. Cycling cameras also visits the other players: after the room's spots comes each other
    // player's view (their head hidden from that camera), then back to your own head.
    public sealed partial class TogetherRuntime
    {
        static readonly FieldInfo spectatorCamField = typeof(SpectatorModeLogic).GetField("spectatorCamController", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        static readonly FieldInfo spectatorPrefabField = typeof(SpectatorModeLogic).GetField("spectatorCamPrefab", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        static readonly FieldInfo anchorIndexField = typeof(SpectatorModeLogic).GetField("anchorIndex", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        // The other player this spectator camera is watching through (-1: none).
        public int SpectatorView = -1;
        public static bool InCycle;   // call-scoped

        public static SpectatorInterfaceController SpectatorUi()
        {
            OwlchemyVRStorage s = OwlchemyVRStorage.no_instantiate_instance;
            return s != null ? s.SpectatorInterfaceController : null;
        }

        static SpectatorCameraController LocalSpectatorCamera(SpectatorInterfaceController ui)
        {
            return ui != null && ui.SpectatorModeLogic != null ? spectatorCamField.GetValue(ui.SpectatorModeLogic) as SpectatorCameraController : null;
        }

        void CaptureSpectator(ref PlayerPose pose)
        {
            SpectatorInterfaceController ui = SpectatorUi();
            if (ui == null || !ui.IsSpectating) return;
            pose.Spectating = true;
            SpectatorCameraController cam = LocalSpectatorCamera(ui);
            if (cam == null || !cam.isActiveAndEnabled || cam.IsInFirstPersonView) return;
            pose.CameraShown = true;
            pose.CameraPosition = cam.transform.position; pose.CameraRotation = cam.transform.rotation;
        }

        // An inert copy of the game's spectator camera (its screen shows nothing: it would mirror this PC's own view).
        public static Transform MakeSpectatorCameraCopy(Transform parent)
        {
            SpectatorInterfaceController ui = SpectatorUi();
            GameObject prefab = ui != null && ui.SpectatorModeLogic != null ? spectatorPrefabField.GetValue(ui.SpectatorModeLogic) as GameObject : null;
            GameObject copy;
            if (prefab != null)
            {
                copy = Avatar.Inert(prefab);
                Shader s = Shader.Find("Standard") ?? Shader.Find("Diffuse");
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = true;
                    if (r.sharedMaterial != null && r.sharedMaterial.mainTexture is RenderTexture) r.material = new Material(s) { color = new Color(0.08f, 0.1f, 0.12f) };
                }
            }
            else
            {
                copy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(copy.GetComponent<Collider>());
                copy.transform.localScale = new Vector3(0.12f, 0.08f, 0.06f);
            }
            copy.name = "SpectatorCamera";
            copy.transform.SetParent(parent, true);
            copy.SetActive(false);
            return copy.transform;
        }

        // Other players this spectator can watch through, in id order.
        List<Peer> Watchable()
        {
            List<Peer> list = new List<Peer>();
            foreach (Peer p in peers.Values) if (p.Accepted && p.Avatar != null && p.Avatar.Root.activeSelf) list.Add(p);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        // Harmony prefix on SpectatorModeLogic.CycleTarget. False: the mod moved the camera itself.
        public bool OnSpectatorCycle(SpectatorModeLogic logic, out int anchorBefore)
        {
            anchorBefore = (int)anchorIndexField.GetValue(logic);
            if (SpectatorView < 0 || Mutant("nospectatorview")) return true;
            List<Peer> list = Watchable();
            int next = list.FindIndex(p => p.Id == SpectatorView) + 1;
            if (next > 0 && next < list.Count) { WatchPlayer(logic, list[next]); return false; }
            // After the last player: back to your own head.
            StopWatching();
            anchorIndexField.SetValue(logic, -1);
            logic.FindAndSetTarget();
            return false;
        }

        // Postfix: the game ran out of room spots and went back to the head; visit the other players first.
        public void AfterSpectatorCycle(SpectatorModeLogic logic, int anchorBefore)
        {
            if (Mutant("nospectatorview") || (int)anchorIndexField.GetValue(logic) != -1) return;
            List<Peer> list = Watchable();
            if (list.Count > 0) WatchPlayer(logic, list[0]);
        }

        void WatchPlayer(SpectatorModeLogic logic, Peer p)
        {
            StopWatching();
            SpectatorCameraController cam = logic.SpectatorCameraController;
            cam.FollowHead();
            GlobalStorage.Instance.ExternalCameraController.SetTarget(p.Avatar.Head, true);
            p.Avatar.SetHeadHidden(true);
            SpectatorView = p.Id;
            Log("Spectator: watching through " + p.Name + "'s eyes");
        }

        public void ForgetSpectatorView() { StopWatching(); }

        void StopWatching()
        {
            foreach (Peer p in peers.Values) if (p.Avatar != null) p.Avatar.SetHeadHidden(false);
            SpectatorView = -1;
        }

        void SpectatorUpdate()
        {
            if (SpectatorView < 0) return;
            SpectatorInterfaceController ui = SpectatorUi();
            Peer p;
            bool ok = ui != null && ui.IsSpectating && peers.TryGetValue(SpectatorView, out p) && p.Avatar != null && p.Avatar.Root.activeSelf;
            if (ok) return;
            // The watched player left or went to another scene, or spectating stopped: back to the game's own camera.
            StopWatching();
            if (ui != null && ui.IsSpectating && ui.SpectatorModeLogic != null) { anchorIndexField.SetValue(ui.SpectatorModeLogic, -1); ui.SpectatorModeLogic.FindAndSetTarget(); }
        }
    }

    [HarmonyPatch(typeof(SpectatorModeLogic), "CycleTarget")]
    static class SpectatorCyclePatch
    {
        static bool Prefix(SpectatorModeLogic __instance, out int __state)
        {
            __state = -1;
            TogetherRuntime rt = TogetherRuntime.Instance;
            if (rt == null) return true;
            TogetherRuntime.InCycle = true;
            bool run = rt.OnSpectatorCycle(__instance, out __state);
            if (!run) TogetherRuntime.InCycle = false;
            return run;
        }
        static void Postfix(SpectatorModeLogic __instance, int __state, bool __runOriginal)
        {
            TogetherRuntime rt = TogetherRuntime.Instance;
            if (rt == null || !__runOriginal) return;
            TogetherRuntime.InCycle = false;
            rt.AfterSpectatorCycle(__instance, __state);
        }
    }

    // The game re-aims the camera on its own (teleports, spectating turned on): stop watching another player then.
    [HarmonyPatch(typeof(SpectatorModeLogic), "FindAndSetTarget")]
    static class SpectatorRetargetPatch
    {
        static void Postfix()
        {
            TogetherRuntime rt = TogetherRuntime.Instance;
            if (rt != null && !TogetherRuntime.InCycle && rt.SpectatorView >= 0) rt.ForgetSpectatorView();
        }
    }
}
