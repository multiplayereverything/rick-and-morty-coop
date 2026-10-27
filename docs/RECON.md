# Recon: Rick and Morty: Virtual Rick-ality

What the game is made of, how it runs, and what each system becomes in co-op. File references are to the decompiled
`Assembly-CSharp` (kept in `private/decompiled`, never committed).

## The build

| Item | Value |
| --- | --- |
| Game | Rick and Morty: Virtual Rick-ality (Owlchemy Labs / Adult Swim Games, 2017) |
| Steam app id / build | 469610 / build 2288226 |
| Exe | `RickAndMortyVR.exe`, x64, SHA-256 `01678ebd02edec86258c482fbb5d00f3f0474e1a6515a905531f81df1a80ec50` |
| Engine | Unity 5.4.3p4, Mono, .NET 3.5 profile (`mscorlib` 2.0) |
| Code | `Assembly-CSharp.dll` (Owlchemy's `OwlchemyVR` framework, the "Brain" story graph), `Assembly-CSharp-firstpass.dll` (Steamworks.NET, SteamVR plugin, FinalIK, GoKit) |
| Steamworks | 2016 Steamworks.NET with `CSteamworks.dll`: lobbies, `ISteamNetworking` P2P (`SendP2PPacket`), no Steam Networking Sockets |
| VR | SteamVR (OpenVR) through Unity's VR support; Oculus SDK code is present but the Steam build always picks SteamVR (`VRPlatform.GetCurrVRPlatformType`) |
| Anti-cheat | none |
| Prior co-op work | none found (`search_directory` "Rick and Morty", "VR"; no Nexus/GitHub hits) |

## Hooking: BepInEx 5 on Unity 5.4

Unity 5.4 loads the game's scripts into a **child domain**. With BepInEx's default entrypoint (`Application..cctor`)
the chainloader runs in the middle of Unity's `MonoManager ReloadAssembly`, before `Assembly-CSharp` is loaded;
the plugin's GameObject is made too early and every scene logs "The referenced script on this Behaviour is
missing!" (SteamManager, the play-space selector and the rest stop working). Measured in the lab:

| Entrypoint | Result |
| --- | --- |
| default `Application..cctor`, plugin touching game types | every scene script missing |
| default, plugin with no game types | scripts fine, but the plugin's `Update` never runs and the game sometimes stalls in Loading |
| `SceneManager.Internal_SceneLoaded` | chainloader starts at the first scene load, after the game's scripts are bound: everything works |

So the release ships `BepInEx/config/BepInEx.cfg` with `[Preloader.Entrypoint] Type = SceneManager`,
`Method = Internal_SceneLoaded`. MelonLoader was not used: its Unity 5.4 support is unproven and BepInEx covers it.

## Running without a headset (the lab)

`-vrmode None` keeps Unity from loading OpenVR. The game then needs three repairs (all in `src/Headless.cs`, only
when no VR device is active):

1. `OwlchemyVRStorage.LoadCore` throws inside `VRPlatform.GetCurrVRPlatformHardwareType` (it reads
   `SteamVR.instance.hmd_TrackingSystemName`, and `SteamVR.instance` is null). The mod sets the platform fields and
   reruns `LoadCore`, which instantiates the normal `SteamVR_HMDAndInput` rig.
2. `PlaySpaceSizeSelector.DetermineSteamVRPlaySpace` waits for a calibrated chaperone forever. The mod picks the
   smallest SteamVR layout (2.0 x 1.5 m) and marks it complete.
3. `LoadGameWhenReady`'s fade coroutine died on the missing rig, so the mod loads `LivingRoom` itself.

The rig then runs in the game's own **debug mode** (`MasterHMDAndInputController.DebugModeEnabled`): each
`HandController` swaps its SteamVR controller for a `Debug_TrackedInputController` whose public `isGrabbing` drives
the grab button. The lab rig (`src/Rig.cs`) moves the head camera and both tracked hand transforms and sets
`isGrabbing`, so the game's real grab, joint and release code runs as if a player did it.

## The player

| Part | Where | Notes |
| --- | --- | --- |
| Rig | `MasterHMDAndInputController` (prefab per platform, `DontDestroyOnLoad`) | one per game; `Head`, `Hands[2]`, `HMDCamera` |
| Head model | `HeadControllerRAM(Clone)/CloneHeadDynamic/Face` under the HMD camera | the player is a Morty clone; this skinned head is the model mirrors and the spectator camera see |
| Hands | `HandController` -> `InteractionHandController` -> `HandRigidbody/.../AnimatedHandController/MortyHand` | the visual is Morty's hand; `Animator` states `HandOpen` / `HandClose` |
| Grab | `InteractionHandController.Update`: `GrabCustom` button down -> `GetClosestGrabbableItem` (colliders in the `GrabSphere` trigger) -> `GrabbableItem.Grab`, a `ConfigurableJoint` to the item | events `OnGrabSuccess`, `OnReleasedGrabbable`; a grab of an item in the other hand swaps it |
| Teleport | `TeleportationController.Teleport(zone)` sets the rig origin | one rig, one current zone |

## Story, levels and world (from the decompile)

- **Progression:** `ProgressionManager.SetStage` (linear stage list from `Resources/Progression/GameProgression`,
  stage names only in assets). Called from `DVDPlayerController` (`OnPlayButtonPushed`, `TutorialTime`) and the Brain
  (`BrainControlledModule_Progression`). A stage advance saves.
- **Brain:** a data-driven cause -> node -> effect graph. Every input is a world event string through
  `WorldEventsManager.EventOccurred*` -> `BrainManager.CheckCauseLookupDictionaryForAction`; effects run by
  reflection in `BrainManager.ExecuteEffectWithContext` on `BrainControlledModule_*` components (voice lines, Rick's
  animations and cinematics, scene activation, teleports, spawns).
- **Levels:** `LivingRoom` is a real scene (the DVD menu). Inserting a DVD and pressing Play calls
  `GlobalStorage.FadeAndLoadScene("RicksGarage")`. The garage and every mission (`Death`, `EggShootOut`, `Bathroom`,
  `PetriDish`, `Troy`, `Satellite`) are one multi-scene loaded additively by `MultiSceneController.LoadAllScenes`;
  moving between them is `SceneController.BeginDelayedActivation/Deactivation`, driven by the blast-shield portal
  selector, levers, buttons and the Brain.
- **Interactables:** `PushableButton.OnButtonPushed`, `GrabbableHinge` / `GrabbableSlider` lock events (driven
  remotely through `LimitedIndirectGrabbableController.LockUpper/LockLower/SetNormalizedAmount`),
  `PullCordController.OnPullCordPulled`, `LockingDoor.SetDoorLocked`. Scene objects have `UniqueObject` ids.
- **Spawns:** `BasePrefabSpawner.SpawnPrefab`, `ItemRespawner`, `CombinatorController.TryCombination` (identity = the
  two `WorldItemData.ItemName`s), `YouseeksBallController.SpawnYouseeks`, `LaundrySpawner` (random), Meeseeks box.
- **Death:** `DeathController.PlayerDeath` / `PlayerRespawn` (activates the Death scene, teleports).
- **Pause:** SteamVR dashboard focus only; it pauses audio, not time.
- **Save:** `GameStateController.SaveState` -> `SaveLoad.Save` to `persistentDataPath/RickAndMortySimulatorSave.save`
  (shared by every copy on one PC; the lab never reads or writes it).
- **Single-player assumptions:** 81 files read `OwlchemyVRStorage.Instance.MasterHMDAndInputController` (NPC head
  look and aim, `BodyPartDetectionZone` player zones, teleports and portals, enemies aiming at the HMD camera).

## Plan: who owns what

| System | Co-op model | Status |
| --- | --- | --- |
| Head pose | **Per-player**, streamed at 20 Hz; others draw a floating Morty head (the game's own clone head) with Morty hands | done (0.1.0; head only since 0.3.0) |
| Hands: pose, grip, held item | **Per-player**, in the same stream; remote hands open/close with the grip | done (0.1.0) |
| Grabbable items (pickups, throws, hand-offs) | **Host-shared** with ownership: host grants every grab claim (last grab wins), bumps an epoch, the previous holder lets go; the owner simulates and streams, everyone else follows kinematically | done (0.1.0) |
| Buttons, levers, hinges, sliders, pull cords, doors | **Host-shared**: levers/doors/sliders use item ownership and stream axis + lock state, followers driven with the game's own `Lock*/SetNormalizedAmount`; buttons and cords are events replayed by the host. Per-player: height and spectator levers | done (0.2.0) |
| Spawned items (combinator, respawners, Youseeks, laundry) | **Host-shared**: the host spawns and announces prefab + id; clients' own spawns are suppressed and replaced | next |
| Story (progression stage, Brain world events, VO, Rick's cinematics) | **Host-shared**: the host's Brain decides; its cause keys are replayed on clients, clients' causes become requests; stage by asset name; Brain dice seeded identically | done (0.3.0) |
| Level transitions (DVD -> garage, mission scene activation, portals) | **Host-shared**: announced as the host's fade starts; clients follow with `FadeAndLoadScene`, own loads held back; garage areas mirrored; teleport to the host's zone when the client's area goes off | done (0.3.0) |
| Death and respawn | **Per-player** death scene, host-shared respawn rules | later |
| NPC look-at, enemy aim, player zones | **Host-shared** with retargeting to the nearest player | later |
| Saves | **Host only** saves; a client's own save is never touched | done (0.1.0) |
| Haptics, spectator camera, play-space layout, comfort settings | **Per-player**, local only | out of scope |
| Oculus SDK path, PSVR | out of scope (Steam build only) | out of scope |

## Networking choice

- **Online:** Steam lobbies (friends-only or public, overlay invites, `+connect_lobby`, rich presence) through the
  game's own Steamworks under app 469610. The game's Steamworks.NET predates Steam Networking Sockets, so packets use
  `ISteamNetworking.SendP2PPacket` (reliable, channel 7), which falls back to Valve's relay like the newer API. No
  global relay.
- **LAN / direct:** length-prefixed TCP on port 27040 with keepalive frames from the writer thread (so a long scene load
  does not drop anyone; see the Baldi note on game-loop heartbeats).
- **Protocol check:** `Hello` carries the protocol version; a mismatch is refused with both versions in the message.
