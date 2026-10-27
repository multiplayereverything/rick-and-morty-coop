using System;
using System.Collections.Generic;
using Steamworks;

namespace RickAndMortyTogether
{
    // Online play through the game's own Steamworks (app 469610). The game ships a 2016 Steamworks.NET without
    // Steam Networking Sockets, so packets go over ISteamNetworking P2P (SendP2PPacket), which also runs through
    // Valve's relay when a direct path fails: nobody forwards a port. Main thread only: Poll pumps the queue and
    // session callbacks arrive through SteamAPI.RunCallbacks in the game's own SteamManager.Update.
    public sealed partial class Transport
    {
        public bool OnSteam { get; private set; }
        const int SteamChannel = 7;
        // P2P is connectionless: a peer that sends nothing (not even the runtime's 1 s ping) for this long is gone.
        const float SteamTimeout = 30f;
        readonly Dictionary<ulong, int> steamPeerOf = new Dictionary<ulong, int>();
        readonly Dictionary<int, CSteamID> steamLinks = new Dictionary<int, CSteamID>();
        readonly Dictionary<int, float> steamSeen = new Dictionary<int, float>();
        Callback<P2PSessionRequest_t> steamRequest;
        Callback<P2PSessionConnectFail_t> steamFail;
        readonly byte[] steamInbox = new byte[MaxFrame];
        public static Func<float> Clock;   // process-scoped

        public void HostSteam()
        {
            if (listener != null || Hosting || disposed) throw new InvalidOperationException("Transport already used.");
            BeginSteam();
            Hosting = true;
        }

        public void JoinSteam(CSteamID host)
        {
            if (disposed) throw new ObjectDisposedException("Transport");
            BeginSteam();
            steamLinks[0] = host; steamPeerOf[host.m_SteamID] = 0; steamSeen[0] = Now();
            Emit(0, "connected", null, null);
        }

        static float Now() { return Clock != null ? Clock() : 0f; }

        void BeginSteam()
        {
            OnSteam = true;
            SteamNetworking.AllowP2PPacketRelay(true);
            steamRequest = Callback<P2PSessionRequest_t>.Create(OnSteamRequest);
            steamFail = Callback<P2PSessionConnectFail_t>.Create(OnSteamFail);
        }

        void OnSteamRequest(P2PSessionRequest_t request)
        {
            if (disposed) return;
            // Only the host takes new peers; a client accepts its own host only.
            if (Hosting || steamPeerOf.ContainsKey(request.m_steamIDRemote.m_SteamID)) SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
        }

        void OnSteamFail(P2PSessionConnectFail_t fail)
        {
            int id;
            if (!disposed && steamPeerOf.TryGetValue(fail.m_steamIDRemote.m_SteamID, out id)) DropSteam(id, "Steam P2P session failed (error " + fail.m_eP2PSessionError + ")");
        }

        void PumpSteam()
        {
            uint size;
            int guard = 0;
            while (guard++ < 256 && SteamNetworking.IsP2PPacketAvailable(out size, SteamChannel))
            {
                CSteamID from;
                if (!SteamNetworking.ReadP2PPacket(steamInbox, (uint)steamInbox.Length, out size, out from, SteamChannel)) break;
                if (size < 1 || size > MaxFrame) continue;
                int id;
                if (!steamPeerOf.TryGetValue(from.m_SteamID, out id))
                {
                    if (!Hosting) continue;
                    id = 1;
                    while (steamLinks.ContainsKey(id)) id++;
                    if (id > 3)
                    {
                        byte[] reject = Wire.Packet(Op.Reject, w => Wire.Text(w, "The game is full (4 players)."));
                        SteamNetworking.SendP2PPacket(from, reject, (uint)reject.Length, EP2PSend.k_EP2PSendReliable, SteamChannel);
                        continue;
                    }
                    steamLinks[id] = from; steamPeerOf[from.m_SteamID] = id;
                    Emit(id, "connected", null, null);
                }
                steamSeen[id] = Now();
                byte[] data = new byte[size];
                Buffer.BlockCopy(steamInbox, 0, data, 0, (int)size);
                Emit(id, "packet", data, null);
            }
            float now = Now();
            List<int> stale = null;
            foreach (KeyValuePair<int, float> seen in steamSeen) if (now - seen.Value > SteamTimeout) (stale ?? (stale = new List<int>())).Add(seen.Key);
            if (stale != null) foreach (int id in stale) DropSteam(id, "no packets for " + SteamTimeout + " s");
        }

        void SendSteam(int id, byte[] packet, bool close)
        {
            CSteamID to;
            if (!steamLinks.TryGetValue(id, out to)) return;
            // Steam buffers reliable packets itself; a failure means the session is gone.
            if (!SteamNetworking.SendP2PPacket(to, packet, (uint)packet.Length, EP2PSend.k_EP2PSendReliable, SteamChannel)) { DropSteam(id, "Steam would not send"); return; }
            if (close) DropSteam(id, null);
        }

        void DropSteam(int id, string reason)
        {
            CSteamID who;
            if (!steamLinks.TryGetValue(id, out who)) return;
            steamLinks.Remove(id); steamPeerOf.Remove(who.m_SteamID); steamSeen.Remove(id);
            SteamNetworking.CloseP2PSessionWithUser(who);
            Emit(id, "disconnected", null, reason);
        }

        void DisposeSteam()
        {
            if (!OnSteam) return;
            foreach (CSteamID who in steamLinks.Values) SteamNetworking.CloseP2PSessionWithUser(who);
            steamLinks.Clear(); steamPeerOf.Clear(); steamSeen.Clear();
            if (steamRequest != null) { steamRequest.Dispose(); steamRequest = null; }
            if (steamFail != null) { steamFail.Dispose(); steamFail = null; }
        }
    }
}
