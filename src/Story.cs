using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using OwlchemyVR;
using OwlchemyVR.Brain;
using OwlchemyVR.SceneManagement;
using OwlchemyVR.Teleportation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // Levels and story, host-authoritative.
    // - Scenes: the living room (the DVD menu) and the garage (which holds every mission) are separate Unity scenes.
    //   Clients load whatever the host has loaded, with the game's own GlobalStorage.FadeAndLoadScene; a client's own
    //   load (say its DVD player) is held back unless it is the host's scene.
    // - Garage areas: the garage loads every mission as an additive sub-scene and switches them on and off with
    //   SceneController.BeginDelayedActivation/Deactivation. The host announces each switch and the set that is on;
    //   clients mirror it. A client standing in an area the host switched off is teleported to the host's zone.
    // - Story: the Brain (the game's cause -> effect story graph) is fed by world events, each one a string key. The
    //   host's Brain decides: every cause key it runs is replayed on the clients; a client's own causes are sent to
    //   the host as requests instead of running. The progression stage is sent as its asset name.
    public sealed partial class TogetherRuntime
    {
        // Causes from one machine's own hardware stay local.
        static readonly string[] LocalCausePrefixes = { "PLAYSPACE_SIZE", "HARDWARE", "PLATFORM" };   // process-scoped (a constant)
        const float CauseEcho = 1f;   // the same cause seen twice this soon is one event seen on two copies
        // True while a cause from the host (or a client request on the host) runs, so the patch lets it through.
        public static bool ReplayingCause;   // call-scoped
        static readonly MethodInfo checkCause = typeof(BrainManager).GetMethod("CheckCauseLookupDictionaryForAction", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);   // process-scoped (reflection handle)
        static readonly MethodInfo checkCauseAmount = typeof(BrainManager).GetMethod("CheckCauseLookupDictionaryForAction", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(float) }, null);   // process-scoped (reflection handle)
        static readonly FieldInfo progressionListField = typeof(ProgressionManager).GetField("progressionList", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        readonly Dictionary<string, float> recentCauses = new Dictionary<string, float>();
        public int CausesSent, CausesReplayed, CausesRequested;   // lab checks read these
        public string LastReplayedCause = "";
        string hostStage = "";
        readonly List<string> hostAreas = new List<string>();
        string hostZone = "";
        bool hostAreasKnown;
        bool followingLoad;
        float followLoadAt;
        public int BlockedLoads;
        float nextAreaSync;
        string pendingLoad = "";
        float pendingLoadAt;

        // The scene the host is in, or is loading: a fade takes seconds, and clients must not be sent back meanwhile.
        public string HostSceneNow()
        {
            string active = SceneManager.GetActiveScene().name;
            if (pendingLoad.Length > 0 && pendingLoad != active && Time.realtimeSinceStartup - pendingLoadAt < 60f) return pendingLoad;
            pendingLoad = "";
            return active;
        }

        void ResetStory()
        {
            recentCauses.Clear(); hostAreas.Clear(); hostAreasKnown = false;
            hostStage = ""; hostZone = ""; followingLoad = false; pendingLoad = "";
        }

        static bool LocalCause(string key) { foreach (string p in LocalCausePrefixes) if (key.StartsWith(p, StringComparison.Ordinal)) return true; return false; }

        static bool KnownCause(string key)
        {
            BrainManager brain = BrainManager.no_instantiate_instance;
            return brain != null && brain.QuickLookupForCauses != null && brain.QuickLookupForCauses.ContainsKey(key);
        }

        // Harmony prefix on BrainManager.CheckCauseLookupDictionaryForAction: false = this copy does not run it.
        public bool OnCause(string key, bool hasAmount, float amount)
        {
            if (!Active || ReplayingCause || LocalCause(key) || Mutant("nocause")) return true;
            if (Hosting)
            {
                if (!KnownCause(key)) return true;
                recentCauses[key] = Time.realtimeSinceStartup;
                CausesSent++;
                Broadcast(CausePacket(key, hasAmount, amount), -1);
                return true;
            }
            // A client's own world event: the host decides whether it happened.
            if (KnownCause(key)) { CausesRequested++; Send(0, CausePacket(key, hasAmount, amount)); }
            return false;
        }

        static byte[] CausePacket(string key, bool hasAmount, float amount)
        {
            return Wire.Packet(Op.Cause, w => { Wire.Text(w, key); w.Write(hasAmount); w.Write(amount); });
        }

        void HostCause(int from, BinaryReader r)
        {
            string key = Wire.Text(r, 256); bool hasAmount = r.ReadBoolean(); float amount = Wire.Number(r);
            if (LocalCause(key)) return;
            CausesFromClients++;
            float seen;
            // The host's own copy already saw it (a replayed button press fires the same event on both copies).
            if (!hasAmount && recentCauses.TryGetValue(key, out seen) && Time.realtimeSinceStartup - seen < CauseEcho) return;
            BrainManager brain = BrainManager.no_instantiate_instance;
            if (brain == null) return;
            // Runs through the patch, which broadcasts it to everyone (the requester too: its own copy held it back).
            if (hasAmount) checkCauseAmount.Invoke(brain, new object[] { key, amount });
            else checkCause.Invoke(brain, new object[] { key });
        }

        void ClientCause(BinaryReader r)
        {
            string key = Wire.Text(r, 256); bool hasAmount = r.ReadBoolean(); float amount = Wire.Number(r);
            BrainManager brain = BrainManager.no_instantiate_instance;
            if (brain == null) return;
            ReplayingCause = true;
            try
            {
                if (hasAmount) checkCauseAmount.Invoke(brain, new object[] { key, amount });
                else checkCause.Invoke(brain, new object[] { key });
            }
            finally { ReplayingCause = false; }
            CausesReplayed++;
            LastReplayedCause = key;
        }

        // --- Progression stage ---

        public void OnStageSet(ProgressionStageData stage)
        {
            if (!Active || !Hosting || stage == null) return;
            hostStage = stage.name;
            Broadcast(Wire.Packet(Op.Stage, w => Wire.Text(w, stage.name)), -1);
            Log("Stage -> " + stage.name);
        }

        static ProgressionStageData StageNamed(string name, out int index)
        {
            index = -1;
            ProgressionManager pm = ProgressionManager.no_instantiate_instance;
            if (pm == null) return null;
            ProgressionListData list = progressionListField.GetValue(pm) as ProgressionListData;
            if (list == null) return null;
            for (int i = 0; i < list.ProgressionStages.Count; i++) if (list.ProgressionStages[i] != null && list.ProgressionStages[i].name == name) { index = i; return list.ProgressionStages[i]; }
            return null;
        }

        public static string CurrentStageName()
        {
            ProgressionManager pm = ProgressionManager.no_instantiate_instance;
            if (pm == null) return "";
            ProgressionListData list = progressionListField.GetValue(pm) as ProgressionListData;
            int i = pm.CurrentProgressionIndex;
            return list != null && i >= 0 && i < list.ProgressionStages.Count && list.ProgressionStages[i] != null ? list.ProgressionStages[i].name : "";
        }

        void ClientStage(BinaryReader r)
        {
            hostStage = Wire.Text(r, 128);
            ApplyHostStage();
        }

        void ApplyHostStage()
        {
            if (hostStage.Length == 0 || Mutant("nostage")) return;
            int index;
            ProgressionStageData stage = StageNamed(hostStage, out index);
            ProgressionManager pm = ProgressionManager.no_instantiate_instance;
            if (stage == null || pm == null || pm.CurrentProgressionIndex == index) return;
            pm.SetStage(stage);
            Log("Stage <- " + hostStage + " (from the host)");
        }

        // --- Scenes and garage areas ---

        static MultiSceneController Areas()
        {
            GlobalStorage g = GlobalStorage.no_instantiate_instance;
            return g != null ? g.MultiSceneController : null;
        }

        // Garage areas that are on (or switching on) here.
        public static List<string> ActiveAreas()
        {
            List<string> on = new List<string>();
            MultiSceneController m = Areas();
            if (m == null || m.SceneNames == null) return on;
            foreach (string name in m.SceneNames)
            {
                SceneController c = m.GetSceneController(name);
                if (c != null && (c.IsDelayedActivationInProgress || (c.IsActivated && !c.IsDelayedDeactivationInProgress))) on.Add(name);
            }
            return on;
        }

        public void OnAreaSwitch(SceneController area, bool on)
        {
            if (!Active || !Hosting || area == null) return;
            string name = area.SceneName;
            Broadcast(Wire.Packet(Op.Area, w => { Wire.Text(w, name); w.Write(on); }), -1);
            Log("Area " + name + (on ? " on" : " off"));
        }

        void BroadcastAreas()
        {
            List<string> on = ActiveAreas();
            TeleportationController tc = GlobalStorage.no_instantiate_instance != null ? GlobalStorage.no_instantiate_instance.TeleportationController : null;
            string zone = tc != null && tc.CurrentZone != null && tc.CurrentZone.UniqueObjectData != null ? tc.CurrentZone.UniqueObjectData.name : "";
            string stage = CurrentStageName();
            Broadcast(Wire.Packet(Op.Areas, w =>
            {
                Wire.Text(w, HostSceneNow()); Wire.Text(w, stage); Wire.Text(w, zone);
                w.Write((byte)Mathf.Min(on.Count, 64));
                for (int i = 0; i < on.Count && i < 64; i++) Wire.Text(w, on[i]);
            }), -1);
        }

        void ClientArea(BinaryReader r)
        {
            string name = Wire.Text(r, 128); bool on = r.ReadBoolean();
            if (on) { if (!hostAreas.Contains(name)) hostAreas.Add(name); } else hostAreas.Remove(name);
            SwitchArea(name, on);
        }

        void ClientAreas(BinaryReader r)
        {
            HostScene = Wire.Text(r, 64);
            string stage = Wire.Text(r, 128);
            hostZone = Wire.Text(r, 128);
            int count = r.ReadByte();
            hostAreas.Clear();
            for (int i = 0; i < count; i++) hostAreas.Add(Wire.Text(r, 128));
            hostAreasKnown = true;
            if (stage.Length > 0 && stage != hostStage) { hostStage = stage; ApplyHostStage(); }
            ReconcileAreas();
        }

        void SwitchArea(string name, bool on)
        {
            if (Mutant("noarea") || SceneManager.GetActiveScene().name != HostScene) return;
            MultiSceneController m = Areas();
            SceneController c = m != null ? m.GetSceneController(name) : null;
            if (c == null) return;
            if (on && !c.IsDelayedActivationInProgress && !(c.IsActivated && !c.IsDelayedDeactivationInProgress)) { c.BeginDelayedActivation(); Log("Area " + name + " on (host)"); }
            if (!on && !c.IsDelayedDeactivationInProgress && (c.IsActivated || c.IsDelayedActivationInProgress)) { c.BeginDelayedDeactivation(); Log("Area " + name + " off (host)"); }
        }

        void ReconcileAreas()
        {
            if (!hostAreasKnown || SceneManager.GetActiveScene().name != HostScene) return;
            List<string> mine = ActiveAreas();
            foreach (string name in hostAreas) if (!mine.Contains(name)) SwitchArea(name, true);
            foreach (string name in mine) if (!hostAreas.Contains(name)) SwitchArea(name, false);
            FollowHostZone();
        }

        // A client whose zone is in an area the host has switched off joins the host where the host stands.
        void FollowHostZone()
        {
            GlobalStorage g = GlobalStorage.no_instantiate_instance;
            TeleportationController tc = g != null ? g.TeleportationController : null;
            if (tc == null || tc.IsTeleporting || hostZone.Length == 0) return;
            TeleportZone here = tc.CurrentZone;
            if (here != null && hostAreas.Contains(here.gameObject.scene.name)) return;
            if (here != null && here.UniqueObjectData != null && here.UniqueObjectData.name == hostZone) return;
            foreach (TeleportZone zone in UnityEngine.Object.FindObjectsOfType<TeleportZone>())
            {
                if (zone.UniqueObjectData == null || zone.UniqueObjectData.name != hostZone) continue;
                tc.Teleport(zone, false, true, true, true);
                Log("Teleported to the host's zone " + hostZone);
                return;
            }
        }

        // Harmony prefix on GlobalStorage.FadeAndLoadScene: false = held back (or loaded here without the fade).
        public bool OnLoadScene(string scene)
        {
            if (Active && Hosting)
            {
                // Tell everyone as the fade starts, so their loads run alongside the host's and the story timers
                // in the new scene start together.
                pendingLoad = scene; pendingLoadAt = Time.realtimeSinceStartup;
                Broadcast(Wire.Packet(Op.Scene, w => Wire.Text(w, scene)), -1);
                Log("Loading " + scene + " for everyone");
            }
            else if (Active && !(followingLoad || scene == HostScene || Mutant("noscenefollow")))
            {
                BlockedLoads++;
                Log("Held back loading " + scene + ": the host is in " + HostScene);
                return false;
            }
            // Without a headset the game's fader never finishes (its SteamVR overlay is missing): load straight away.
            if (Headless)
            {
                GameUtils.CleanupAllSystemInPreparationForSceneChange();
                SceneManager.LoadScene(scene);
                return false;
            }
            return true;
        }

        void StoryUpdate(float now)
        {
            if (!Active) return;
            if (Hosting)
            {
                if (now >= nextAreaSync) { nextAreaSync = now + 3f; BroadcastAreas(); }
                return;
            }
            string here = SceneManager.GetActiveScene().name;
            if (followingLoad && here == HostScene) { followingLoad = false; Log("Followed the host into " + here); }
            if (HostScene.Length == 0 || here == HostScene || here == "Loading" || Mutant("noscenefollow")) return;
            GlobalStorage g = GlobalStorage.no_instantiate_instance;
            if (!followingLoad && g != null)
            {
                followingLoad = true; followLoadAt = now;
                Log("Following the host into " + HostScene);
                g.FadeAndLoadScene(HostScene, 1f);
            }
            // Without a headset the game's fader can stall the load; load the plain way after 15 s.
            else if (followingLoad && now - followLoadAt > 15f) { followLoadAt = now; Log("Fade load stalled; loading " + HostScene + " directly"); SceneManager.LoadScene(HostScene); }
            if (now >= nextAreaSync) { nextAreaSync = now + 3f; ReconcileAreas(); }
        }
    }

    [HarmonyPatch(typeof(BrainManager), "CheckCauseLookupDictionaryForAction", new[] { typeof(string) })]
    static class CausePatch
    {
        static bool Prefix(string action) { TogetherRuntime rt = TogetherRuntime.Instance; return rt == null || rt.OnCause(action, false, 0); }
    }

    [HarmonyPatch(typeof(BrainManager), "CheckCauseLookupDictionaryForAction", new[] { typeof(string), typeof(float) })]
    static class CauseAmountPatch
    {
        static bool Prefix(string action, float amount) { TogetherRuntime rt = TogetherRuntime.Instance; return rt == null || rt.OnCause(action, true, amount); }
    }

    [HarmonyPatch(typeof(ProgressionManager), "SetStage")]
    static class StagePatch
    {
        static void Postfix(ProgressionStageData stage) { TogetherRuntime rt = TogetherRuntime.Instance; if (rt != null) rt.OnStageSet(stage); }
    }

    [HarmonyPatch(typeof(SceneController), "BeginDelayedActivation")]
    static class AreaOnPatch
    {
        static void Postfix(SceneController __instance) { TogetherRuntime rt = TogetherRuntime.Instance; if (rt != null) rt.OnAreaSwitch(__instance, true); }
    }

    [HarmonyPatch(typeof(SceneController), "BeginDelayedDeactivation")]
    static class AreaOffPatch
    {
        static void Postfix(SceneController __instance) { TogetherRuntime rt = TogetherRuntime.Instance; if (rt != null) rt.OnAreaSwitch(__instance, false); }
    }

    [HarmonyPatch(typeof(GlobalStorage), "FadeAndLoadScene")]
    static class LoadScenePatch
    {
        static bool Prefix(string sceneName) { TogetherRuntime rt = TogetherRuntime.Instance; return rt == null || rt.OnLoadScene(sceneName); }
    }
}
