# Rick and Morty Together

Co-op for **Rick and Morty: Virtual Rick-ality** (Steam, VR). Two players share one game: both are Morty
clones in the same garage, see each other's head and hands, and can pick things up, throw them and pass them from
hand to hand.

**Status: 0.9.0 pre-release.** Players, hands, grabbable items, doors, levers, sliders, buttons, the story, level
changes, and Rick's and Morty's lines and animations are shared, with the host's game in charge (see
[What is shared](#what-is-shared)). Things the game makes or destroys while you play are shared too.

## Install

If the Multiplayer Everything launcher lists this mod, it installs both parts for you. By hand:

1. Open the game folder (Steam: right-click the game > Manage > Browse local files).
2. Unzip `BepInEx_win_x64_5.4.23.5.zip` from the
   [BepInEx 5.4.23.5 loader release](https://github.com/multiplayereverything/rick-and-morty-coop/releases/tag/bepinex-5.4.23.5)
   there, next to `RickAndMortyVR.exe`.
3. Unzip `RickAndMortyTogether-BepInEx-<version>.zip` from the
   [releases](https://github.com/multiplayereverything/rick-and-morty-coop/releases) on top, and let it replace
   `BepInEx/config/BepInEx.cfg`.
4. Start the game from Steam as usual.

Keep the mod's `BepInEx.cfg`: this game's Unity version needs BepInEx to start at the first scene load, and the
default setting breaks the game's scenes. If you have your own BepInEx config for this game, copy its
`[Preloader.Entrypoint]` section instead.

To remove the mod, delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx` folder.

## Play

Press **F5** on the desktop window (the mirror of your headset view) to open the panel. Set it up before you put the
headset on, or ask someone at the PC.

- **Host on Steam** opens a lobby (friends only by default, or public). Use **Invite Steam friends**, or friends pick
  **Join Game** on you in the Steam friends list.
- **Find public Steam games** lists public lobbies.
- **Host on LAN / direct IP** listens on TCP port 27040; friends type your IP (or `IP:port`) and press **Join IP**.

Join before the story starts: while the host is still in the living room (the DVD menu). Once the host has gone into
the garage, a new player is refused with "The game has started. Join while the host is in the living room (the DVD
menu)." A third player is refused with "The game is full (2 players)." Everyone needs the same mod version; a different
version is refused with both versions named.

## Play without a headset (flat-screen mode)

In Steam: right-click the game > Properties > Launch options, type `-vrmode None`, and start the game. With no headset
running, the mod switches to keyboard and mouse:

| Key | Does |
| --- | --- |
| Mouse | Look around |
| W A S D, Shift | Walk, walk faster |
| Ctrl | Crouch |
| Left click / right click (hold) | Grab with the right / left hand |
| Mouse wheel | Reach nearer or further |
| E / Q | Right / left hand trigger (fire, use) |
| T | Teleport to the pad you are looking at |
| M | Menu button (the DVD tutorial's "PRESS IT") |
| Alt (hold) | Free the mouse cursor |
| F5 | The co-op panel |

A flat-screen player and a headset player can play together; the other player sees the same Morty head and hands.
Remove the launch option to play in VR again.

## What is shared

| Part | Shared? | How |
| --- | --- | --- |
| Players | Yes | Each player is a floating Morty head with Morty hands (the game's own Morty-clone head), following the headset and controllers, with their name above. |
| Hands | Yes | Position, rotation, open/closed grip, and what each hand holds. |
| Items you can grab | Yes | Pick up, carry, throw, hand to another player. The host decides who holds what; the last hand to grab wins, and the other hand lets go. |
| Doors, levers, drawers, sliders | Yes | Whoever holds one moves it for everyone; when it clicks into place (locks), it locks for everyone and the game reacts on every player's side. Your own height lever and spectator-camera lever stay yours. |
| Buttons, pull cords | Yes | A press by anyone is a press for everyone. The DVD player's Play button waits for level sync. |
| Level changes | Yes | Everyone goes where the host goes: the living room (DVD menu), the garage, and each mission area. You can't leave on your own while in someone else's game. |
| Story | Yes | The host's game decides what happened; anything you do that moves the story is sent to the host and happens for everyone. |
| Rick's and Morty's lines and animations | Yes | Same line at the same moment for everyone, including the game's random choices. |
| Spectator mode | Yes | If someone's spectator camera is out in the room, everyone sees it there. "Cycle cameras" also lets your desktop window watch through each other player's eyes. |
| Enemies | Yes | The host's game runs them; both players see the same enemies at the same places, either player can shoot them, and they shoot at whoever is nearest. |
| Final boss | Yes | One boss fight for both; when it fires its death beam, both players die and come back. |
| Dying | Your own | You go to your own death scene and answer your own death phone to come back; the other player plays on (or, in the intro's scripted death, answers theirs). After you come back, Rick's and Morty's next lines may run a little behind the other player's until the next story event. |
| Rick's smartwatch | Yes | One player wears it (on their own wrist); the others see it on that player's hand, and everyone hears Rick's calls. |
| Parts in sockets | Yes | Putting a part into something (the computer's RAM stick, crystal and fuse, or any other socket) or taking it out happens for everyone, and the game reacts on every player's side. |
| Things the game makes or destroys | Yes | Machine results, dispenser drops and the like exist once, in the host's game, for both players; something eaten or smashed goes for both. |
| Saves | Host only | A player in someone else's game keeps their own save untouched. |

## Known limits

- Steam P2P between two different accounts has not been tested in the lab yet (the lab runs on one account); LAN and
  the Steam lobby flow are tested.
- Joining is only possible before the story starts (see Play); a player who drops out mid-game can rejoin only after the
  host goes back to the living room.
- Not yet checked with real headsets on two Steam accounts or with a real keyboard and mouse: see
  `docs/PLAYTEST.md` for the 15-minute checks.

## For developers

See `AGENTS.md` (how the mod hooks the game, rules, lab), `docs/RECON.md` (how the game works), `MODLOG.md`
(changes and decisions) and `handoff.md` (current state).

Build: `scripts/build.ps1`. Lab: `scripts/setup-lab.ps1`, then `scripts/run-tour.ps1 -Tour handoff [-Steam] [-Lag 120]`
and `scripts/mutants.ps1`. Rules: `python tests/rules-test.py`.

Not affiliated with Adult Swim Games or Owlchemy Labs. No game files are included.
