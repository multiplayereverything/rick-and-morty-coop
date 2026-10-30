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

        public void SetHostScene(string scene) { HostScene = scene; }

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
        static void Prefix(BrainEffectInstance effect) { if (TogetherRuntime.Instance != null) TogetherRuntime.Instance.OnEffect(effect); }
    }
}
