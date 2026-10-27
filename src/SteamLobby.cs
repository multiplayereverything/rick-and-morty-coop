using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RickAndMortyTogether
{
    public sealed class LobbyInfo
    {
        public CSteamID Id;
        public string Name;
        public int Members;
    }

    // Steam lobbies through the game's own Steamworks: host a friends-only (or public) lobby, invite through the
    // overlay, join from an invite, Join Game, or +connect_lobby, or browse public games. Once in a lobby, players
    // talk to the lobby owner over Steam P2P (SteamNet.cs).
    public sealed partial class TogetherRuntime
    {
        public CSteamID Lobby = CSteamID.Nil;
        public bool PublicLobby;
        public readonly List<LobbyInfo> FoundLobbies = new List<LobbyInfo>();
        public bool Searching;
        CallResult<LobbyCreated_t> lobbyCreated;
        CallResult<LobbyMatchList_t> lobbyList;
        Callback<LobbyEnter_t> lobbyEnter;
        Callback<GameLobbyJoinRequested_t> lobbyJoinRequest;
        string steamLabTag = "";
        bool steamLabJoin;

        public static bool SteamReady
        {
            get
            {
                // SteamAPI.IsSteamRunning() can be false in copies whose own SteamAPI.Init() worked; BLoggedOn throws if not initialised.
                try { return SteamUser.BLoggedOn(); }
                catch (Exception) { return false; }   // Steamworks not initialised: no Steam play
            }
        }

        void InitSteamCallbacks()
        {
            if (lobbyEnter != null || !SteamReady) return;
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
            lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            lobbyJoinRequest = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequest);
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-rt-steam-lab" && i + 1 < args.Length) steamLabTag = args[++i];
                ulong id;
                // Steam starts the game with +connect_lobby <id> when a friend accepts an invite with the game closed.
                if (args[i] == "+connect_lobby" && i + 1 < args.Length && ulong.TryParse(args[++i], out id)) SteamMatchmaking.JoinLobby(new CSteamID(id));
            }
        }

        public void StartSteamHost()
        {
            InitSteamCallbacks();
            if (!SteamReady) { Status = "Steam is not running"; Log(Status); return; }
            // Steam's lobby search lists public lobbies only; a lab run finds its own host by a run tag.
            if (steamLabTag.Length > 0) PublicLobby = true;
            Status = "Creating a Steam lobby";
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(PublicLobby ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly, 4));
        }

        void OnLobbyCreated(LobbyCreated_t result, bool failed)
        {
            if (failed || result.m_eResult != EResult.k_EResultOK) { Status = "Steam would not create a lobby (" + result.m_eResult + ")"; Log(Status); return; }
            Lobby = new CSteamID(result.m_ulSteamIDLobby);
            bool lab = steamLabTag.Length > 0;
            // Lab copies share one Steam account, which cannot reach itself over P2P: the lobby is real, the data
            // path is the lab's loopback TCP port named in the lobby.
            StartHost(!lab);
            if (!Active) return;
            SteamMatchmaking.SetLobbyData(Lobby, "rmt", "1");
            SteamMatchmaking.SetLobbyData(Lobby, "ver", Wire.Version.ToString());
            SteamMatchmaking.SetLobbyData(Lobby, "name", LocalName);
            if (lab) { SteamMatchmaking.SetLobbyData(Lobby, "lab", steamLabTag); SteamMatchmaking.SetLobbyData(Lobby, "labport", port.ToString()); }
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + Lobby.m_SteamID);
            SteamFriends.SetRichPresence("status", "Rick and Morty Together");
            Status = "Hosting on Steam (" + (PublicLobby ? "public" : "friends") + " lobby)";
            Log(Status + " " + Lobby.m_SteamID);
            Check("steam.lobby", true, Lobby.m_SteamID.ToString());
        }

        public void InviteFriends() { if (Lobby != CSteamID.Nil) SteamFriends.ActivateGameOverlayInviteDialog(Lobby); }

        void OnJoinRequest(GameLobbyJoinRequested_t request) { Log("Joining a friend's lobby from Steam"); SteamMatchmaking.JoinLobby(request.m_steamIDLobby); }

        public void JoinLobby(CSteamID lobby) { InitSteamCallbacks(); if (SteamReady) SteamMatchmaking.JoinLobby(lobby); }

        void OnLobbyEnter(LobbyEnter_t entered)
        {
            CSteamID lobby = new CSteamID(entered.m_ulSteamIDLobby);
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (owner == SteamUser.GetSteamID()) return;   // our own lobby
            if (entered.m_EChatRoomEnterResponse != 1) { Status = "Could not enter the Steam lobby (" + entered.m_EChatRoomEnterResponse + ")"; Log(Status); return; }
            string ver = SteamMatchmaking.GetLobbyData(lobby, "ver");
            Lobby = lobby;
            Log("In " + SteamMatchmaking.GetLobbyData(lobby, "name") + "'s lobby (protocol " + ver + ")");
            JoinSteamHost(owner);
        }

        // Lists Rick and Morty Together lobbies (friends' and public ones the search returns).
        public void StartSteamFind()
        {
            InitSteamCallbacks();
            if (!SteamReady) { Status = "Steam is not running"; Log(Status); return; }
            steamLabJoin = steamLabTag.Length > 0;
            Searching = true;
            Status = "Looking for Steam games";
            SteamMatchmaking.AddRequestLobbyListStringFilter("rmt", "1", ELobbyComparison.k_ELobbyComparisonEqual);
            if (steamLabTag.Length > 0) SteamMatchmaking.AddRequestLobbyListStringFilter("lab", steamLabTag, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            lobbyList.Set(SteamMatchmaking.RequestLobbyList());
        }

        void OnLobbyList(LobbyMatchList_t list, bool failed)
        {
            Searching = false;
            FoundLobbies.Clear();
            if (!failed)
                for (int i = 0; i < list.m_nLobbiesMatching; i++)
                {
                    CSteamID id = SteamMatchmaking.GetLobbyByIndex(i);
                    FoundLobbies.Add(new LobbyInfo { Id = id, Name = SteamMatchmaking.GetLobbyData(id, "name"), Members = SteamMatchmaking.GetNumLobbyMembers(id) });
                }
            Status = FoundLobbies.Count + " Steam game" + (FoundLobbies.Count == 1 ? "" : "s") + " found";
            Log(Status);
            if (!steamLabJoin) return;
            if (FoundLobbies.Count == 0) { labSteamRetryAt = Time.realtimeSinceStartup + 3; return; }
            CSteamID lobby = FoundLobbies[0].Id;
            Check("steam.found", true, "lobby " + lobby.m_SteamID + " by " + FoundLobbies[0].Name);
            int labPort;
            if (int.TryParse(SteamMatchmaking.GetLobbyData(lobby, "labport"), out labPort)) { port = labPort; joinAddress = "127.0.0.1"; autoRole = "join"; StartJoin(); }
        }

        float labSteamRetryAt = -1;

        void SteamUpdate()
        {
            if (labSteamRetryAt > 0 && Time.realtimeSinceStartup >= labSteamRetryAt) { labSteamRetryAt = -1; StartSteamFind(); }
        }

        void LeaveLobby()
        {
            if (Lobby == CSteamID.Nil) return;
            try { SteamMatchmaking.LeaveLobby(Lobby); SteamFriends.ClearRichPresence(); }
            catch (Exception) { }   // Steam already shut down
            Lobby = CSteamID.Nil;
        }
    }
}
