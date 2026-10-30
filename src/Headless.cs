using System.Linq;
using System.Reflection;
using OwlchemyVR;
using OwlchemyVR.PlaySpaceCustomizer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VR;

namespace RickAndMortyTogether
{
    // Lab copies run with -vrmode None (no SteamVR, no headset). The game assumes SteamVR: without it the platform
    // query throws inside OwlchemyVRStorage.LoadCore during the Loading scene, and the play-space selector waits
    // forever for a chaperone. This repairs both, switches the rig to the game's own debug head and hands, and
    // moves on to the LivingRoom. It never runs when a VR device is active.
    // Oculus mode (launch option -vrmode Oculus, no SteamVR): the Steam build ships the game's own Oculus path (the
    // Oculus runtime plugin, CoreLoader-Oculus, the Oculus rig) but always picks SteamVR, so the same start-up fails.
    // The mod sets the platform to Oculus and makes the same repairs with the game's Oculus rig and play-space check.
    public sealed partial class TogetherRuntime
    {
        public static bool Headless;   // process-scoped
        public static bool OculusMode; // process-scoped: Unity runs the Oculus runtime (-vrmode Oculus)
        bool headlessRepaired, headlessLoaded;
        float oculusPlaySpaceAt = -1;  // session-scoped: when the game's Oculus play-space check was started

        void InitHeadless()
        {
            Headless = !VRSettings.enabled;
            OculusMode = VRSettings.enabled && VRSettings.loadedDeviceName == "Oculus";
            if (OculusMode)
            {
                BindingFlags st = BindingFlags.Static | BindingFlags.NonPublic;
                typeof(VRPlatform).GetField("currVRPlatformType", st).SetValue(null, VRPlatformTypes.Oculus);
                typeof(VRPlatform).GetField("currVRPlatformHardwareType", st).SetValue(null, VRPlatformHardwareType.OculusRift);
                Log("Oculus mode: the Oculus runtime is running; using the game's Oculus rig");
                return;
            }
            if (!Headless) return;
            SetPlatform();
            if (LabMode)
            {
                QualitySettings.SetQualityLevel(0, true);
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 30;
                Application.runInBackground = true;
            }
            Log("Headless: no VR device, using the debug rig");
        }

        static void SetPlatform()
        {
            BindingFlags s = BindingFlags.Static | BindingFlags.NonPublic;
            typeof(VRPlatform).GetField("currVRPlatformType", s).SetValue(null, VRPlatformTypes.SteamVR);
            typeof(VRPlatform).GetField("currVRPlatformHardwareType", s).SetValue(null, VRPlatformHardwareType.SteamVRCompatible);
        }

        string teleportScene = "";
        float teleportSceneAt;

        void HeadlessUpdate()
        {
            // Teleporting is switched off for a scene load and back on when the new scene's fade-in finishes
            // (FadeInOnStart); without a headset that fade never finishes. Switch it back on once the scene has run 2 s.
            if (Headless && !Mutant("noheadlessteleport"))
            {
                string scene = SceneManager.GetActiveScene().name;
                if (scene != teleportScene) { teleportScene = scene; teleportSceneAt = Time.realtimeSinceStartup; }
                GlobalStorage g = GlobalStorage.no_instantiate_instance;
                if (Time.realtimeSinceStartup - teleportSceneAt > 2f && g != null && g.TeleportationController != null && !g.TeleportationController.CanTeleport
                    && (DeathController.Instance == null || !DeathController.Instance.PlayerIsDead))
                {
                    g.TeleportationController.CanTeleport = true;
                    teleportSceneAt = float.MaxValue;
                    Log("Headless: teleporting on in " + scene + " (the fade-in that would do it never finishes without a headset)");
                }
            }
            if (!Headless && !OculusMode) return;
            if (!PlaySpaceSizeSelector.isComplete)
            {
                PlaySpaceSizeSelector selector = FindObjectOfType<PlaySpaceSizeSelector>();
                // Oculus: the game's own guardian check (its start-up never got to it); the smallest layout if it has not
                // finished in 10 s.
                if (selector != null && OculusMode && oculusPlaySpaceAt < 0)
                {
                    oculusPlaySpaceAt = Time.realtimeSinceStartup;
                    selector.StartCoroutine((System.Collections.IEnumerator)typeof(PlaySpaceSizeSelector).GetMethod("DetermineOculusPlaySpace", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(selector, null));
                    Log("Oculus mode: checking the guardian play space");
                }
                else if (selector != null && (Headless || Time.realtimeSinceStartup - oculusPlaySpaceAt > 10f))
                {
                    selector.StopAllCoroutines();
                    LayoutConfiguration[] layouts = (LayoutConfiguration[])typeof(PlaySpaceSizeSelector).GetField("steamVRLayouts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(selector);
                    Room.defaultLayoutToLoad = layouts.OrderBy(l => l.sizeInMeters.x).ThenBy(l => l.sizeInMeters.y).First();
                    PlaySpaceSizeSelector.isComplete = true;
                    Log("Headless: play space set to " + Room.defaultLayoutToLoad.sizeInMeters);
                }
            }
            OwlchemyVRStorage store = OwlchemyVRStorage.no_instantiate_instance;
            if (!headlessRepaired && store != null && store.MasterHMDAndInputController == null)
            {
                CoreLoader core = (CoreLoader)typeof(OwlchemyVRStorage).GetMethod("LoadCore", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                typeof(OwlchemyVRStorage).GetField("coreLoader", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(store, core);
                store.SetMasterHMDAndInput(core.MasterHMDAndInputController);
                headlessRepaired = true;
                Log((OculusMode ? "Oculus mode" : "Headless") + ": rig created (" + core.MasterHMDAndInputController.GetType().Name + ")");
            }
            MasterHMDAndInputController m = Master;
            if (Headless && m != null && !m.DebugModeEnabled) { m.DebugModeEnabled = true; RigActive = LabMode && !FlatMode; }
            // LoadGameWhenReady's own fade coroutine died on the missing rig; start the game the same way it would.
            if (!headlessLoaded && m != null && PlaySpaceSizeSelector.isComplete && SceneManager.GetActiveScene().name == "Loading")
            {
                headlessLoaded = true;
                SceneManager.LoadScene("LivingRoom");
            }
        }
    }
}
