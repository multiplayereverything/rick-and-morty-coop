using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RickAndMortyTogether
{
    // The final boss is a state machine (FinalBossController.SetState: entering, waiting for an eye hit, killing the
    // player, waiting for food, constipated, stunned, dead). Its inputs (items thrown at its eye or fed to its vacuum,
    // the sac pull, the Brain) all reach the host's copy, because items, controls and story are shared. So the host's
    // boss decides: each state change it makes goes to the clients (Op.BossState, plus every 2 s), and a client's
    // boss changes state only when the host's did.
    // Decision: in the killing state each copy's boss aims at its own player, so the death beam kills both players
    // (each respawns in their own game), as the boss threatens everyone in the room.
    public sealed partial class TogetherRuntime
    {
        static readonly MethodInfo bossSetState = typeof(FinalBossController).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        static readonly FieldInfo bossStateField = typeof(FinalBossController).GetField("currentState", BindingFlags.Instance | BindingFlags.NonPublic);   // process-scoped (reflection handle)
        public static bool ReplayingBoss;   // call-scoped
        float nextBossSend;
        public int BossStatesSent, BossStatesShown, BossStatesHeld;   // lab checks read these

        public static FinalBossController Boss() { return UnityEngine.Object.FindObjectOfType<FinalBossController>(); }
        public static int BossState(FinalBossController b) { return b != null ? Convert.ToInt32(bossStateField.GetValue(b)) : -1; }

        // Harmony prefix on FinalBossController.SetState. False: a client's boss waits for the host's.
        public bool OnBossState(FinalBossController b, int state)
        {
            if (!Active || Hosting || ReplayingBoss || Mutant("nobosssync")) return true;
            if (BossState(b) != state) BossStatesHeld++;
            return false;
        }

        public void AfterBossState(FinalBossController b)
        {
            if (!Active || !Hosting) return;
            int state = BossState(b);
            BossStatesSent++;
            Broadcast(Wire.Packet(Op.BossState, w => w.Write((byte)state)), -1);
            Log("Boss state -> " + state);
        }

        void BossUpdate(float now)
        {
            if (!Active || !Hosting || now < nextBossSend) return;
            nextBossSend = now + 2f;
            FinalBossController b = Boss();
            if (b == null) return;
            int state = BossState(b);
            Broadcast(Wire.Packet(Op.BossState, w => w.Write((byte)state)), -1);
        }

        void ClientBossState(BinaryReader r)
        {
            int state = r.ReadByte();
            FinalBossController b = Boss();
            if (b == null || BossState(b) == state) return;
            ReplayingBoss = true;
            try { bossSetState.Invoke(b, new object[] { Enum.ToObject(bossStateField.FieldType, state) }); }
            finally { ReplayingBoss = false; }
            BossStatesShown++;
            Log("Boss state <- " + state + " (from the host)");
        }
    }

    [HarmonyPatch(typeof(FinalBossController), "SetState")]
    static class BossStatePatch
    {
        // The state argument is the boss's private FinalBossStates enum, read from __args.
        static bool Prefix(FinalBossController __instance, object[] __args)
        {
            TogetherRuntime rt = TogetherRuntime.Instance;
            return rt == null || rt.OnBossState(__instance, Convert.ToInt32(__args[0]));
        }
        static void Postfix(FinalBossController __instance, bool __runOriginal)
        {
            TogetherRuntime rt = TogetherRuntime.Instance;
            if (rt != null && __runOriginal) rt.AfterBossState(__instance);
        }
    }
}
