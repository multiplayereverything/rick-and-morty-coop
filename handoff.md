# Handoff

## State (2026-10-29, 0.7.1, protocol 7)

- Garage spawners: the computer's parts (tour `garage`); attached parts the game made stay each copy's own.

- Late joiners skip openings, characters look at the nearest player, flat-mode teleport works, slow-machine spawn
  matching (tours `latejoin`, `flat`, `spawns`). Real-player checks: `docs/PLAYTEST.md`.

- Enemies (`src/Enemies.cs`, tour `enemies`), per-player death (`src/Story.cs`, tour `death`), final boss state
  (`src/Boss.cs`, tour `boss`), every mission area played through together (tour `playthrough`).

- Spawns and despawns: host ids, client adopts / copies / removes (`src/Spawns.cs`, tour `spawns`, mutants
  `nospawnmatch`, `nospawnremove`, `nodespawn`).
- Flat-screen mode (`-vrmode None`): `src/Flat.cs`, tour `flat`, mutants `noflatreach`, `noflatbuttons`. Teleport (T)
  is untested: the living room has one pad; test it in the garage.

- Spectator mode: remote spectator cameras drawn, cycle visits other players' eyes (`src/Spectator.cs`, tour
  `spectator`, mutant `nospectatorview`).

- Story (Brain causes, stage), levels (scene loads, garage areas, zone follow), NPC dialogue/animation (seeded dice)
  sync: `src/Story.cs`, `src/Npcs.cs`, tour `story`, mutants `nocause`, `nostage`, `noarea`, `noscenefollow`.
- Remote players are head + hands only (the user's request); picture in `docs/remote-player.png`.

- Levers, doors, sliders, buttons and pull cords sync (`src/Controls.cs`, tour `controls`, mutants `nocontrol`,
  `nobutton`, `nograbkinematic`).

- Vertical slice done: two lab copies see each other's Morty avatar and hands, and an item passes hand to hand with
  host-granted ownership. Tours pass on LAN, with 120 ms lag, and through a Steam lobby; three mutants are caught.
- Recon and the co-op plan are in `docs/RECON.md`.

## Next, in order

0. **User report (2026-10-29, real play, screenshot from the spectator view):** in the starting intro, both Mortys are
   not at the location the intro puts the player. Check where each copy's rig starts in the intro (the intro's start
   pad / play-space origin) and where the remote Morty is drawn; the intro may have one player spot for two players.
1. **Attachments:** putting a part into the computer (or any attach point) is not synced; a part pulled out becomes
   synced. Watch the FixComputerTask with two players. Combinator results and the laundry (stage LaundryTask) are
   still unchecked in the lab.
2. **Mid-scene join, rest:** the joiner's timed banter trails the host's until the next shared story event; copy the
   Brain's running nodes if players notice.
3. **Real headset check of the fade path** (the lab loads without the fade).
4. **Enemies in real play:** mirrored enemies and nearest-player aim checked only in the lab.
5. Friends' lobbies in the browser (Steam friends' game info), two-account Steam P2P test.

## Needs the user (headset, two accounts)

- A real headset session: controller grip/trigger mapping on Index/Vive/Rift (the lab only drives the debug grab
  button), comfort of the remote avatar, head model clipping.
- A two-account Steam P2P game (one account cannot P2P to itself in the lab).
