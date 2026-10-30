using System;
using System.Collections.Generic;
using System.IO;
using OwlchemyVR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    public sealed class Peer
    {
        public int Id;
        public string Name = "";
        public bool Accepted;
        public PlayerPose Pose;
        public bool HasPose;
        public float PoseAt;
        public Avatar Avatar;
        public string SnapshotScene = "";   // the host scene this player last got the item snapshot for
    }

    // The co-op core: one per game, kept across scene loads. Host-authoritative: the host decides item ownership and
    // (later) story; every player owns their own head and hands.
    public sealed partial class TogetherRuntime : MonoBehaviour
    {
        public const string ModVersion = "0.9.0";
        public static TogetherRuntime Instance;   // process-scoped
        public static Action<string> Log = delegate { };   // process-scoped
        public static string Root = "";   // process-scoped

        Transport transport;
        readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        public int LocalId = -1;
        public string LocalName = "Morty";
        public bool Hosting { get { return transport != null && transport.Hosting; } }
        public bool Active { get { return transport != null && LocalId >= 0; } }
        public int AcceptedCount { get { int n = 0; foreach (Peer p in peers.Values) if (p.Accepted) n++; return n; } }
        public string Status = "Not connected";

        // Command line (lab and power users): -rt-host / -rt-join <address> / -rt-port / -rt-name / -rt-steam / -rt-join-steam.
        string autoRole = "";
        string joinAddress = "127.0.0.1";
        int port = 27040;
        int expectPlayers;
        float nextPose, nextPing, nextScene;
        // Host clock estimate on a client (host time = local time + offset), from ping round trips.
        public float HostClockOffset;
        float bestRtt = float.MaxValue;
        public float HostTime { get { return Hosting || LocalId <= 0 ? Time.realtimeSinceStartup : Time.realtimeSinceStartup + HostClockOffset; } }
        public string HostScene = "";
        // Harmony patch classes that failed to apply at load (each one is logged; the lab counts them as mod errors).
        public static readonly List<string> PatchFailures = new List<string>();   // process-scoped

        public static void Create(string root)
        {
            Root = root;
            GameObject go = new GameObject("RickAndMortyTogether");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TogetherRuntime>();
        }

        void Awake()
        {
            Transport.Clock = () => Time.realtimeSinceStartup;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-rt-host") autoRole = "host";
                if (args[i] == "-rt-join" && i + 1 < args.Length) { autoRole = "join"; joinAddress = args[++i]; }
                if (args[i] == "-rt-steam") autoRole = "steamhost";
                if (args[i] == "-rt-join-steam") autoRole = "steamjoin";
                if (args[i] == "-rt-port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
                if (args[i] == "-rt-name" && i + 1 < args.Length) LocalName = args[++i];
                if (args[i] == "-rt-expect" && i + 1 < args.Length) int.TryParse(args[++i], out expectPlayers);
            }
            InitLab(args);
            LoadSettings();
            InitHeadless();
            InitFlat(args);
            Log("Rick and Morty Together " + ModVersion + " (protocol " + Wire.Version + ") ready" + (autoRole.Length > 0 ? ", auto role " + autoRole : ""));
        }

        void Start()
        {
            if (autoRole == "host") StartHost(false);
            else if (autoRole == "join" && labJoinAfter <= 0) StartJoin();   // a lab late joiner joins from LabUpdate
            else if (autoRole == "steamhost") StartSteamHost();
            else if (autoRole == "steamjoin") StartSteamFind();
        }

        public void StartHost(bool steam)
        {
            Stop("restarting");
            transport = new Transport();
            try
            {
                if (steam) transport.HostSteam();
                else transport.Host(port, false);
            }
            catch (Exception ex) { Status = "Could not host: " + ex.Message; Log(Status); transport.Dispose(); transport = null; return; }
            LocalId = 0;
            peers.Clear();
            Status = steam ? "Hosting on Steam" : "Hosting on port " + transport.Port;
            Log(Status);
            Check("net.host.started", true, Status);
        }

        public void StartJoin()
        {
            Stop("restarting");
            transport = new Transport();
            transport.Join(joinAddress, port);
            Status = "Joining " + joinAddress + ":" + port;
            Log(Status);
        }

        public void JoinSteamHost(Steamworks.CSteamID host)
        {
            Stop("restarting");
            transport = new Transport();
            transport.JoinSteam(host);
            Status = "Joining " + host + " over Steam";
            Log(Status);
        }

        public void Stop(string why)
        {
            if (transport == null) return;
            if (Active && !Hosting) { try { transport.SendAndClose(0, Wire.Packet(Op.Leave, null)); } catch (Exception) { } }   // best effort goodbye
            LeaveLobby();
            transport.Dispose();
            transport = null;
            foreach (Peer p in peers.Values) if (p.Avatar != null) p.Avatar.Destroy();
            peers.Clear();
            LocalId = -1;
            HostClockOffset = 0; bestRtt = float.MaxValue;
            ResetItems();
            ResetStory();
            StopWatching();
            Status = "Not connected";
            Log("Network stopped: " + why);
        }

        void OnApplicationQuit() { Stop("quitting"); }

        void Update()
        {
            labWatch.Reset(); labWatch.Start();
            try
            {
                HeadlessUpdate();
                PanelUpdate();
                SteamUpdate();
                PumpNetwork();
                if (Active)
                {
                    float now = Time.realtimeSinceStartup;
                    if (now >= nextPose) { nextPose = now + 0.05f; SendPose(); }
                    if (now >= nextPing) { nextPing = now + 1f; if (!Hosting) Send(0, Wire.Packet(Op.Ping, w => w.Write(now))); }
                    if (Hosting && now >= nextScene) { nextScene = now + 5f; BroadcastScene(); }
                    ItemsUpdate();
                    StoryUpdate(now);
                    EnemiesUpdate(now);
                    BossUpdate(now);
                    LookUpdate(now);
                }
                AvatarsUpdate();
                SpectatorUpdate();
                RigUpdate();
                FlatUpdate();
            }
            catch (Exception ex) { ModError("update", ex); }
            labWatch.Stop();
            labModTicks += labWatch.ElapsedTicks; labFrames++;
            LabUpdate();
        }

        void PumpNetwork()
        {
            if (transport == null) return;
            NetworkEvent e;
            int budget = 512;
            while (budget-- > 0 && transport != null && transport.Poll(out e))
            {
                try { Handle(e); }
                catch (Exception ex) { Log("Bad packet from peer " + e.Peer + ": " + ex.GetType().Name + ": " + ex.Message); if (Hosting && e.Peer > 0) transport.Drop(e.Peer); }
            }
        }

        void Handle(NetworkEvent e)
        {
            if (e.Kind == "log") { Log(e.Message); return; }
            if (e.Kind == "error") { Status = "Connection failed: " + e.Message; Log(Status); Stop("connection failed"); return; }
            if (e.Kind == "connected")
            {
                if (Hosting) { peers[e.Peer] = new Peer { Id = e.Peer }; Log("Peer " + e.Peer + " connected, waiting for hello"); }
                else Send(0, Wire.Packet(Op.Hello, w => { w.Write(Wire.Version); Wire.Text(w, ModVersion); Wire.Text(w, LocalName); }));
                return;
            }
            if (e.Kind == "disconnected")
            {
                Log("Peer " + e.Peer + " disconnected" + (e.Message != null ? ": " + e.Message : ""));
                if (Hosting) { RemovePeer(e.Peer); BroadcastRoster(); }
                else { Status = "Disconnected from host" + (e.Message != null ? " (" + e.Message + ")" : ""); Stop("host link ended"); }
                return;
            }
            using (MemoryStream memory = new MemoryStream(e.Data))
            using (BinaryReader reader = new BinaryReader(memory))
            {
                Op op = (Op)reader.ReadByte();
                if (Hosting) HostPacket(e.Peer, op, reader);
                else ClientPacket(op, reader);
            }
        }

        // Sender rule: the host takes only these ops from clients; anything else is a protocol error.
        public int JoinsRefusedMidGame;   // session-scoped: lab check (joins refused because the story is under way)

        void HostPacket(int from, Op op, BinaryReader r)
        {
            Peer peer;
            if (!peers.TryGetValue(from, out peer)) return;
            if (op == Op.Hello)
            {
                int version = r.ReadInt32();
                string modVersion = Wire.Text(r, 32);
                peer.Name = Clean(Wire.Text(r, 32));
                if (version != Wire.Version)
                {
                    string why = "Version mismatch: host has Rick and Morty Together " + ModVersion + " (protocol " + Wire.Version + "), you have " + modVersion + " (protocol " + version + ").";
                    transport.SendAndClose(from, Wire.Packet(Op.Reject, w => Wire.Text(w, why)));
                    Log("Refused " + peer.Name + ": " + why);
                    Check("net.version.refused", true, why);
                    peers.Remove(from);
                    return;
                }
                // Joining is only before the story starts: in the living room (the DVD menu), not in a scene under way.
                if (SceneManager.GetActiveScene().name != "LivingRoom" && !Mutant("nojoinlock"))
                {
                    string why = "The game has started. Join while the host is in the living room (the DVD menu).";
                    transport.SendAndClose(from, Wire.Packet(Op.Reject, w => Wire.Text(w, why)));
                    Log("Refused " + peer.Name + ": the game is under way in " + SceneManager.GetActiveScene().name);
                    JoinsRefusedMidGame++;
                    peers.Remove(from);
                    return;
                }
                peer.Accepted = true;
                Send(from, Wire.Packet(Op.Welcome, w => { w.Write((byte)from); Wire.Text(w, LocalName); w.Write(Time.realtimeSinceStartup); }));
                BroadcastRoster();
                BroadcastScene();
                SendItemSnapshot(from);
                BroadcastAreas();
                Log("Accepted " + peer.Name + " as player " + (from + 1));
                Check("net.peer.accepted", true, peer.Name);
                return;
            }
            if (!peer.Accepted) throw new IOException("packet before hello");
            switch (op)
            {
                case Op.Leave: transport.Drop(from); RemovePeer(from); BroadcastRoster(); return;
                case Op.Ping: { float t = r.ReadSingle(); Send(from, Wire.Packet(Op.Pong, w => { w.Write(t); w.Write(Time.realtimeSinceStartup); })); return; }
                case Op.Pose:
                    {
                        r.ReadByte();   // the claimed sender is ignored: the host knows who sent it
                        PlayerPose pose = PlayerPose.Read(r);
                        ApplyPose(peer, pose);
                        byte[] relay = Wire.Packet(Op.Pose, w => { w.Write((byte)from); pose.Write(w); });
                        foreach (Peer other in peers.Values) if (other.Accepted && other.Id != from) Send(other.Id, relay);
                        return;
                    }
                case Op.ItemClaim: HostItemClaim(from, r); return;
                case Op.ItemState: HostItemState(from, r); return;
                case Op.ControlState: HostState(from, r, Op.ControlState); return;
                case Op.TriggerFired: HostTriggerFired(from, r); return;
                case Op.Cause: HostCause(from, r); return;
                case Op.Despawn: HostDespawn(from, r); return;
                case Op.EnemyHit: HostEnemyHit(from, r); return;
                case Op.WatchClip: HostWatchClip(from, r); return;
                default: throw new IOException("op " + op + " is not accepted from clients");
            }
        }

        void ClientPacket(Op op, BinaryReader r)
        {
            switch (op)
            {
                case Op.Reject: { string why = Wire.Text(r, 512); Status = why; Log("Refused by host: " + why); Check("net.rejected", true, why); Stop("refused"); return; }
                case Op.Welcome:
                    {
                        LocalId = r.ReadByte();
                        string hostName = Wire.Text(r, 32);
                        float hostNow = r.ReadSingle();
                        HostClockOffset = hostNow - Time.realtimeSinceStartup;
                        Status = "Connected to " + hostName + " as player " + (LocalId + 1);
                        Log(Status);
                        Check("net.welcome", true, Status);
                        return;
                    }
                case Op.Roster:
                    {
                        int count = r.ReadByte();
                        HashSet<int> seen = new HashSet<int>();
                        for (int i = 0; i < count; i++)
                        {
                            int id = r.ReadByte();
                            string name = Clean(Wire.Text(r, 32));
                            if (id == LocalId) continue;
                            seen.Add(id);
                            Peer p;
                            if (!peers.TryGetValue(id, out p)) { p = new Peer { Id = id }; peers[id] = p; }
                            p.Name = name; p.Accepted = true;
                        }
                        foreach (int gone in new List<int>(peers.Keys)) if (!seen.Contains(gone)) RemovePeer(gone);
                        return;
                    }
                case Op.Pong:
                    {
                        float sent = r.ReadSingle(), hostNow = r.ReadSingle();
                        float now = Time.realtimeSinceStartup, rtt = now - sent;
                        lastRtt = rtt;
                        // The lowest round trip gives the best offset estimate; drift is negligible over a session.
                        if (rtt >= 0 && rtt <= bestRtt * 1.5f + 0.005f) { bestRtt = Mathf.Min(bestRtt, rtt); HostClockOffset = hostNow + rtt / 2 - now; }
                        return;
                    }
                case Op.Pose:
                    {
                        int from = r.ReadByte();
                        PlayerPose pose = PlayerPose.Read(r);
                        Peer p;
                        if (from == LocalId) return;
                        if (!peers.TryGetValue(from, out p)) { p = new Peer { Id = from, Accepted = true }; peers[from] = p; }
                        ApplyPose(p, pose);
                        return;
                    }
                case Op.Scene: SetHostScene(Wire.Text(r, 64)); return;
                case Op.ItemOwner: ClientItemOwner(r); return;
                case Op.ItemState: ClientItemState(r); return;
                case Op.ControlState: ClientState(r, Op.ControlState); return;
                case Op.TriggerFired: ClientTriggerFired(r); return;
                case Op.Cause: ClientCause(r); return;
                case Op.Stage: ClientStage(r); return;
                case Op.Area: ClientArea(r); return;
                case Op.Areas: ClientAreas(r); return;
                case Op.Spawn: ClientSpawn(r); return;
                case Op.Despawn: ClientDespawn(r); return;
                case Op.Enemies: ClientEnemies(r); return;
                case Op.PlayerHit: ClientPlayerHit(r); return;
                case Op.BossState: ClientBossState(r); return;
                case Op.WatchClip: ClientWatchClip(r); return;
                default: throw new IOException("op " + op + " is not expected from the host");
            }
        }

        static string Clean(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "Morty";
            return name.Length > 24 ? name.Substring(0, 24) : name;
        }

        void RemovePeer(int id)
        {
            Peer p;
            if (!peers.TryGetValue(id, out p)) return;
            if (p.Avatar != null) p.Avatar.Destroy();
            peers.Remove(id);
            if (Hosting) ReleaseItemsOf(id);
        }

        void BroadcastRoster()
        {
            List<Peer> list = new List<Peer>();
            foreach (Peer p in peers.Values) if (p.Accepted) list.Add(p);
            byte[] packet = Wire.Packet(Op.Roster, w =>
            {
                w.Write((byte)(list.Count + 1));
                w.Write((byte)0); Wire.Text(w, LocalName);
                foreach (Peer p in list) { w.Write((byte)p.Id); Wire.Text(w, p.Name); }
            });
            foreach (Peer p in list) Send(p.Id, packet);
        }

        void BroadcastScene()
        {
            string scene = HostSceneNow();
            byte[] packet = Wire.Packet(Op.Scene, w => Wire.Text(w, scene));
            foreach (Peer p in peers.Values) if (p.Accepted) Send(p.Id, packet);
        }

        public void Send(int id, byte[] packet) { if (transport != null) transport.Send(id, packet); }

        // Host: to every accepted client except one (-1 = none). Client: to the host, which relays.
        public void Broadcast(byte[] packet, int except)
        {
            if (!Active) return;
            if (!Hosting) { Send(0, packet); return; }
            foreach (Peer p in peers.Values) if (p.Accepted && p.Id != except) Send(p.Id, packet);
        }

        void SendPose()
        {
            PlayerPose pose;
            if (!CaptureLocalPose(out pose)) return;
            byte[] packet = Wire.Packet(Op.Pose, w => { w.Write((byte)LocalId); pose.Write(w); });
            Broadcast(packet, -1);
        }

        void ApplyPose(Peer peer, PlayerPose pose)
        {
            peer.Pose = pose; peer.HasPose = true; peer.PoseAt = Time.realtimeSinceStartup;
        }

        public IEnumerable<Peer> Peers { get { return peers.Values; } }

        public static MasterHMDAndInputController Master
        {
            get { OwlchemyVRStorage s = OwlchemyVRStorage.no_instantiate_instance; return s != null ? s.MasterHMDAndInputController : null; }
        }
    }
}
