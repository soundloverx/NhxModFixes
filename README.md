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

## Sockets opening at chests

Opening a chest with the interact key while still holding it could open the sockets of an item in the middle of the screen instead (Jewelcrafting, with AzuEPI's equipment slots there), and leave the mouse stuck until the inventory was closed with the keyboard. With NhxModFixes, the key press that opens a chest doesn't open sockets, and holding the key stacks your items into the chest as usual. Once the key is released, pressing it over an item with sockets opens them as before.

## Aiming through tames

A tame following you can get between you and what you aim at: a flying one hovering over your head takes the interact key, and you pet it instead of opening the door or chest behind it. Press End to aim through tames: the interact key, the hover text and items used from the hotbar go to what's behind them. Press it again to pet, command, ride or protect your tames. It's for you only, and it's off each time the game starts.

## Tames keeping their distance

A tame following you walks right up to you and stops against you, and a flying one, like RtD's fairies, hovers over your head. With NhxModFixes, a following tame stays 3 m away when it isn't fighting, and a flying one stays beside you. If you walk up to it, it steps back, where there's room and only while you keep walking toward it: stop, and it stops too, close enough to pet it or tell it to stay.

## Ore deposits

Copper deposits, silver veins and big rocks are made of chunks, and when one breaks, the game drops every chunk that nothing holds up any more. It decides that with a box around each chunk, lined up with the world and often several times bigger than the chunk, so a deposit you've dug free can still count as touching the ground and stay hanging in the air. NhxModFixes turns each box to fit its chunk more closely. It helps most with long, diagonal chunks. A box still reaches past the rock in places, so a deposit can still hang now and then. It's always on.

## Getting rested

Resting by a fire, sitting or under a roof, makes you rested after 20 seconds in the game. With NhxModFixes it takes 5 seconds. How long the rested effect lasts doesn't change: it still depends on your comfort level.

## Fusion boxes

Jewelcrafting's sealed fusion boxes fuse while you carry them, at the rate shown on them for each minute, but only the minutes you've moved count, and each minute's progress goes to one of your boxes, picked at random: carry three, and each one takes three times as long. With NhxModFixes, every box you carry gets its rate every minute, whether you move or not, and a boss's fusion reward goes to every box too.

## Ships and tames

A sailing ship that runs into a creature damages it and itself, as if it hit a rock, but the game leaves players out. Tames weren't: RtD's fairies flying around a sailing player, or tames BetterTames teleports to them, onto the deck or into the hull, damaged the ship. With NhxModFixes, ships leave tames out too: neither the ship nor the tame takes damage. Rocks, the shore and wild creatures still damage the ship.

## RtD boss altars

RtDLegends' Fenrir King and Spider Queen altars stand inside their dungeons. Creature Level & Loot Control rebuilds a dungeon to refill it when someone goes in after its respawn time, and the rebuilt altar couldn't take over from the old one: offering the tokens did nothing on that player's game until they logged out and back in, or went far away and came back. With NhxModFixes, the rebuilt altar takes over, and summons the boss as usual.

## AzuCraftyBoxes chests

AzuCraftyBoxes keeps a list of the chests it can take materials from. A chest that loads and unloads while you're not crafting or building, like when you log in at your base and leave through a portal, stays on that list for the rest of the session, and every craft goes through it. With NhxModFixes, unloaded chests are taken off its list.

## Sending world updates

The game sends world updates in batches of at most 10 KB per round trip, counting what the other side hasn't confirmed yet: the server to each player, and each player's game to the server. When a lot changes, like while sailing into new areas or in a fight, updates pile up, players and creatures first, items last, which is likely why picking up items could take seconds after a long sail. With NhxModFixes, each connection's limit fits its ping and Steam's send rate: about 21 KB at 40 ms of ping, up to 64 KB.

## Collision garbage

Every time two things touch, Unity hands the game an object describing the contact, and the game has it make a new one each time: every character standing on the ground makes some 50 a second, and all of them are garbage a moment later. Cleaning up garbage shows as short pauses. With NhxModFixes, Unity reuses one object instead. Collisions work the same.

## Loading bases

Each building piece signs up with its zone's terrain when it loads, so that digging or raising the ground there checks the pieces again, and signs off when it unloads. Each time, the game copies the zone's whole list, so a zone with a few thousand pieces makes millions of copies when your base loads (coming home, logging in, through a portal) and again when it unloads. With NhxModFixes, each zone keeps its pieces in a set. Changing the terrain still checks the pieces as before.

## Mistlands mist

To place its mist, the game checks, for each particle, every mist area and wisp around, and asks Unity for their positions again each time: a few hundred particles per area, several times a second. With NhxModFixes, the positions are read once each time and each particle is only checked against the areas it can be in. The mist looks the same. It only matters while the mists are on.

## Unloading zones

While you travel, the game unloads the zones you've left behind, but first checks that nothing still stands in each one, by going through every object it has loaded. With NhxModFixes, it only looks closely at the objects near that zone.

## Dungeon fog, dust and mist

Inside a dungeon (burial chambers, troll caves, sunken crypts, frost caves, the Mistlands' mines, and the dungeons mods add), the game's fog is 10 to 60 times thicker than outdoors, and dust and big puffs of mist drift around you, lit by every torch, fire and spell nearby. NhxModFixes can thin the fog, and the dust and mist, for players who find it too much: "Dungeon fog" and "Dungeon dust and mist", in percent of the game's. They're each player's own, and the game's look by default.

## Steady torches

Torches, fires, braziers and lanterns flicker and wobble all the time, and the game's "Reduce flashing lights" option (Accessibility) leaves them alone: it only calms short flashes like lightning, explosions and spell hits. With "Steady torches" on, lasting lights shine steadily, everywhere. Each player's own, off by default.

## Fire and shields on tames

Fire and spirit damage set a creature burning, with flames, sparks and a flickering light scaled to its size, and shields (the Staff of Protection's, the ones RtD's fairies and pixies cast) wrap it in a glowing bubble that bursts when it's hit or breaks. A tame flying around your head, like RtD's fairies, can then cover most of the screen with fire or bubbles. With "Disable tamed on fire" on, burning tamed creatures are shown without their flames and light; with "Disable tamed shields" on, shielded tamed creatures are shown without their bubble, its bursts, and a fairy's burst when it casts a shield. Sounds, damage and what the shields absorb stay as they are, and your own shield still shows. Each player's own, off by default. The bursts of a shield on a tame another player's game controls are only hidden if that player also has this version.

## Session report

Every 5 minutes in a world, NhxModFixes writes a line to the log with the frame rate, memory use and garbage collections, how many objects the game holds, how long loading building pieces, the mist and unloading zones took, AzuCraftyBoxes' list, and each connection's ping, send window and backlog. It also logs item pickups that waited a second or more for another player's game. The server writes it too, in its own log.

## Settings

`NhxModFixes.cfg`:

| Section | Setting | Default | Description |
|----|----|----|----|
| RtD spawns | Max RtD monsters nearby | 3 | How many RtD monsters, all kinds together, can be around before more stop spawning. 0 removes the limit. |
| Collisions | Pass through tames | true | Players walk through tamed creatures. |
| Collisions | Pass through players | true | Players walk through each other. |
| Containers | Fix sockets opening at chests | true | The fix for sockets opening at chests. |
| Tames | Follow distance | 3 | How far, in meters, a following tame stays from its player, body to body. 0 for the game's behavior. |
| Tames | Step back when approached | true | A following tame steps back when its player walks up to it. |
| Tames | Ignore tames key | End | The key to aim through tames, or to stop. |
| Ships | No damage from tames | true | Ships take no damage from running into tamed creatures, and deal none to them. |
| Rested | Time to get rested | 5 | How many seconds of resting it takes to get rested. 20 for the game's timing. |
| Fusion boxes | Fuse all carried boxes | true | Every carried fusion box gets its progress, instead of one at random. |
| Fusion boxes | Fuse while standing still | true | Fusion boxes progress every minute, even when the player doesn't move. |
| Boss altars | Fix RtD altars | true | RtDLegends' dungeon altars (Fenrir King, Spider Queen) still summon their boss after their dungeon is rebuilt. |
| Containers | Forget unloaded chests | true | Unloaded chests are taken off AzuCraftyBoxes' list. |
| Network | Fit send window to ping | true | Each connection's send limit fits its ping and Steam's send rate, instead of the game's 10 KB. |
| Physics | Reuse collision objects | true | Unity reuses one object for the collisions it reports, instead of making a new one each time. |
| Bases | Fix base loading slowdown | true | Each zone keeps its building pieces in a set, so big bases load and unload faster. |
| Mist | Faster mist | true | The Mistlands' mist reads the mist areas' positions once and checks only the areas a particle can be in. |
| Zones | Faster zone unloading | true | Before unloading a zone, only the objects near it are looked at closely. |
| Dungeons | Dungeon fog | 100 | How thick the fog is inside dungeons, in percent of the game's. 0 removes it. |
| Dungeons | Dungeon dust and mist | 100 | How much of the dust and drifting mist inside dungeons is kept, in percent. 0 removes them. |
| Lights | Steady torches | false | Torches, fires, braziers, lanterns and other lasting lights stop flickering. |
| Tames | Disable tamed on fire | false | Burning tamed creatures are shown without their flames and light. |
| Tames | Disable tamed shields | false | Shielded tamed creatures are shown without their shield bubble and its bursts. |
| Diagnostics | Session report interval | 5 | Every this many minutes in a world, a session report in the log. 0 turns it off. |

The server's settings apply to every player, and only admins can change them, in game with the Configuration Manager (F1). A change to the server's config file applies after a restart. The exceptions are "Ignore tames key", "Dungeon fog", "Dungeon dust and mist", "Steady torches", "Disable tamed on fire", "Disable tamed shields" and "Session report interval": each game has its own, the server's included.

## Installation

The server and every player need it, at the same version. It needs Jotunn.
