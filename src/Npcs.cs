using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using OwlchemyVR;
using OwlchemyVR.Brain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // NPC dialogue and animation. Rick, Troy and the rest are driven by Brain effects (PlayVO, PlayAnimation...), and
    // every copy runs the same effects because the host's story causes are replayed on every copy (Story.cs). What
    // still differs is the game's own dice: a node that plays "one random effect" (which line Rick says), the random
    // interrupt line, and random waits. Each of those rolls is seeded from what is being rolled and how many times it
    // has been rolled since the scene loaded, so every copy picks the same line at the same point in the story.
    public sealed partial class TogetherRuntime
    {
        // Rolls per (brain, node) since this scene loaded (cleared on scene change).
        public static readonly Dictionary<string, int> Rolls = new Dictionary<string, int>();   // process-scoped (cleared with the scene)
        static string rollScene = "";   // process-scoped (pairs with Rolls)

        public static int RollSeed(string key)
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene != rollScene) { Rolls.Clear(); rollScene = scene; }
            int n;
            Rolls.TryGetValue(key, out n);
            Rolls[key] = n + 1;
            return (int)Hash(key + "#" + n);
        }

        // Characters looking at "the player" (BrainControlledModule_CharacterHeadLook.LookAtPlayer aims their AimSystem at
        // this copy's own head) look at whichever player is nearest them: this one, or another's drawn head.
        static readonly FieldInfo headLookAim = typeof(BrainControlledModule_CharacterHeadLook).GetField("lookAimSystem", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        BrainControlledModule_CharacterHeadLook[] lookers = new BrainControlledModule_CharacterHeadLook[0];
        float nextLook, nextLookFind;
        public int LookRetargets;   // lab checks read it

        public static AimSystem LookAim(BrainControlledModule_CharacterHeadLook m) { return headLookAim.GetValue(m) as AimSystem; }

        void LookUpdate(float now)
        {
            if (!Active || now < nextLook || Master == null || Master.Head == null || Mutant("nolooknearest")) return;
            nextLook = now + 0.5f;
            if (now >= nextLookFind) { nextLookFind = now + 3f; lookers = Object.FindObjectsOfType<BrainControlledModule_CharacterHeadLook>(); }
            Transform own = Master.Head.transform;
            foreach (BrainControlledModule_CharacterHeadLook m in lookers)
            {
                AimSystem aim = m != null ? LookAim(m) : null;
                if (aim == null || aim.Target == null) continue;
                bool atPlayer = aim.Target == own;
                foreach (Peer p in peers.Values) if (p.Avatar != null && aim.Target == p.Avatar.Head) atPlayer = true;
                if (!atPlayer) continue;
                Transform best = own; float bestDistance = (own.position - aim.transform.position).sqrMagnitude;
                foreach (Peer p in peers.Values)
                {
                    if (p.Avatar == null || !p.Avatar.Root.activeSelf) continue;
                    float d = (p.Avatar.Head.position - aim.transform.position).sqrMagnitude;
                    if (d < bestDistance) { best = p.Avatar.Head; bestDistance = d; }
                }
                if (aim.Target != best) { aim.SetTarget(best); LookRetargets++; }
            }
        }

        // A player who joins a scene the host has been in a while starts that scene's story from its beginning: its
        // opening lines would play for them alone. For the first 15 s of that story here (it starts when the scene's
        // areas have come up, which on a slow machine is a minute after the load), voiced lines complete without playing.
        const float OpeningMute = 15f;
        float hostSceneSince, muteOpeningsUntil = -1, arrivedAt = -1;
        public int MutedOpenings;   // lab checks read it

        // age: how long the host has been in that scene (seconds), or -1 when the message doesn't say.
        public void SetHostScene(string scene, float age)
        {
            if (age >= 0) hostSceneSince = Time.realtimeSinceStartup - age;
            else if (scene != HostScene) hostSceneSince = Time.realtimeSinceStartup;
            HostScene = scene;
        }

        // Host: seconds since its active scene last changed (0 while it is loading another).
        string ageScene = "";
        float ageSince;
        public float HostSceneAge()
        {
            string active = SceneManager.GetActiveScene().name;
            if (active != ageScene) { ageScene = active; ageSince = Time.realtimeSinceStartup; }
            return HostSceneNow() == active ? Time.realtimeSinceStartup - ageSince : 0f;
        }

        // A load into a scene the host has been in a while: lines are held from the start of the load until 15 s after
        // the new scene's first line here, or 2 minutes after arriving if none comes.
        void FollowStarting()
        {
            if (Time.realtimeSinceStartup - hostSceneSince < OpeningMute || Mutant("noopeningmute")) return;
            muteOpeningsUntil = float.MaxValue; arrivedAt = -1;
            Log("Joining " + HostScene + " mid-scene (the host has been there " + (Time.realtimeSinceStartup - hostSceneSince).ToString("0") + " s): its opening lines are skipped");
        }

        void ArrivedInHostScene()
        {
            if (muteOpeningsUntil == float.MaxValue) arrivedAt = Time.realtimeSinceStartup;
        }

        public bool MuteOpening(BrainEffectInstance effect)
        {
            float now = Time.realtimeSinceStartup;
            if (muteOpeningsUntil < now || effect == null || effect.Effect == null) return false;
            string fx = effect.Effect.ClassName + "." + effect.Effect.FunctionToCall;
            if (!(fx.Contains("BrainControlledModule_Voice.") || fx.Contains("WithVoice"))) return false;
            // The new scene's first line here starts the 15 s (or, 2 minutes after arriving with none, the hold ends).
            if (muteOpeningsUntil == float.MaxValue && arrivedAt >= 0) muteOpeningsUntil = now - arrivedAt > 120f ? -1 : now + OpeningMute;
            if (now >= muteOpeningsUntil) return false;
            MutedOpenings++;
            return true;
        }

        // Lab: every Brain effect that runs here, so the verifier can compare dialogue and animation across copies.
        public void OnEffect(BrainEffectInstance effect)
        {
            if (!LabMode || effect == null || effect.Effect == null) return;
            StringBuilder s = new StringBuilder("RT-FX ht=" + HostTime.ToString("0.000") + " scene=" + SceneManager.GetActiveScene().name + " fx=" + effect.Effect.ClassName + "." + effect.Effect.FunctionToCall);
            object[] args = effect.CachedParameters;
            if (args != null) foreach (object a in args) if (a is Object && (Object)a != null) s.Append(":" + ((Object)a).name); else if (a is string || a is bool || a is int) s.Append(":" + a);
            Log(s.ToString());
        }
    }

    // A seeded roll leaves Unity's shared random state as it was, so nothing else in the game is shifted.
    static class SeededRoll
    {
        public static Random.State Begin(string key)
        {
            Random.State saved = Random.state;
            Random.InitState(TogetherRuntime.RollSeed(key));
            return saved;
        }
    }

    [HarmonyPatch(typeof(BrainNodeStatusController), "Execute")]
    static class NodeRollPatch
    {
        static void Prefix(BrainNodeStatusController __instance, out Random.State __state)
        {
            BrainNode node = __instance.Node;
            string brain = __instance.ParentDataStatus != null && __instance.ParentDataStatus.Data != null ? __instance.ParentDataStatus.Data.name : "";
            __state = SeededRoll.Begin("node:" + brain + ":" + (node != null ? node.ID + ":" + node.Name : ""));
        }
        static void Postfix(Random.State __state) { Random.state = __state; }
    }

    [HarmonyPatch(typeof(BrainControlledModule_Voice), "DoInterruptLogicAndGetInterruptClip")]
    static class InterruptRollPatch
    {
        static void Prefix(BrainControlledModule_Voice __instance, out Random.State __state) { __state = SeededRoll.Begin("interrupt:" + __instance.name); }
        static void Postfix(Random.State __state) { Random.state = __state; }
    }

    [HarmonyPatch(typeof(BrainControlledModule_Core), "WaitForRandomAmountOfTime")]
    static class WaitRollPatch
    {
        static void Prefix(BrainControlledModule_Core __instance, out Random.State __state) { __state = SeededRoll.Begin("wait:" + __instance.name); }
        static void Postfix(Random.State __state) { Random.state = __state; }
    }

    [HarmonyPatch(typeof(BrainManager), "ExecuteEffectWithContext")]
    static class EffectLogPatch
    {
        static readonly System.Reflection.MethodInfo complete = typeof(BrainControlledModule).GetMethod("Complete", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);   // process-scoped (reflection handle)

        static bool Prefix(BrainEffectInstance effect, BrainInstanceStatusController instanceStatus)
        {
            TogetherRuntime rt = TogetherRuntime.Instance;
            if (rt == null) return true;
            if (rt.MuteOpening(effect))
            {
                // Done without being played: the module's own completion, so the story moves on as if it had.
                BrainControlledModule module = instanceStatus.ObjectInstance.RegisteredModuleEvents[effect.Effect.ClassName];
                complete.Invoke(module, new object[] { effect });
                return false;
            }
            rt.OnEffect(effect);
            return true;
        }
    }
}
