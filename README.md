# NhxModFixes

A private mod for our server: fixes and tweaks for other mods, and for the game.

## Missing prefabs

Some mods have effects that the game shares with every player but that they never register with the game, like RtDLegends' blood splats and OdinsFoodBarrels' building effects. When another player's game shows one, yours can't find it, and fills the log with "Missing prefab hash" warnings for as long as it lasts, which shows as a lag spike. NhxModFixes finds these effects among what the mods have loaded and registers them, so you see them too.

## RtD spawns

RtDLegends and RtDMythics add their monsters to the world's spawns, each kind with its own limit, on top of the game's. At night in the Black Forest, that's a dozen kinds at once. NhxModFixes makes them share one limit: when 3 of them are around (within about 150 m), no more spawn until there are fewer. Tamed ones don't count, so a pet following you doesn't hold back the monsters, and neither do the ones inside dungeons, so a crypt full of them doesn't hold back the monsters outside. The game's own monsters, and the monsters in dungeons, are not affected.

## Boss kills for everyone

When a boss dies, the game records it in only one player's character: the one whose game was controlling the boss. Everyone gets the kill in their stats and the achievement, but mods that read the bosses a player has beaten, like Creature Level & Loot Control's world level, treat the others as if they hadn't. NhxModFixes gives the boss to every player who hit it or was within 100 m when it died.

## Walking through tames and players

Tamed creatures and other players push you around when they walk into you, and at a crafting station, being pushed a step away closes its window. With NhxModFixes, players walk through tamed creatures (pets, mounts, summons) and through each other. Attacks still hit them as before, but you can no longer stand on a lox's back or on another player.

## Players left in portals

Because of a bug in the game's server, a player who goes through a portal can stay visible to the others, standing in it and doing what they do on the other side, with monsters attacking them for nothing, until they come back or the others move away. NhxModFixes fixes the server, so the others' games remove them once they've left.

## Sockets opening at chests

Opening a chest with the interact key while still holding it could open the sockets of an item in the middle of the screen instead (Jewelcrafting, with AzuEPI's equipment slots there), and leave the mouse stuck until the inventory was closed with the keyboard. With NhxModFixes, the key press that opens a chest doesn't open sockets, and holding the key stacks your items into the chest as usual. Once the key is released, pressing it over an item with sockets opens them as before.

## Aiming through tames

A tame following you can get between you and what you aim at: a flying one hovering over your head takes the interact key, and you pet it instead of opening the door or chest behind it. Press End to aim through tames: the interact key, the hover text and items used from the hotbar go to what's behind them. Press it again to pet, command, ride or protect your tames. It's for you only, and it's off each time the game starts.

## Settings

`NhxModFixes.cfg`:

| Section | Setting | Default | Description |
|----|----|----|----|
| RtD spawns | Max RtD monsters nearby | 3 | How many RtD monsters, all kinds together, can be around before more stop spawning. 0 removes the limit. |
| Collisions | Pass through tames | true | Players walk through tamed creatures. |
| Collisions | Pass through players | true | Players walk through each other. |
| Teleports | Fix players left in portals | true | The fix for players left in portals. Only the server uses it. |
| Containers | Fix sockets opening at chests | true | The fix for sockets opening at chests. |
| Tames | Ignore tames key | End | The key to aim through tames, or to stop. |

The server's settings apply to every player, and only admins can change them, in game with the Configuration Manager (F1). A change to the server's config file applies after a restart. The exception is "Ignore tames key": each player sets their own.

## Installation

The server and every player need it, at the same version. It needs Jotunn.
