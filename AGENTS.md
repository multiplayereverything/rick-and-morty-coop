# Rick and Morty Together development

Start with `handoff.md` (current state, next steps), `docs/RECON.md` (how the game works, the co-op plan) and
`README.md` (behaviour, limits). Keep all of them current along with `MODLOG.md`.

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
| `SaveLoad.Save` / `SaveLoad.Load` (Harmony prefixes, `src/Patches.cs`) | Lab never touches the shared save; clients don't save. |
| `OwlchemyVRStorage.LoadCore`, `PlaySpaceSizeSelector`, `MasterHMDAndInputController.DebugModeEnabled` (reflection, `src/Headless.cs`) | Headless boot for the lab, only when no VR device is active. |
| `Debug_TrackedInputController.isGrabbing`, `HandController.TrackedTransform`, HMD camera transform (`src/Rig.cs`) | Lab rig: scripted head, hands and grab. |

## Networking
- **Wire:** `src/Wire.cs` (`Op`, `Wire.Version`), handlers in `TogetherRuntime.HostPacket` / `ClientPacket`
  (`src/Runtime.cs`). The host accepts only `Hello, Leave, Ping, Pose, ItemClaim, ItemState` from clients.
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
