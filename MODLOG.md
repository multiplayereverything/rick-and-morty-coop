# MODLOG

Newest first. Each entry: what changed, why, and how it was tested. Decisions made without asking are marked **Decision**.

## 0.4.0 (2026-10-27, protocol 4)

Spectator mode is shared; the game takes two players.

- **Two players:** the user's call. A third joiner is refused with "The game is full (2 players)." (LAN and Steam
  P2P), and Steam lobbies are created with two slots. `full` tour: three copies start, the third is refused.
- **Spectator cameras:** whether a player is spectating, and where their spectator camera floats when it is out in the
  room (at one of the room's camera spots), travel in their pose. Everyone else sees an inert copy of the game's
  spectator camera there (its screen blank: the real one mirrors that PC's own window). It disappears when that player
  stops spectating or puts the camera back on their head.
- **Watch other players:** "cycle cameras" (the panel button or double-click grip) now goes through the room's camera
  spots, then each other player's eyes (their head hidden from that camera), then back to your own head. If the
  watched player leaves or changes scene, the camera goes back to the game's own choice.
- **Fix:** each player's spectator camera is a grabbable object, so it was being synced as a shared item; it is now
  left out of item sync (it would have pulled another spectator's camera to the same spot).
- **Avatar jumps:** a jump of more than half a metre (a teleport) now snaps instead of gliding, and an avatar shown
  again after another scene is placed outright. No mutant: the glide lasts about 0.2 s, shorter than the lab can see.
- **Lab judge:** the 3 m "desync" the `story` and `controls` runs reported was two different moments compared: a copy
  whose frame was stalled by a scene load took its sample 0.4 s late while the other player was mid-teleport. Samples
  more than 0.15 s off the shared 2 s grid are now left out.
- **Tested:** `spectator` tour: PASS, 7/7 host, 5/5 client. The client sees the host's camera at the host's spot and
  sees it go away; the host's window camera sits 2 mm from the client's head while watching it, with the client's
  head hidden; one more cycle returns to its own head. Picture: `docs/spectator-through-other-player.png`.
  Mutant `nospectatorview`.
  - `full` (three copies): PASS; the third gets "The game is full (2 players)."
  - `story`: PASS, 10/10 each side; the host now also switches on a second garage area mid-visit and the client
    mirrors it (before, both copies switched on the main area by themselves, so `noarea` was only caught by chance).
- **Lab:** a client's state samples start after its first ping round-trip (its host clock is wrong before that), and
  the first sample after a scene change is not compared (it can catch the one frame a player spawns in the new scene
  before moving to their spot). A copy that hangs at startup (other mods' labs share this PC) is restarted once.

## 0.3.0 (2026-10-27, protocol 3)

Story, level changes, NPC dialogue and animation are shared; remote players are a floating Morty head with hands.

- **Levels:** the living room (the DVD menu) and the garage (which holds every mission) follow the host. The host
  announces a load as its fade starts, so everyone loads together; a client's own load (its DVD player, eating the
  menu) is held back unless it is the host's scene. Garage areas (the additive mission scenes switched by
  `SceneController.BeginDelayedActivation/Deactivation`) mirror the host's, by event and a 3 s snapshot; a client
  standing in an area the host switched off is teleported to the host's zone.
- **Story:** the Brain's causes are string keys (`EVENT:Item:Item`). The host's Brain decides: every cause it runs is
  replayed on the clients (`Op.Cause`); a client's own causes go to the host as requests and do not run on their own.
  A request for a cause the host itself saw in the last second is the same event seen twice and is dropped. Causes
  from one machine's hardware (`PLAYSPACE_SIZE`, `HARDWARE`, `PLATFORM`) stay local. The progression stage is sent by
  asset name (`Op.Stage`, and in the snapshot).
- **NPC dialogue and animation:** Rick's and Morty's lines and animations are Brain effects, so they follow the
  replayed causes. The game's dice (a node's "one random effect", the random interrupt line, random waits) are seeded
  from what is rolled and how often since the scene loaded, so every copy picks the same line.
  - **Decision:** a copy that loads a scene runs that scene's opening effects itself; with loads now starting together
    they line up (75 ms apart in the lab). A mid-scene join replays the opening for the joiner only.
- **Remote players:** the game's Morty-clone head and Morty hands only (no body), as the user asked. The name above
  the head always faces the viewer (it read mirrored from the front).
- **Fix:** the save patch named an overloaded method, Harmony threw, and `PatchAll` stopped there, so later patches
  never loaded (since 0.1.0). With every patch loaded, the 0.2.0 `nograbkinematic` switch no longer changes anything
  (the game marks the door as held before the mod's grab handler runs, which the other half of that check covers), so
  the switch is gone; the check stays. Both `SaveLoad.Save` overloads are now patched, every patch class is applied on its own,
  and a patch that fails is a mod error in the lab. No save file was ever written by a lab copy.
- **Lab:** without a headset the game's fader never finishes, so headless copies load scenes directly. `story` tour;
  `portrait` tour (in-game picture of the other player, `docs/remote-player.png`); `RT-FX` lines for every Brain
  effect, and the verifier checks that dialogue and animation effects match across copies in the same scene.
- **Tested:**
  - `story` tour: PASS, 9/9 host, 10/10 client: a host story event runs on the client; a client event is held and
    runs after the host sends it back; the stage follows; the client follows the host into the garage with the same
    areas on; its own load back is held; it follows the host back to the living room. NPC effects: 12 pairs, 0 missing.
  - `portrait`: PASS.

## 0.2.0 (2026-10-27, protocol 2)

Levers, doors, sliders, buttons and pull cords are shared.

- **Levers, doors, drawers, sliders** (`LimitedIndirectGrabbableController`: `GrabbableHinge`, `GrabbableSlider`) use
  the same ownership as items (grab claims, host grants, epochs), but the owner streams the position along the axis
  and the lock state (hard and soft, upper and lower) instead of a pose (`Op.ControlState`). Followers are driven with
  the game's own `SetNormalizedAmount`, `LockUpper/LockLower`, `Unlock*` and soft-lock calls, so the lock events and
  the game code listening to them run on every copy. The follower's own `LateUpdate`/`FixedUpdate` are skipped
  (Harmony prefixes) so its spring and lock checks cannot fight the stream or fire events twice. Controls without a
  rigidbody are synced too.
  - **Decision:** the player-height lever and the spectator-camera lever stay local: their effect is one player's own
    height and camera.
- **Buttons and pull cords** (`PushableButton.OnButtonPushed`, `PullCordController.OnPullCordPulled`): the copy where
  a hand fired it tells the host; the host replays it with the game's own button lock (or the cord's event) and tells
  everyone else (`Op.TriggerFired`). A second fire of the same trigger within 0.5 s counts once, because a followed
  item can push the same button on two copies.
  - **Decision:** the DVD player's Play button stays local until level changes are synced (it loads the garage).
- **Fix:** a door that was following another player's copy stayed kinematic when grabbed here (some doors are
  kinematic whenever no hand holds them, and the mod restored that after the game's own grab handler had cleared it),
  so it never moved. Mutant `nograbkinematic`.
- **Lab:** `survey` tour lists every control and button in the scene with its listeners; `RT-STATE` logs
  `ctl<id>=amount|locks|owner|epoch|mode`; the verifier compares settled controls (within 2% of travel, same locks).
- **Tested:**
  - `controls` tour: PASS, 8/8 checks on the host, 7/7 on the client. The host opens the left cabinet door and the
    client sees it open; the client opens the right door (ownership passes) and the host sees it; the client pushes
    the DVD player's eject button and the host's copy replays it (the game's listener fires); the host's door swings
    shut by itself and latches, and the client sees the latch. 21 settled control pairs, all equal.
  - Mutants `nocontrol`, `nobutton`, `nograbkinematic`: each caught by `controls`.

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
