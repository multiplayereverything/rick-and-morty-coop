using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OwlchemyVR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // A grabbable world item with a rigidbody, known by the same id on every copy of the game.
    public sealed class NetItem
    {
        public uint Id;
        public string Key;
        public GrabbableItem Grab;
        public Rigidbody Body;
        public int Owner;              // peer id that simulates it (0 = host)
        public ushort Epoch;           // bumped by the host on every ownership change; older states are dropped
        public bool OriginalKinematic;
        public bool Following;         // a kinematic copy following the owner's stream
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public bool HasTarget;
        public float LastSend, RestSince = -1, LastMoved;
        public bool SentRest;
        // Levers, doors, drawers and sliders: synced as their position along the axis plus lock state, not a pose.
        public LimitedIndirectGrabbableController Control;
        public float TargetAmount, LastAmount = -1;
        public byte TargetLock, LastLock;
        public bool HasControlTarget, QuietControl;
        public bool Spawned;           // made by the game while playing (Spawns.cs), id given by the host
    }

    // Host-authoritative item ownership. The host starts as owner of every item. Grabbing an item claims it; the
    // host grants every claim in arrival order (last grab wins, which is how a hand-off between players works),
    // bumps the epoch and tells everyone. The previous holder's hand lets go. The owner runs the physics and streams
    // the pose at 20 Hz while the item is held or moving; everyone else holds a kinematic copy that follows.
    public sealed partial class TogetherRuntime
    {
        readonly Dictionary<uint, NetItem> items = new Dictionary<uint, NetItem>();
        readonly Dictionary<GrabbableItem, NetItem> itemOf = new Dictionary<GrabbableItem, NetItem>();
        string itemScene = "";
        float nextItemScan;
        readonly HashSet<InteractionHandController> hooked = new HashSet<InteractionHandController>();

        void ResetItems()
        {
            foreach (NetItem item in items.Values) StopFollowing(item);
            items.Clear(); itemOf.Clear(); passedOver.Clear(); Started.Clear(); itemScene = ""; nextItemScan = 0;
            triggers.Clear(); triggerOf.Clear(); FollowingControls.Clear(); ResetSpawns();
        }

        public NetItem ItemById(uint id) { NetItem item; return items.TryGetValue(id, out item) ? item : null; }
        public IEnumerable<NetItem> Items { get { return items.Values; } }

        public uint ItemIdOf(GrabbableItem grab) { NetItem item; return grab != null && itemOf.TryGetValue(grab, out item) ? item.Id : 0u; }

        // Scene name plus the hierarchy path with sibling indices: identical on every copy of the same scene.
        static string KeyOf(Transform t)
        {
            StringBuilder path = new StringBuilder();
            for (Transform x = t; x != null; x = x.parent) path.Insert(0, "/" + x.name + "#" + x.GetSiblingIndex());
            return t.gameObject.scene.name + path;
        }

        public static uint Hash(string key)
        {
            uint h = 2166136261;
            foreach (char c in key) { h ^= c; h *= 16777619; }
            return h == 0 ? 1u : h;
        }

        // Grabbables the game just started (Harmony postfix on GrabbableItem.Start), looked at on the next frame: new
        // things show up at once without searching the whole scene (the garage holds some 21,000 objects).
        public static readonly List<GrabbableItem> Started = new List<GrabbableItem>();   // process-scoped (drained every frame)
        // Grabbables that are not synced (no body, someone's own, a duplicate path): not looked at again.
        readonly HashSet<GrabbableItem> passedOver = new HashSet<GrabbableItem>();

        void ScanItems() { ScanItems(UnityEngine.Object.FindObjectsOfType<GrabbableItem>(), true); }

        void ScanItems(IList<GrabbableItem> found, bool full)
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene != itemScene) { foreach (NetItem old in items.Values) StopFollowing(old); items.Clear(); itemOf.Clear(); passedOver.Clear(); triggers.Clear(); triggerOf.Clear(); FollowingControls.Clear(); ResetSpawns(); itemScene = scene; }
            int added = 0;
            foreach (GrabbableItem grab in found)
            {
                if (grab == null || itemOf.ContainsKey(grab) || passedOver.Contains(grab)) continue;
                Rigidbody body = grab.GetComponent<Rigidbody>();
                LimitedIndirectGrabbableController control = grab.GetComponent<LimitedIndirectGrabbableController>();
                if (body == null && control == null) { passedOver.Add(grab); continue; }
                // Each player's own spectator camera is theirs (it travels in their pose, see Spectator.cs).
                if (grab.GetComponentInParent<SpectatorCameraController>() != null) { passedOver.Add(grab); continue; }
                if (IsSpawned(grab.transform)) { FoundSpawned(grab, body, control); continue; }
                string key = KeyOf(grab.transform);
                if (control != null && PerPlayerControl(key)) { passedOver.Add(grab); continue; }
                uint id = Hash(key);
                if (items.ContainsKey(id)) { passedOver.Add(grab); continue; }   // a duplicate path (an unnamed runtime clone) stays local
                NetItem item = new NetItem { Id = id, Key = key, Grab = grab, Body = body, Control = control, Owner = 0, OriginalKinematic = body != null && body.isKinematic };
                items[id] = item; itemOf[grab] = item;
                added++;
                // On a client every item starts as a follower of the host's copy, so local bumps cannot drift it.
                if (!Hosting && !Mutant("nofollow")) StartFollowing(item);
            }
            if (added > 0) Log("Items: " + added + " new in " + scene + " (" + items.Count + " synced)");
            if (!full) return;
            ScanTriggers();
            HookHands();
        }

        void HookHands()
        {
            MasterHMDAndInputController m = Master;
            if (m == null) return;
            foreach (HandController hand in m.Hands)
            {
                InteractionHandController ih = hand != null ? hand.InteractionHandController : null;
                if (ih == null || hooked.Contains(ih)) continue;
                hooked.Add(ih);
                ih.OnGrabSuccess += LocalGrabbed;
                ih.OnReleasedGrabbable += LocalReleased;
            }
        }

        void StartFollowing(NetItem item)
        {
            if (item.Following) return;
            if (item.Body == null && item.Control == null) return;
            item.Following = true;
            if (item.Control != null) FollowingControls.Add(item.Control);
            if (item.Body != null) item.Body.isKinematic = true;
        }

        // grabbedHere: a hand on this copy just took it, so the game's own grab handler has already set up its body.
        void StopFollowing(NetItem item, bool grabbedHere = false)
        {
            if (item.Control != null) FollowingControls.Remove(item.Control);
            item.HasControlTarget = false;
            if (!item.Following || item.Body == null) { item.Following = false; return; }
            item.Following = false;
            item.HasTarget = false;
            // A lever or door grabbed here was just made non-kinematic by the game's own grab handler (some doors are
            // kinematic whenever no hand holds them): keep it so.
            item.Body.isKinematic = item.OriginalKinematic && !(item.Control != null && (grabbedHere || (item.Grab != null && item.Grab.IsCurrInHand)));
        }

        // A local hand grabbed something: claim it.
        void LocalGrabbed(InteractionHandController hand, GrabbableItem grab)
        {
            if (!Active) return;
            NetItem item;
            if (!itemOf.TryGetValue(grab, out item)) return;
            if (Hosting) { GrantItem(item, 0); return; }
            // Optimistic: simulate it at once so the hand carries it; the host's answer confirms or hands it on.
            item.Owner = LocalId;
            StopFollowing(item, true);
            item.SentRest = false;
            if (!Mutant("noclaim")) Send(0, Wire.Packet(Op.ItemClaim, w => { w.Write(item.Id); w.Write(item.Epoch); }));
            Log("Claimed " + grab.name);
        }

        void LocalReleased(InteractionHandController hand, GrabbableItem grab)
        {
            NetItem item;
            if (!Active || grab == null || !itemOf.TryGetValue(grab, out item)) return;
            item.RestSince = -1; item.SentRest = false; item.LastMoved = Time.realtimeSinceStartup;
        }

        void HostItemClaim(int from, BinaryReader r)
        {
            uint id = r.ReadUInt32(); r.ReadUInt16();
            NetItem item;
            if (!items.TryGetValue(id, out item)) return;   // not in the host's scene: the claim is moot
            GrantItem(item, from);
        }

        void GrantItem(NetItem item, int owner)
        {
            item.Epoch++;
            item.Owner = owner;
            item.SentRest = false; item.RestSince = -1;
            if (owner == LocalId) StopFollowing(item);
            else
            {
                // Passed to someone else: this copy's hand lets go and the item follows its new owner.
                ReleaseLocalHold(item);
                StartFollowing(item);
            }
            byte[] packet = Wire.Packet(Op.ItemOwner, w => { w.Write(item.Id); w.Write(item.Epoch); w.Write((byte)owner); });
            Broadcast(packet, -1);
            Log("Item " + item.Grab.name + " owner -> " + owner + " (epoch " + item.Epoch + ")");
        }

        void ReleaseLocalHold(NetItem item)
        {
            if (Mutant("nohandoff")) return;
            if (item.Grab != null && item.Grab.IsCurrInHand) { item.Grab.CurrInteractableHand.TryRelease(false); Log("Let go of " + item.Grab.name + ": another player took it"); }
        }

        void ClientItemOwner(BinaryReader r)
        {
            uint id = r.ReadUInt32(); ushort epoch = r.ReadUInt16(); int owner = r.ReadByte();
            NetItem item;
            if (!items.TryGetValue(id, out item)) return;
            item.Epoch = epoch; item.Owner = owner;
            if (owner == LocalId) StopFollowing(item);
            else { ReleaseLocalHold(item); StartFollowing(item); }
        }

        void SendItemSnapshot(int to)
        {
            // Things the game made while playing first, so the client knows them before their owners and states.
            foreach (NetItem item in items.Values) if (item.Spawned && item.Grab != null) Send(to, SpawnPacket(item));
            foreach (NetItem item in items.Values)
            {
                NetItem it = item;
                if (it.Grab == null) continue;
                Send(to, Wire.Packet(Op.ItemOwner, w => { w.Write(it.Id); w.Write(it.Epoch); w.Write((byte)it.Owner); }));
                if (it.Owner == 0 && (it.Body != null || it.Control != null)) Send(to, StatePacket(it, true));
            }
        }

        // A player who left gives their items back to the host, which simulates them from where they are.
        void ReleaseItemsOf(int peer)
        {
            foreach (NetItem item in items.Values) if (item.Owner == peer) GrantItem(item, 0);
        }

        byte[] StatePacket(NetItem item, bool rest)
        {
            if (item.Control != null) return ControlStatePacket(item, rest);
            Transform t = item.Body.transform;
            Vector3 velocity = item.Body.isKinematic ? Vector3.zero : item.Body.velocity;
            return Wire.Packet(Op.ItemState, w => { w.Write(item.Id); w.Write(item.Epoch); w.Write(rest); Wire.Vec(w, t.position); Wire.Rot(w, t.rotation); Wire.Vec(w, velocity); });
        }

        void HostItemState(int from, BinaryReader r) { HostState(from, r, Op.ItemState); }

        // Owner and epoch checked, applied here, relayed to everyone else unchanged.
        void HostState(int from, BinaryReader r, Op op)
        {
            long start = r.BaseStream.Position;
            uint id = r.ReadUInt32(); ushort epoch = r.ReadUInt16();
            NetItem item;
            if (!items.TryGetValue(id, out item) || item.Owner != from || item.Epoch != epoch) return;   // stale or not theirs
            r.BaseStream.Position = start;
            if (op == Op.ControlState) ApplyControlState(item, r); else ApplyState(item, r);
            byte[] relay = Wire.Packet(op, w => { r.BaseStream.Position = start; w.Write(r.ReadBytes((int)(r.BaseStream.Length - start))); });
            Broadcast(relay, from);
        }

        void ClientItemState(BinaryReader r) { ClientState(r, Op.ItemState); }

        void ClientState(BinaryReader r, Op op)
        {
            long start = r.BaseStream.Position;
            uint id = r.ReadUInt32(); ushort epoch = r.ReadUInt16();
            NetItem item;
            if (!items.TryGetValue(id, out item) || item.Owner == LocalId || epoch < item.Epoch) return;
            r.BaseStream.Position = start;
            if (op == Op.ControlState) ApplyControlState(item, r); else ApplyState(item, r);
        }

        void ApplyState(NetItem item, BinaryReader r)
        {
            r.ReadUInt32(); r.ReadUInt16();
            bool rest = r.ReadBoolean();
            Vector3 position = Wire.Vec(r); Quaternion rotation = Wire.Rot(r); Wire.Vec(r);
            if (item.Owner == LocalId || item.Body == null) return;
            if (!Mutant("nofollow")) StartFollowing(item);
            item.TargetPosition = position; item.TargetRotation = rotation; item.HasTarget = true;
            if (rest) { item.Body.transform.position = position; item.Body.transform.rotation = rotation; }
        }

        void ItemsUpdate()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= nextItemScan) { nextItemScan = now + 3f; Started.Clear(); ScanItems(); }
            else if (Started.Count > 0) { GrabbableItem[] fresh = Started.ToArray(); Started.Clear(); ScanItems(fresh, false); }
            SpawnsUpdate(now);
            float k = 1f - Mathf.Exp(-Time.deltaTime * 20f);
            foreach (NetItem item in items.Values)
            {
                if (item.Grab == null) continue;
                if (item.Control != null) { ControlUpdate(item, now, k); continue; }
                if (item.Body == null) continue;
                if (item.Owner != LocalId)
                {
                    if (item.Following && item.HasTarget)
                    {
                        Transform t = item.Body.transform;
                        t.position = Vector3.Lerp(t.position, item.TargetPosition, k);
                        t.rotation = Quaternion.Slerp(t.rotation, item.TargetRotation, k);
                    }
                    continue;
                }
                // Owner: stream while held or moving, then one rest state.
                bool held = item.Grab.IsCurrInHand;
                bool moving = !item.Body.isKinematic && !item.Body.IsSleeping() && item.Body.velocity.sqrMagnitude > 0.0004f;
                if (held || moving) { item.LastMoved = now; item.SentRest = false; }
                if (held || moving || now - item.LastMoved < 1f)
                {
                    if (now - item.LastSend >= 0.05f) { item.LastSend = now; Broadcast(StatePacket(item, false), -1); }
                }
                else if (!item.SentRest && item.LastMoved > 0)
                {
                    item.SentRest = true; item.LastSend = now;
                    Broadcast(StatePacket(item, true), -1);
                }
            }
        }
    }
}
