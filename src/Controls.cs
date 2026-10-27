using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // A button or pull cord: a one-shot event, replayed on every other copy so the game's own listeners run there too.
    public sealed class NetTrigger
    {
        public uint Id;
        public Component Source;
        public byte Kind;              // 1 = PushableButton pushed, 2 = PullCordController pulled
        public float LastFire = -10;
        public int Fires;              // local fires plus replays (lab checks read it)
        public int Replays;
    }

    // World controls. Levers, doors, drawers and sliders (LimitedIndirectGrabbableController) are items with a
    // different state: the owner streams the position along the axis and the lock state; everyone else drives
    // their copy there with the game's own lock calls, so the game's lock events (and what listens to them) run on
    // every copy. Buttons and pull cords are events: the copy where a hand pushed it tells the host, the host replays
    // it and tells everyone else.
    public sealed partial class TogetherRuntime
    {
        // Controls whose effect belongs to one player (their own height, their spectator camera) stay local.
        static readonly string[] PerPlayerPaths = { "/PlayerHeight_LeverMachine#", "/SpectatorCamera_LeverMachine#" };   // process-scoped (a constant)
        // Buttons whose effect belongs to one player stay local (none so far: the DVD player's Play button is shared,
        // and a client's own scene load waits for the host, see Story.cs).
        static readonly string[] LocalButtonPaths = { };   // process-scoped (a constant)
        // Controls that follow another player's copy; their own updates must not move them (cleared with the items).
        public static readonly HashSet<LimitedIndirectGrabbableController> FollowingControls = new HashSet<LimitedIndirectGrabbableController>();   // process-scoped
        readonly Dictionary<uint, NetTrigger> triggers = new Dictionary<uint, NetTrigger>();
        readonly Dictionary<Component, NetTrigger> triggerOf = new Dictionary<Component, NetTrigger>();
        bool replaying;
        const float TriggerEcho = 0.5f;   // a second fire of the same trigger this soon is the same press

        static bool PerPlayerControl(string key) { foreach (string p in PerPlayerPaths) if (key.Contains(p)) return true; return false; }

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly MethodInfo softLockUpper = typeof(LimitedIndirectGrabbableController).GetMethod("SoftLockUpper", Any);   // process-scoped (reflection handle)
        static readonly MethodInfo softUnlockUpper = typeof(LimitedIndirectGrabbableController).GetMethod("SoftUnlockUpper", Any);   // process-scoped (reflection handle)
        static readonly MethodInfo softLockLower = typeof(LimitedIndirectGrabbableController).GetMethod("SoftLockLower", Any);   // process-scoped (reflection handle)
        static readonly MethodInfo softUnlockLower = typeof(LimitedIndirectGrabbableController).GetMethod("SoftUnlockLower", Any);   // process-scoped (reflection handle)
        static readonly MethodInfo unlockedUpdate = typeof(LimitedIndirectGrabbableController).GetMethod("UnlockedUpdate", Any);   // process-scoped (reflection handle)
        static readonly FieldInfo upperSoftField = typeof(LimitedIndirectGrabbableController).GetField("upperSoftLocked", Any);   // process-scoped (reflection handle)
        static readonly FieldInfo lowerSoftField = typeof(LimitedIndirectGrabbableController).GetField("lowerSoftLocked", Any);   // process-scoped (reflection handle)
        static readonly FieldInfo buttonRestField = typeof(PushableButton).GetField("restingPosition", Any);   // process-scoped (reflection handle)
        static readonly FieldInfo buttonReadyField = typeof(PushableButton).GetField("hasBeenToRestingPosition", Any);   // process-scoped (reflection handle)
        static readonly MethodInfo buttonLockUpper = typeof(PushableButton).GetMethod("LockUpper", Any, null, new[] { typeof(bool) }, null);   // process-scoped (reflection handle)
        static readonly MethodInfo buttonLockLower = typeof(PushableButton).GetMethod("LockLower", Any, null, new[] { typeof(bool) }, null);   // process-scoped (reflection handle)
        static readonly FieldInfo cordPulledField = typeof(PullCordController).GetField("OnPullCordPulled", Any);   // process-scoped (reflection handle)

        // Bits: 1 upper locked, 2 lower locked, 4 upper soft-locked, 8 lower soft-locked.
        static byte LockBits(LimitedIndirectGrabbableController c)
        {
            return (byte)((c.IsUpperLocked ? 1 : 0) | (c.IsLowerLocked ? 2 : 0) | (c.IsUpperSoftLocked ? 4 : 0) | (c.IsLowerSoftLocked ? 8 : 0));
        }

        byte[] ControlStatePacket(NetItem item, bool rest)
        {
            float amount = Mathf.Clamp01(item.Control.NormalizedAxisValue);
            byte locks = LockBits(item.Control);
            return Wire.Packet(Op.ControlState, w => { w.Write(item.Id); w.Write(item.Epoch); w.Write(rest); w.Write(amount); w.Write(locks); });
        }

        void ApplyControlState(NetItem item, BinaryReader r)
        {
            r.ReadUInt32(); r.ReadUInt16();
            bool rest = r.ReadBoolean();
            float amount = Mathf.Clamp01(Wire.Number(r));
            byte locks = r.ReadByte();
            if (item.Owner == LocalId || item.Control == null || Mutant("nocontrol")) return;
            StartFollowing(item);
            // A snapshot for a late joiner sets the state without replaying events that already happened.
            item.QuietControl = rest && !item.HasControlTarget;
            item.TargetAmount = amount; item.TargetLock = locks; item.HasControlTarget = true;
        }

        void ControlUpdate(NetItem item, float now, float k)
        {
            LimitedIndirectGrabbableController c = item.Control;
            if (c == null || !c.isActiveAndEnabled) return;
            if (item.Owner != LocalId)
            {
                if (item.Following && item.HasControlTarget) FollowControl(item, k);
                return;
            }
            // Owner: stream while held or changing, then one rest state.
            bool held = item.Grab.IsCurrInHand;
            float amount = Mathf.Clamp01(c.NormalizedAxisValue);
            byte locks = LockBits(c);
            if (item.LastAmount < 0) { item.LastAmount = amount; item.LastLock = locks; }
            bool changed = Mathf.Abs(amount - item.LastAmount) > 0.002f || locks != item.LastLock;
            if (held || changed) { item.LastMoved = now; item.SentRest = false; }
            if (held || changed || now - item.LastMoved < 1f)
            {
                if (now - item.LastSend >= 0.05f) { item.LastSend = now; item.LastAmount = amount; item.LastLock = locks; Broadcast(ControlStatePacket(item, false), -1); }
            }
            else if (!item.SentRest && item.LastMoved > 0)
            {
                item.SentRest = true; item.LastSend = now;
                Broadcast(ControlStatePacket(item, true), -1);
            }
        }

        // Drives a follower with the game's own calls: lock and unlock fire the same events the owner's copy fired.
        void FollowControl(NetItem item, float k)
        {
            LimitedIndirectGrabbableController c = item.Control;
            byte l = item.TargetLock;
            bool quiet = item.QuietControl;
            item.QuietControl = false;
            // Swinging from one end straight to the other leaves the first end locked: let go of it first.
            if ((l & 1) != 0) { if (c.IsLowerLocked) c.UnlockLower(false, true, !quiet); if (!c.IsUpperLocked) c.LockUpper(quiet, quiet); return; }
            if ((l & 2) != 0) { if (c.IsUpperLocked) c.UnlockUpper(false, true, !quiet); if (!c.IsLowerLocked) c.LockLower(quiet, quiet); return; }
            if (c.IsUpperLocked) c.UnlockUpper(false, false, !quiet);
            if (c.IsLowerLocked) c.UnlockLower(false, false, !quiet);
            float current = Mathf.Clamp01(c.NormalizedAxisValue);
            float next = quiet || Mathf.Abs(item.TargetAmount - current) < 0.002f ? item.TargetAmount : Mathf.Lerp(current, item.TargetAmount, k);
            c.SetNormalizedAmount(next, true);
            SetSoft(c, upperSoftField, softLockUpper, softUnlockUpper, (l & 4) != 0, quiet);
            SetSoft(c, lowerSoftField, softLockLower, softUnlockLower, (l & 8) != 0, quiet);
            // Threshold events such as a hinge being pulled past its activation angle.
            if (!quiet && unlockedUpdate != null) unlockedUpdate.Invoke(c, null);
        }

        static void SetSoft(LimitedIndirectGrabbableController c, FieldInfo field, MethodInfo lockMethod, MethodInfo unlockMethod, bool want, bool quiet)
        {
            bool now = (bool)field.GetValue(c);
            if (now == want) return;
            if (quiet) field.SetValue(c, want);
            else (want ? lockMethod : unlockMethod).Invoke(c, new object[] { true });
        }

        // Buttons and pull cords in the scene, hooked once each.
        void ScanTriggers()
        {
            foreach (PushableButton b in UnityEngine.Object.FindObjectsOfType<PushableButton>())
            {
                if (triggerOf.ContainsKey(b)) continue;
                string key = KeyOf(b.transform);
                bool local = false;
                foreach (string p in LocalButtonPaths) if (key.Contains(p)) local = true;
                if (local) { triggerOf[b] = null; continue; }
                if (AddTrigger(b, key, 1) == null) continue;
                PushableButton button = b;
                b.OnButtonPushed += x => LocalFired(button);
            }
            foreach (PullCordController c in UnityEngine.Object.FindObjectsOfType<PullCordController>())
            {
                if (triggerOf.ContainsKey(c)) continue;
                if (AddTrigger(c, KeyOf(c.transform), 2) == null) continue;
                PullCordController cord = c;
                c.OnPullCordPulled += x => LocalFired(cord);
            }
        }

        NetTrigger AddTrigger(Component source, string key, byte kind)
        {
            uint id = Hash(key);
            if (triggers.ContainsKey(id)) { triggerOf[source] = null; return null; }   // a duplicate path stays local
            NetTrigger t = new NetTrigger { Id = id, Source = source, Kind = kind };
            triggers[id] = t; triggerOf[source] = t;
            return t;
        }

        public NetTrigger TriggerByPath(string path)
        {
            foreach (NetTrigger t in triggers.Values) if (t.Source != null && KeyOf(t.Source.transform).Contains(path)) return t;
            return null;
        }

        // A hand here pushed a button or pulled a cord.
        void LocalFired(Component source)
        {
            NetTrigger t;
            if (!triggerOf.TryGetValue(source, out t) || t == null) return;
            t.Fires++;
            if (replaying) return;
            t.LastFire = Time.realtimeSinceStartup;
            if (!Active || Mutant("nobutton")) return;
            byte[] packet = Wire.Packet(Op.TriggerFired, w => { w.Write(t.Id); w.Write(t.Kind); });
            if (Hosting) Broadcast(packet, -1); else Send(0, packet);
            Log("Fired " + source.name + " (" + (t.Kind == 1 ? "button" : "cord") + ")");
        }

        void HostTriggerFired(int from, BinaryReader r)
        {
            uint id = r.ReadUInt32(); byte kind = r.ReadByte();
            NetTrigger t;
            if (!triggers.TryGetValue(id, out t) || t.Kind != kind) return;   // not in the host's scene
            Replay(t);
            Broadcast(Wire.Packet(Op.TriggerFired, w => { w.Write(id); w.Write(kind); }), from);
        }

        void ClientTriggerFired(BinaryReader r)
        {
            uint id = r.ReadUInt32(); byte kind = r.ReadByte();
            NetTrigger t;
            if (triggers.TryGetValue(id, out t) && t.Kind == kind) Replay(t);
        }

        void Replay(NetTrigger t)
        {
            float now = Time.realtimeSinceStartup;
            // A thrown item that follows here can push the same button the owner's copy pushed: count it once.
            if (now - t.LastFire < TriggerEcho || t.Source == null) return;
            t.LastFire = now;
            t.Replays++;
            replaying = true;
            try
            {
                PushableButton button = t.Source as PushableButton;
                if (button != null)
                {
                    buttonReadyField.SetValue(button, true);
                    bool restsUp = (PushableButton.RestingPositionType)buttonRestField.GetValue(button) == PushableButton.RestingPositionType.UpperLimit;
                    (restsUp ? buttonLockLower : buttonLockUpper).Invoke(button, new object[] { false });
                }
                PullCordController cord = t.Source as PullCordController;
                if (cord != null)
                {
                    Action<PullCordController> pulled = cordPulledField.GetValue(cord) as Action<PullCordController>;
                    if (pulled != null) pulled(cord);
                }
            }
            finally { replaying = false; }
            Log("Replayed " + t.Source.name + " from another player");
        }
    }

    // A follower's lever or door is driven by FollowControl; its own update would fight it or fire lock events twice.
    [HarmonyPatch(typeof(LimitedIndirectGrabbableController), "LateUpdate")]
    static class ControlLateUpdatePatch
    {
        static bool Prefix(LimitedIndirectGrabbableController __instance) { return !TogetherRuntime.FollowingControls.Contains(__instance); }
    }

    [HarmonyPatch(typeof(LimitedIndirectGrabbableController), "FixedUpdate")]
    static class ControlFixedUpdatePatch
    {
        static bool Prefix(LimitedIndirectGrabbableController __instance) { return !TogetherRuntime.FollowingControls.Contains(__instance); }
    }
}
