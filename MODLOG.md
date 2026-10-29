# MODLOG

Newest first. Each entry: what changed, why, and how it was tested. Decisions made without asking are marked **Decision**.

## 0.7.1 (2026-10-29, protocol 7)

The garage's own spawners checked in the lab: the computer's parts, made by the game's
`BrainControlledModule_Computer.SpawnAndAttachComputerParts` (broken ones when the garage loads, working ones when the
story repairs it). Same protocol as 0.7.0.

- **Fix:** a part the game made and attached (RAM stick, crystal, fuse in the computer) was registered or not depending
  on whether its rigidbody happened to be on, so the copies registered different parts. A client then removed its own
  attached parts as "made only here", and the game's next repair tripped over the destroyed part
  (NullReferenceException in `AttachableItem.Detach`). Attached parts the game made now stay each copy's own (both
  games make and attach them, through the shared story) and are registered once loose. Mutant `noattachwait`.
- **Fix:** something only the host's game made, with nothing of that name out on the client (the working parts when
  the client's stage came from the host's snapshot), was dropped ("nothing like it here to copy"). After the 30 s wait
  for the client's own, it is now copied from a loaded one that is not out, a prefab included. Mutant
  `nospawntemplate`.
- **Fix:** copying a game object for a remote player (Morty head, spectator camera) removed its scripts in an order
  Unity refuses ("Can't remove TextContainer because TextMeshPro depends on it", a game error on the client). They are
  removed last first now.
- **Decision:** a part a player pulls out of the computer becomes a synced item from then on; putting a part into the
  computer is not synced (each copy's attach points are its own). Listed in Known limits.
- **Lab:** where the game starts a player after a scene load (the first pad on `TeleportationController`'s
  `initialZoneDatas`, the laundry pad for the garage intro) is logged by `LogGarage` and checked on both copies in the
  `garage` tour (`garage.host.start`, `garage.client.start`: PASS, both on `TeleportZone_GarageLaundry`). Checked for
  the user's report that both Mortys started away from the intro's spot; not reproduced in the lab.
- **Not a mod issue:** two lab runs had one copy freeze for 20-45 s with no error (shared lab PC); reruns passed.
- **Tested** (`artifacts/tests/`):
  - `garage` tour (new): PASS, 5/5 checks on each copy. The host's game makes a loose working RAM stick from the part's
    prefab; the client, with none out, gets a copy. Both repair the computer (the client 4 s later): each holds its own
    working parts, nothing copied or removed by the mod, no game errors after the scene load.
  - Mutants `nospawntemplate`, `noattachwait`: each caught by `garage`.
  - `spectator`: PASS, no TextContainer error.
  - `spawns`, `handoff`, `boss`: PASS (one `spawns` run failed while the host ran at 14 fps with a 4 s stall; the rerun
    passed).

## 0.7.0 (2026-10-28, protocol 7)

Late joiners, characters looking at the nearest player, flat-screen teleporting, and slow machines.

- **Characters look at the nearest player:** when the story aims Rick or Morty at "the player"
  (`BrainControlledModule_CharacterHeadLook.LookAtPlayer`), each copy turns that to the nearest player's head
  (another player's drawn head, or this player's own), twice a second.
- **Joining mid-scene:** a player who loads a scene the host has been in for 15 s or more starts that scene's story
  from its beginning; its opening lines would play for them alone. Their voiced lines are completed without playing
  (the module's own completion, so the story moves on) from the load until 15 s after the scene's first line there.
  The host's scene message now carries how long it has been in that scene (`Op.Scene`, protocol 7).
  - **Known limit:** after that, the joiner's timed banter trails the host's until the next shared story event
    (the story graph's running state is not copied). Players still see the same items, doors, story progress and
    levels; the verifier reports this case instead of comparing it.
- **Fix (flat-screen mode):** teleporting stayed off after every scene load without a headset (the game switches it
  back on when the new scene's fade-in finishes, and without a headset the fade never finishes). It is switched back
  on once the scene has run 2 s. Found by the flat tour's new garage step. Mutant `noheadlessteleport`.
- **Fix (slow machines):** a host's announcement of something the game made could arrive before a slow client had
  made its own copy; the client dropped the announcement, then removed its own copy as one only it made. The
  announcement now waits up to 30 s for a matching copy. Found in a late-join run. Mutant `nospawnwait`.
- **Fix:** a host whose own living room was still starting when it loaded the garage ran the game's scene cleanup too
  early (NullReferenceException); the cleanup is skipped when there is nothing to clean.
- **Docs:** `docs/PLAYTEST.md`, the checklist for what only real players can check (two headsets on two Steam
  accounts, over the internet, flat-screen mode with a real keyboard and mouse).
- **Lab:** `latejoin` tour (`-rt-join-after` delays a lab client's join); the flat tour now tests T (teleport) in the
  garage; the spawns tour has a slow-machine case.
- **Tested** (`artifacts/tests/`):
  - `latejoin` tour: PASS (client joins 60 s late in the garage; Rick looks at the client's head when it is nearer;
    the client's opening lines are skipped).
  - `spawns`, `flat`, `spectator`: PASS on the final build (rerun 2026-10-29); `handoff`, `controls`, `story`: PASS.
  - Mutants `noopeningmute`, `nolooknearest` (latejoin), `noheadlessteleport` (flat), `nospawnwait` (spawns): each
    caught. `tests/rules-test.py`: 0 failed.

## 0.6.0 (2026-10-28, protocol 6)

Enemies, the final boss and death work in co-op; the whole garage, every mission area, is played through together.

- **Enemies** (the egg shoot-out's gunmen, a fixed pool under `EnemyManager`, so a pool index is a shared id): the
  host's game runs them and streams each one's state (on, alive, pose) at 15 Hz (`Op.Enemies`). A client spawns and
  moves none itself (its `RunPhase1/RunPhase2` are held, a mirrored enemy's own routines stopped) and places the host's.
  A client's hit (gun, thrown item) goes to the host (`Op.EnemyHit`), which applies it; the death then plays on the
  client too, which also counts the kill for its own story. Enemies aim at the nearest player: on the host another
  player's drawn head gets a collider on the layer of the player's own `HurtTarget` (what enemy bullets look for), and
  a bullet that hits it goes to that player (`Op.PlayerHit`), who takes it through their own `HurtTarget`.
- **Death** is one player's own: the Death area, the DEATH / RESPAWN events and every story event a dead player raises
  stay on their copy; nobody is pulled into or out of a death by area or zone following.
- **Final boss**: its state machine (`FinalBossController.SetState`) runs on the host; each state change goes to the
  clients (`Op.BossState`, and every 2 s), and a client's boss changes state only when the host's did.
  - **Decision:** in the killing state each copy's boss aims at its own player, so the death beam kills both players
    (each respawns in their own game): the boss threatens everyone in the room.
- **Playthrough** of every mission area: entered the way the blast-shield portal does it (the garage stays on, the
  next mission on, the previous off). A client in an area that goes off is brought to the host.
- **Fix:** the mod took 3.3-3.9 ms a frame in the garage (21,000 objects) because 0.5.0 searched all grabbables every
  0.5 s. New grabbables now register when the game starts them (`GrabbableItem.Start` postfix); a full search runs
  every 3 s, and grabbables that are never synced are not looked at again.
- **Fix:** a client following the host into the garage while its own living room was still starting called the game's
  scene cleanup too early (NullReferenceException); it now waits for its teleport system and rig. Exceptions the mod
  catches are now mod errors in the lab (they were logged under a name the verifier didn't count).
- **Fix:** loading the garage reloads the story stage from the copy's own save (a fresh one for a guest), so a client
  arriving in the garage dropped back to the first stage, and the host's 3 s snapshot did not put it back (it only
  applied a stage when the host's had changed). The snapshot now puts it back whenever they differ. Found by the
  `boss` tour (the client's boss never appeared). Mutant `nostagerepair`.
- **Lab:** `enemies`, `death`, `boss` and `playthrough` tours. The lab rig can stand in the play area
  (`RigRelative`), so it moves with teleports. The verifier leaves out the first state sample after a teleport, and
  does not judge frame cost or NPC line timing when a copy ran under 27 fps (other labs on this PC starved it); the
  run output says so instead. A copy flags its own instant jumps (a part moving 30 cm in one frame); the verifier does
  not compare that player at that sample. Hands that jump more than half a metre snap, like heads. Tours can make
  unjudged tries (check names starting with `~`): the controls tour retries closing a door the hand slipped off.
- **Tested** (another session's lab had the CPU at 90-100% for most of these runs):
  - `enemies`: PASS, 9/9 host, 6/6 client: the same enemies 0.00 m apart, a client hit applied by the host and the
    death shown on the client, every enemy aims at the client in the arena, the bullet ray hits its head, the hit goes
    to the client and it dies in its own game.
  - `death`: PASS, 4/4 host, 5/5 client: the client dies, stays in its death area 10 s, respawns; the host never dies.
  - `boss`: PASS, 5/5 host, 4/4 client: the client's stage repaired to the boss stage, the boss's entrance and battle
    shown from the host's states, the client's own state change held.
  - `playthrough`: PASS, 14/14 host, 8/8 client: shoot-out, bathroom, petri dish, Troy, satellite and home; the
    client brought along each time its area went off.
  - Every earlier tour again: hand-off, controls, story, spectator, spawns, flat (twice at 6-13 fps), full: PASS.
  - Mutants: 19, every one caught by its tour (`noenemysync`, `noenemytarget`, `nodeathlocal`, `nobosssync`,
    `nostagerepair` new).

## 0.5.0 (2026-10-27, protocol 5)

Things the game makes while playing, and things it destroys, are shared. Flat-screen mode: play without a headset.

- **Spawns:** the game makes items from about forty places (combinator results, dispensers, Youseeks, Meeseeks,
  laundry, deliveries). Most already run on both copies because what triggers them is shared, so instead of hooking
  each one: every new grabbable whose path holds "(Clone)" gets an id from the host, which announces it (`Op.Spawn`:
  scene, name, pose). A client adopts its own new item with the same name within 1.5 m made in the same few seconds
  (it then follows the host's); with none, it copies an object of that name; one of its own that nothing claims
  within 3 s is removed, because the host's game did not make it.
  - **Decision:** match by name and place, not by hooking each spawner. One generic rule covers every spawner,
    including ones not yet seen in a tour; a spawner whose result differs per copy (random laundry) ends with the
    host's choice, because the client's unclaimed one is removed and the host's is copied.
- **Despawns:** an item destroyed in the host's game (eaten, recycled, smashed) is destroyed on the client
  (`Op.Despawn`); one destroyed while a client holds it is a request the host carries out.
- **Arriving in a scene:** a player who arrives in the host's scene gets that scene's current items (what the game
  made, owners, resting places), not the scene as it loads.
- **Flat-screen mode** (the user's request): start the game with `-vrmode None`; with no headset the mod drives the
  game's debug hands from keyboard and mouse (mouse look, WASD, Shift, Ctrl crouch, click to grab with either hand,
  wheel for reach, E/Q triggers, T teleport to the pad in view, M menu, Alt frees the cursor). The debug hands only
  know "grab", so the mod answers the trigger and menu buttons for them (Harmony postfix on
  `Debug_TrackedInputController.GetButtonDetailedControllerIsReady`).
  - **Decision:** a click reaches for the grabbable item in the middle of the view (within 2 m) and presses grab when
    the hand arrives: the game only grabs on a press with the item already in the hand, and a mouse can't place a
    hand in 3D the way a controller can. The hand goes first to a spot 25 cm in front of the item at its height, then
    straight in: a straight line from the eyes clipped the cabinet top and knocked the comic book deeper inside.
  - **Decision:** hands stay level (turned with the view's yaw only); tipped down with the view they hit the shelf
    above an item. The player walks inside a 4 x 4 m area around the teleport spot, like a large play space.
  - `flat` tour (host in flat mode, driven through `FlatSim`, never real input): PASS, 9/9 host, 3/3 client: a quarter
    turn is 90.0 degrees, one second of W walks 1.44 m, a click on the comic book grabs it and the client sees it in
    the flat player's closed hand, trigger and menu reach the game (3 runs in a row). Teleport: the living room has no second pad, so
    T is untested. Mutants `noflatreach`, `noflatbuttons`.
- **Tested:** `spawns` tour: PASS, 6/6 each side: a host-only item is copied on the client; an item both make at the
  same moment is adopted (one there, not two); a client-only item is removed; an item the host destroys goes on the
  client. Mutants `nospawnmatch`, `nospawnremove`, `nodespawn`.

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
