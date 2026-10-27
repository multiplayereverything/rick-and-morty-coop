# MODLOG

Newest first. Each entry: what changed, why, and how it was tested. Decisions made without asking are marked **Decision**.

## 0.1.0 (2026-10-27, protocol 1)

First vertical slice: two players see each other as Morty and pass an item hand to hand.

- **Loader.** BepInEx 5.4.23.5 with the entrypoint moved to `SceneManager.Internal_SceneLoaded` (shipped in the ZIP's
  `BepInEx/config/BepInEx.cfg`). The default entrypoint breaks every scene's scripts on Unity 5.4 (child domain);
  measured three ways, see `docs/RECON.md`.
  - **Decision:** BepInEx only, no MelonLoader build. MelonLoader's Unity 5.4 support is unproven; one loader keeps
    the release and the lab simple.
- **Networking.** TCP for LAN/direct (port 27040, writer-thread keepalives, byte cap instead of packet cap), Steam
  lobbies plus `ISteamNetworking` P2P for online play, protocol check in `Hello`, up to 4 players.
  - **Decision:** the game's 2016 Steamworks.NET has no Steam Networking Sockets, so P2P uses the older
    `SendP2PPacket` (also relayed by Valve). Shipping a newer `steam_api64.dll` would mean replacing a game file.
  - **Decision:** lab Steam runs find the host through a real public Steam lobby tagged with the run id; the data path
    is loopback TCP because both copies share one Steam account. P2P between two accounts is not tested yet.
- **Players.** Pose stream at 20 Hz: head, both hands, grip, held item. Remote players are drawn with the game's own
  Morty-clone head (`CloneHeadDynamic`), a Morty-coloured body that follows head position and yaw, and copies of the
  player's `MortyHand` that play `HandClose` / `HandOpen` with the grip. Copies are stripped of game scripts inside an
  inactive parent so no game code runs on them.
  - **Decision:** the body is two shaped primitives (yellow shirt, blue trousers) until a full Morty model is
    harvested from the garage scene (only the head and hands are loaded everywhere).
- **Items.** Every `GrabbableItem` with a rigidbody gets an id from its scene path. Host-authoritative ownership with
  epochs: a grab claims, the host grants in arrival order (last grab wins = hand-off), the previous holder's hand
  lets go, the owner simulates and streams at 20 Hz until the item rests; everyone else follows kinematically. A
  leaving player's items go back to the host.
- **Saves.** Lab copies never read or write the shared save; a client in someone else's game does not save.
- **Headless lab.** `-vrmode None` plus three repairs (`LoadCore` platform query, play-space selector, first scene
  load) and the game's own debug hands; scripted head and hands rig; tours with RT-CHECK lines; RT-METRIC every 2 s
  (heap, objects, errors, mod ms, fps); RT-STATE on a shared host-time grid for desync.
  - **Decision:** lab windows are normal titled windows ("R&M lab - host", "R&M lab - c1") that start minimized
    without activation, with a 480x270 restore position parked at the right edge, set once at launch, BelowNormal priority, lowest quality, 30 fps (the user's lab rule from 2026-10-27).
- **F5 panel.** Host on Steam (friends or public), host on LAN, find public Steam games, join by IP, player list with
  ping, invite friends, leave. Checked from in-game screenshots (`-rt-panel`).
- **Tested** (`artifacts/tests/`):
  - `handoff` tour: PASS, 12/12 checks on each copy, moving desync max 0.15 m, rest 0.01 m, mod 0.13 ms/frame.
  - `handoff -Lag 120`: PASS.
  - `handoff -Steam`: PASS (13/13 with `steam.lobby`, `steam.found`).
  - Mutants `nohandoff`, `noclaim`, `nofollow`: each caught by `handoff`.
