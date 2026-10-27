# Rick and Morty Together

Co-op for **Rick and Morty: Virtual Rick-ality** (Steam, VR). Up to 4 players share one game: everyone is a Morty
clone in the same garage, sees the others' heads and hands, and can pick things up, throw them and pass them from
hand to hand.

**Status: 0.1.0 pre-release, a first vertical slice.** Players, hands and grabbable items are shared. Story, missions,
buttons, levers and level changes are not shared yet: each player's game runs its own story for now (see
[What is shared](#what-is-shared)).

## Install

1. Download `RickAndMortyTogether-BepInEx-<version>.zip` from the
   [releases](https://github.com/multiplayereverything/rick-and-morty-coop/releases).
2. Unzip it into the game folder, next to `RickAndMortyVR.exe`
   (Steam: right-click the game > Manage > Browse local files).
3. Start the game from Steam as usual.

The ZIP contains BepInEx 5.4.23.5 and its `BepInEx/config/BepInEx.cfg`. Keep that config: this game's Unity version
needs BepInEx to start at the first scene load, and the default setting breaks the game's scenes. If you already use
BepInEx for this game, merge the `[Preloader.Entrypoint]` section into your config.

To remove the mod, delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx` folder.

## Play

Press **F5** on the desktop window (the mirror of your headset view) to open the panel. Set it up before you put the
headset on, or ask someone at the PC.

- **Host on Steam** opens a lobby (friends only by default, or public). Use **Invite Steam friends**, or friends pick
  **Join Game** on you in the Steam friends list.
- **Find public Steam games** lists public lobbies.
- **Host on LAN / direct IP** listens on TCP port 27040; friends type your IP (or `IP:port`) and press **Join IP**.

Everyone needs the same mod version; a different version is refused with both versions named.

## What is shared

| Part | Shared? | How |
| --- | --- | --- |
| Players | Yes | Each player is drawn as Morty: the game's Morty-clone head, a body under it, Morty hands. Head and hands follow the headset and controllers. |
| Hands | Yes | Position, rotation, open/closed grip, and what each hand holds. |
| Items you can grab | Yes | Pick up, carry, throw, hand to another player. The host decides who holds what; the last hand to grab wins, and the other hand lets go. |
| Buttons, levers, doors, machines | Not yet | Planned next. |
| Story, Rick's lines, missions, level changes | Not yet | Planned: the host's game drives the story and everyone follows. |
| Saves | Host only | A player in someone else's game keeps their own save untouched. |

## Known limits

- Steam P2P between two different accounts has not been tested in the lab yet (the lab runs on one account); LAN and
  the Steam lobby flow are tested.
- The Morty body is a simple shape under the real Morty head.
- Each player's story runs on its own until story sync lands, so players can end up in different rooms.

## For developers

See `AGENTS.md` (how the mod hooks the game, rules, lab), `docs/RECON.md` (how the game works), `MODLOG.md`
(changes and decisions) and `handoff.md` (current state).

Build: `scripts/build.ps1`. Lab: `scripts/setup-lab.ps1`, then `scripts/run-tour.ps1 -Tour handoff [-Steam] [-Lag 120]`
and `scripts/mutants.ps1`. Rules: `python tests/rules-test.py`.

Not affiliated with Adult Swim Games or Owlchemy Labs. No game files are included.
