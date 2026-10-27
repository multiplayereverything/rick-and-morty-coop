using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RickAndMortyTogether
{
    // The F5 panel on the desktop mirror window (the host usually sets the game up there before putting the headset
    // on). One decision per state: not connected, it offers hosting on Steam first, then joining; connected, it shows
    // who is in the game and how to leave. Solid dark panel, one accent (Morty's shirt yellow) on the primary action.
    public sealed partial class TogetherRuntime
    {
        bool panelOpen;
        Rect panelRect = new Rect(24, 24, 360, 10);
        string ipField = "";
        string nameField = "";
        GUIStyle headStyle, titleStyle, textStyle, dimStyle, primaryStyle, buttonStyle, fieldStyle, boxStyle;
        Texture2D panelTexture, primaryTexture, primaryHoverTexture, buttonTexture, buttonHoverTexture, fieldTexture;
        float lastRtt = -1;
        string SettingsPath { get { return Path.Combine(Path.Combine(Root, "BepInEx"), Path.Combine("config", "RickAndMortyTogether.ini")); } }

        void LoadSettings()
        {
            nameField = LocalName; ipField = joinAddress;
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    if (key == "Name" && value.Length > 0 && !LabMode) { LocalName = Clean(value); nameField = LocalName; }
                    if (key == "LastAddress" && value.Length > 0) ipField = value;
                    if (key == "PublicLobby") PublicLobby = value == "true";
                }
            }
            catch (Exception ex) { Log("Settings not read: " + ex.Message); }
        }

        void SaveSettings()
        {
            if (LabMode) return;
            try { File.WriteAllText(SettingsPath, "Name=" + LocalName + "\nLastAddress=" + ipField + "\nPublicLobby=" + (PublicLobby ? "true" : "false") + "\n"); }
            catch (Exception ex) { Log("Settings not saved: " + ex.Message); }
        }

        static Texture2D Solid(Color c) { Texture2D t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); t.hideFlags = HideFlags.HideAndDontSave; return t; }

        void BuildStyles()
        {
            if (titleStyle != null) return;
            Color text = new Color(0.93f, 0.92f, 0.88f), dim = new Color(0.62f, 0.64f, 0.66f), ink = new Color(0.12f, 0.11f, 0.08f);
            panelTexture = Solid(new Color(0.11f, 0.13f, 0.15f, 0.97f));
            primaryTexture = Solid(new Color(0.98f, 0.83f, 0.18f)); primaryHoverTexture = Solid(new Color(1f, 0.9f, 0.4f));
            buttonTexture = Solid(new Color(0.21f, 0.24f, 0.27f)); buttonHoverTexture = Solid(new Color(0.28f, 0.32f, 0.36f));
            fieldTexture = Solid(new Color(0.07f, 0.08f, 0.09f));
            boxStyle = new GUIStyle(GUI.skin.window) { padding = new RectOffset(14, 14, 12, 14) };
            boxStyle.normal.background = panelTexture; boxStyle.onNormal.background = panelTexture; boxStyle.normal.textColor = text; boxStyle.onNormal.textColor = text;
            headStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 0, 6) };
            headStyle.normal.textColor = text;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 10, 4) };
            titleStyle.normal.textColor = dim;
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            textStyle.normal.textColor = text;
            dimStyle = new GUIStyle(textStyle) { fontSize = 12 };
            dimStyle.normal.textColor = dim;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, fixedHeight = 28, border = new RectOffset(0, 0, 0, 0) };
            buttonStyle.normal.background = buttonTexture; buttonStyle.hover.background = buttonHoverTexture; buttonStyle.active.background = buttonHoverTexture;
            buttonStyle.normal.textColor = text; buttonStyle.hover.textColor = text; buttonStyle.active.textColor = text;
            primaryStyle = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Bold, fixedHeight = 34 };
            primaryStyle.normal.background = primaryTexture; primaryStyle.hover.background = primaryHoverTexture; primaryStyle.active.background = primaryHoverTexture;
            primaryStyle.normal.textColor = ink; primaryStyle.hover.textColor = ink; primaryStyle.active.textColor = ink;
            fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 13, fixedHeight = 26, padding = new RectOffset(8, 8, 5, 5) };
            fieldStyle.normal.background = fieldTexture; fieldStyle.focused.background = fieldTexture; fieldStyle.normal.textColor = text; fieldStyle.focused.textColor = text;
        }

        float panelShotAt = -1, panelHostAt;   // lab: -rt-panel opens the panel and saves a screenshot of it

        void PanelUpdate()
        {
            if (LabMode && panelShotAt < 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-rt-panel") >= 0 && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "LivingRoom") { panelOpen = true; panelShotAt = Time.realtimeSinceStartup + 6; }
            if (panelShotAt > 0 && Time.realtimeSinceStartup >= panelShotAt) { panelShotAt = 0; Application.CaptureScreenshot(Path.Combine(Root, "panel-" + (Active ? "session" : "start") + ".png")); if (!Active && autoRole.Length == 0) { panelHostAt = Time.realtimeSinceStartup + 1; panelShotAt = Time.realtimeSinceStartup + 4; } }
            if (panelHostAt > 0 && Time.realtimeSinceStartup >= panelHostAt) { panelHostAt = 0; StartHost(false); }
            if (Input.GetKeyDown(KeyCode.F5)) { panelOpen = !panelOpen; if (panelOpen) { nameField = LocalName; } }
        }

        void OnGUI()
        {
            if (!panelOpen) return;
            BuildStyles();
            panelRect.height = 10;   // GUILayout grows it to fit the content
            panelRect = GUILayout.Window(0x52414D, panelRect, DrawPanel, "", boxStyle, GUILayout.Width(360));
        }

        void DrawPanel(int id)
        {
            GUILayout.Label("Rick and Morty Together " + ModVersion, headStyle);
            GUILayout.Label(Status, textStyle);
            if (!Active && transport == null) DrawStart();
            else DrawSession();
            GUILayout.Space(8);
            GUILayout.Label("F5 closes this panel. Two players: the host's game is the shared one.", dimStyle);
            GUI.DragWindow(new Rect(0, 0, 10000, 30));
        }

        void DrawStart()
        {
            GUILayout.Label("YOUR NAME", titleStyle);
            nameField = GUILayout.TextField(nameField, 24, fieldStyle);
            if (Clean(nameField) != LocalName && GUI.changed) { LocalName = Clean(nameField); SaveSettings(); }

            GUILayout.Label("HOST", titleStyle);
            if (!SteamReady) GUILayout.Label("Steam is not running, so only LAN and direct IP are available.", dimStyle);
            else
            {
                if (GUILayout.Button("Host on Steam", primaryStyle)) { SaveSettings(); StartSteamHost(); }
                bool pub = GUILayout.Toggle(PublicLobby, PublicLobby ? " Public: anyone can find this game" : " Friends only: friends join from your invite", GUI.skin.toggle);
                if (pub != PublicLobby) { PublicLobby = pub; SaveSettings(); }
            }
            if (GUILayout.Button("Host on LAN / direct IP (port " + port + ")", buttonStyle)) StartHost(false);

            GUILayout.Label("JOIN", titleStyle);
            if (SteamReady)
            {
                if (GUILayout.Button(Searching ? "Looking for Steam games..." : "Find public Steam games", buttonStyle) && !Searching) { steamLabJoin = false; StartSteamFind(); }
                foreach (LobbyInfo lobby in new List<LobbyInfo>(FoundLobbies))
                    if (GUILayout.Button((lobby.Name.Length > 0 ? lobby.Name : "Unnamed") + "'s game   " + lobby.Members + "/4", buttonStyle)) JoinLobby(lobby.Id);
                GUILayout.Label("A friend's invite or Join Game in the Steam friends list joins directly.", dimStyle);
            }
            GUILayout.BeginHorizontal();
            ipField = GUILayout.TextField(ipField, 64, fieldStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Join IP", buttonStyle, GUILayout.Width(84)) && ipField.Trim().Length > 0)
            {
                string address = ipField.Trim();
                int colon = address.LastIndexOf(':');
                int custom;
                if (colon > 0 && int.TryParse(address.Substring(colon + 1), out custom)) { port = custom; address = address.Substring(0, colon); }
                joinAddress = address; SaveSettings(); StartJoin();
            }
            GUILayout.EndHorizontal();
        }

        void DrawSession()
        {
            GUILayout.Label("PLAYERS", titleStyle);
            GUILayout.Label("1. " + (Hosting ? LocalName + " (you, host)" : HostNameOrDefault()), textStyle);
            List<Peer> list = new List<Peer>(peers.Values);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (Peer p in list)
            {
                if (!p.Accepted || p.Id == 0) continue;
                GUILayout.Label((p.Id + 1) + ". " + p.Name + (p.Id == LocalId ? " (you)" : ""), textStyle);
            }
            if (!Hosting && LocalId > 0) GUILayout.Label((LocalId + 1) + ". " + LocalName + " (you)" + (lastRtt >= 0 ? "   ping " + Mathf.RoundToInt(lastRtt * 1000) + " ms" : ""), textStyle);
            if (Hosting && AcceptedCount == 0) GUILayout.Label("Nobody has joined yet. Invite friends on Steam, or give them your IP and port " + port + ".", dimStyle);
            GUILayout.Space(6);
            if (Hosting && Lobby != Steamworks.CSteamID.Nil && GUILayout.Button("Invite Steam friends", primaryStyle)) InviteFriends();
            if (GUILayout.Button(Hosting ? "Stop hosting" : "Leave game", buttonStyle)) Stop("left from the panel");
        }

        string HostNameOrDefault() { Peer h; return peers.TryGetValue(0, out h) && h.Name.Length > 0 ? h.Name + " (host)" : "Host"; }
    }
}
