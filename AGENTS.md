# Rick and Morty Together development

Start with `handoff.md` (current state, next steps), `docs/RECON.md` (how the game works, the co-op plan) and
`README.md` (behaviour, limits). Keep all of them current along with `MODLOG.md`.

## How to work (Karpathy guidelines)

The user asked for these on every change to this mod. They favour care over speed; for a one-line fix, use judgment.

1. **Think before coding.** Write down your assumptions. When a request can be read two ways, name both. Say so when
   a simpler approach exists. This project's sessions don't ask the user questions: pick the reading you can defend,
   log it as a **Decision** in `MODLOG.md`, and say it in the report.
2. **Simplicity first.** Write the least code that does what was asked. Nothing speculative: no extra features, no
   settings nobody asked for, no abstraction with one caller, no handling for cases that can't happen. If 200 lines
   could be 50, rewrite them.
3. **Surgical changes.** Every changed line should trace to the request. Match the surrounding style. Don't reformat
   or refactor code that works. Remove what your own change left unused; mention other dead code, don't delete it.
4. **Goal-driven.** Turn each task into a check that can fail: a fix gets a lab tour check that reproduces the bug,
   then passes, plus its `Mutant("x")` switch (see Rules). For multi-step work, write the plan as steps, each with its
   check. "Done" means the check passed in the lab, with the evidence in `artifacts/tests/`.

## Layout
| Path | What |
| --- | --- |
| `src/` | The BepInEx plugin (one assembly, compiled against the game's .NET 3.5 assemblies) |
| `config/BepInEx.cfg` | Shipped config: entrypoint `SceneManager.Internal_SceneLoaded` (required on Unity 5.4) |
| `scripts/` | `build`, `setup-lab`, `launch-lab`, `run-tour`, `mutants`, `package`, `game-dir` |
| `tests/` | `rules-test.py` (source rules), `verify-lab.py` (judges a lab run) |
| `docs/` | `RECON.md`, panel screenshots |
| `private/` | decompiled game (`private/decompiled`), lab copies, lab logs. Never committed. |
| `deps/` | BepInEx 5.4.23.5 x64, ilspycmd. Never committed. |

## How the mod hooks the game
| Hook | Purpose |
| --- | --- |
| BepInEx entrypoint `SceneManager.Internal_SceneLoaded` | Start after Unity has bound the game's scripts (child domain on 5.4). |
| `InteractionHandController.OnGrabSuccess` / `OnReleasedGrabbable` (events, per local hand) | Grab claims and release tracking (`src/Items.cs`). |
| `InteractionHandController.TryRelease(false)` | The previous holder lets go when the host gives the item to someone else. |
| `LimitedIndirectGrabbableController` lock/unlock/`SetNormalizedAmount`, Harmony prefixes on its `LateUpdate`/`FixedUpdate` (`src/Controls.cs`) | Levers, doors and sliders: followers driven by the owner's stream. |
| `PushableButton.OnButtonPushed`, `PullCordController.OnPullCordPulled`; replay via the button's private `LockUpper/LockLower(bool)` | Buttons and cords fire on every copy. |
| `BrainManager.CheckCauseLookupDictionaryForAction(string)` and `(string, float)` (Harmony prefixes, `src/Story.cs`) | Story causes: host broadcasts, clients request. |
| `ProgressionManager.SetStage`, `SceneController.BeginDelayedActivation/Deactivation`, `GlobalStorage.FadeAndLoadScene` | Stage, garage areas and scene loads follow the host. |
| `BrainNodeStatusController.Execute`, `BrainControlledModule_Voice.DoInterruptLogicAndGetInterruptClip`, `BrainControlledModule_Core.WaitForRandomAmountOfTime` (`src/Npcs.cs`) | Seeded dice: the same NPC line on every copy. |
| `SpectatorModeLogic.CycleTarget` (prefix/postfix), `FindAndSetTarget` (postfix), `ExternalCameraController.SetTarget` (`src/Spectator.cs`) | Spectator cycle visits other players' eyes. |
| `Debug_TrackedInputController.GetButtonDetailedControllerIsReady` (postfix), debug hands' `isGrabbing`, HMD camera and tracked hand transforms (`src/Flat.cs`) | Flat-screen mode: keyboard and mouse drive the game's debug hands; trigger and menu buttons answered by the mod. |
| `EnemyManager.RunPhase1/RunPhase2`, `EnemyController.OnEnable` / `Hit` and its `playerHead` field (`src/Enemies.cs`) | Enemies run on the host; clients mirror them, send their hits, enemies aim at the nearest player. |
| `FinalBossController.SetState` (prefix/postfix, `src/Boss.cs`) | The final boss's state follows the host's. |
| `GrabbableItem.Start` (postfix, `src/Spawns.cs`) | New grabbables register at once instead of by searching the scene. |
| `SaveLoad.Save` / `SaveLoad.Load` (Harmony prefixes, `src/Patches.cs`) | Lab never touches the shared save; clients don't save. |
| `OwlchemyVRStorage.LoadCore`, `PlaySpaceSizeSelector`, `MasterHMDAndInputController.DebugModeEnabled` (reflection, `src/Headless.cs`) | Headless boot for the lab, only when no VR device is active. |
| `Debug_TrackedInputController.isGrabbing`, `HandController.TrackedTransform`, HMD camera transform (`src/Rig.cs`) | Lab rig: scripted head, hands and grab. |

## Networking
- **Wire:** `src/Wire.cs` (`Op`, `Wire.Version`), handlers in `TogetherRuntime.HostPacket` / `ClientPacket`
  (`src/Runtime.cs`). The host accepts only `Hello, Leave, Ping, Pose, ItemClaim, ItemState, ControlState, TriggerFired, Cause, Despawn, EnemyHit` from clients.
- **LAN / direct:** `src/Transport.cs`, TCP 27040, keepalives from the writer thread.
- **Online:** `src/SteamLobby.cs` (lobbies, invites, `+connect_lobby`), `src/SteamNet.cs` (`ISteamNetworking` P2P,
  channel 7). The game's Steamworks.NET has no Steam Networking Sockets.
- **Lab Steam:** `run-tour.ps1 -Steam` uses a real public lobby tagged with the run; data goes over loopback TCP
  because the copies share one Steam account.

## Repository
- **GitHub:** https://github.com/multiplayereverything/rick-and-morty-coop (public). Commit and push after each version.
- **Identity:** commits as `multiplayereverything <347356261+multiplayereverything@users.noreply.github.com>`.
- **Releases:** GitHub releases tagged `vX.Y.Z` (test builds are pre-releases) with
  `RickAndMortyTogether-BepInEx-<version>.zip` from `scripts/package.ps1`; the BepInEx loader has its own pinned
  `bepinex-5.4.23.5` release.
- **Before pushing:** `python tests/rules-test.py`; check `git ls-files` holds no game files, `private/` or `deps/`.

## Rules
- **Statics:** a new static field is cleared in a reset method or marked `// process-scoped`, `// call-scoped` or
  `// lab-only` on its line; an empty `catch` says why on its line. `tests/rules-test.py` enforces both.
- **Fixes:** every guarded fix gets a `Mutant("x")` switch and a line in `scripts/mutants.ps1` naming the tour that
  must fail without it (`rules-test.py` checks both ways).
- **Protocol:** bump `Wire.Version` on any wire change and log it in MODLOG (`## x.y.z (date, protocol N)`).
- **Lab:**
  - `scripts/setup-lab.ps1` makes `private/lab-host`, `lab-c1..c3`: hardlinked exe and `steam_api64.dll`, junctioned
    `RickAndMortyVR_Data` (never edit game bytes through it), BepInEx plus `config/BepInEx.cfg`, `steam_appid.txt`
    only there.
  - Copies start minimized (no activation) with a 480x270 restore spot at the right edge, BelowNormal, lowest quality,
    30 fps, `-vrmode None`. Never send keys or mouse input to the desktop. One tour at a time; other mods' labs share
    this PC.
  - Evidence: `private/lab-*/rick-and-morty-together-lab.log` (`RT-CHECK`, `RT-METRIC`, `RT-STATE`,
    `RT-MODERROR`), kept per tour in `artifacts/tests/logs/`. Unity logs in `private/lab-logs/`.
  - Budgets (`verify-lab.py`): no mod errors, game-error slope <= 0.5 per sample, heap slope <= 1.5 MB per sample,
    median mod cost <= 2 ms/frame, mod root objects <= 1 + peers + 2, desync <= 5 cm at rest and <= 50 cm moving.
  - Panel check without keystrokes: `-rt-panel` opens F5 and saves `panel-*.png` in the lab folder.
- **Process safety:** stop only PIDs in `private/lab-pids.txt` or processes whose exe is under `private/`.
- **Never distribute** game assemblies, `private/` or `deps/`.
