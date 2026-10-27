# Handoff

## State (2026-10-28, 0.6.0, protocol 6)

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

1. **Spawns in real play:** the `spawns` tour makes items with `Object.Instantiate`; watch a real combinator,
   dispenser and the laundry in the garage (a garage tour) for names or places that don't match.
2. **Mid-scene join:** a late joiner replays the scene's opening; send the Brain's running nodes or skip openings.
3. **Real headset check of the fade path** (the lab loads without the fade).
4. **NPC look-at / enemies:** retarget to the nearest player (`RickAnimationEventController`, `EnemyController`).
5. Friends' lobbies in the browser (Steam friends' game info), two-account Steam P2P test.

## Needs the user (headset, two accounts)

- A real headset session: controller grip/trigger mapping on Index/Vive/Rift (the lab only drives the debug grab
  button), comfort of the remote avatar, head model clipping.
- A two-account Steam P2P game (one account cannot P2P to itself in the lab).
