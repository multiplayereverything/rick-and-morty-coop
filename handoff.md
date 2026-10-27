# Handoff

## State (2026-10-27, 0.1.0, protocol 1)

- Vertical slice done: two lab copies see each other's Morty avatar and hands, and an item passes hand to hand with
  host-granted ownership. Tours pass on LAN, with 120 ms lag, and through a Steam lobby; three mutants are caught.
- Recon and the co-op plan are in `docs/RECON.md`.

## Next, in order

1. **World interactables:** sync `PushableButton`, `GrabbableHinge`, `GrabbableSlider` lock events, `PullCordController`
   and `LockingDoor` as events (host decides, clients replay with `LockUpper/LockLower/SetNormalizedAmount`). Tour:
   one player pulls a lever, the other sees it locked.
2. **Level transitions:** the DVD in `LivingRoom` -> `GlobalStorage.FadeAndLoadScene("RicksGarage")` on the host
   first, clients follow; mission scene activation (`SceneController.BeginDelayedActivation`) mirrored; teleport
   remote players to the host's zone.
3. **Story:** host runs the Brain; clients replay `BrainManager.CheckCauseLookupDictionaryForAction` keys and
   `ProgressionManager.SetStage`; clients' own Brain inputs become requests. Watch VO duplication.
4. **Spawns:** combinator results, respawners, Youseeks, laundry (random prefab: host picks).
5. **NPC look-at / enemies:** retarget to the nearest player (`RickAnimationEventController`, `EnemyController`).
6. **Morty body:** harvest the full Morty model from the garage scene instead of the primitive body.
7. Friends' lobbies in the browser (Steam friends' game info), two-account Steam P2P test.

## Needs the user (headset, two accounts)

- A real headset session: controller grip/trigger mapping on Index/Vive/Rift (the lab only drives the debug grab
  button), comfort of the remote avatar, head model clipping.
- A two-account Steam P2P game (one account cannot P2P to itself in the lab).
