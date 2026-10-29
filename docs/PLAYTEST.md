# Playtest checklist

The lab plays two copies of the game on one PC without a headset. Three things it cannot do need real players. Each
takes about 15 minutes. If anything on a list goes wrong, send `BepInEx/LogOutput.log` from each player's game folder
(the mod writes every connection, story event and sync decision there) and say which step it was.

## 1. Two headsets, two Steam accounts (the real thing)

Two PCs, both with the mod installed (README > Install), each on its own Steam account, friends with each other.

| Step | Expect |
| --- | --- |
| Player 1: F5 > Host on Steam (friends only), then Invite Steam friends | Player 2 gets a Steam invite |
| Player 2: accept the invite (or Join Game on Player 1 in the friends list) | Player 2's game joins; F5 shows both names and a ping |
| Look at each other | A floating Morty head with hands, the name above it, moving with the other player |
| Pick up the comic book in the living room and hand it over | The other player takes it out of your hand; it falls the same way for both |
| Open a cabinet door; press the DVD player's eject button | The door opens for both; eject happens for both |
| Player 1 starts the game from the DVD player (Play, then the controller's menu button for the tutorial) | Both fade out and arrive in the garage; Rick's first lines play once, for both, at the same time |
| Do the first garage tasks together | Rick's lines, items, machines and the progression stay the same for both |
| Player 2 tries to leave on their own (eat the menu to go back to the living room) | Nothing happens: only the host's game changes level |
| One player dies (the egg shoot-out, or poison) | Only that player goes to the death scene and comes back |
| Play a mission together through the portal | When the host switches the portal to another mission, the other player is brought along |
| The final boss | One fight for both; the death beam kills both, both come back |

Watch for: the fade between the living room and the garage (the lab never sees it), controller grip and trigger on
Index, Vive and Rift, and how the other player's head looks up close.

## 2. Over the internet, not LAN

Same as list 1, but the two PCs on different networks. Steam relays the connection; nothing to open on the router.
If joining fails, try F5 > Host on LAN with a port forward of TCP 27040 as a check, and send both logs.

## 3. Flat-screen mode with a real keyboard and mouse

Steam > the game > Properties > Launch options: `-vrmode None`. Start the game (no headset needed).

| Step | Expect |
| --- | --- |
| Move the mouse; WASD; Shift; Ctrl | Look around; walk; faster; crouch |
| Look at the comic book and hold left click | The right hand reaches for it and takes it; right click does the same with the left hand |
| Mouse wheel while holding something | It moves nearer or further |
| E with a gun or tool in hand | It fires or works |
| Alt | The mouse cursor is free (for the F5 panel) |
| In the garage, look at a teleport pad and press T | You teleport to it |
| Play with a friend in VR | They see you as the same Morty head and hands |

Remove the launch option to play in VR again.
