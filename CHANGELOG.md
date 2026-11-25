# Changelog

## v1.6.0
- Works with the game's 1.0.30 update. v1.5.2 failed to load there (the game removed a method its build menu fix patched), so most of its fixes were off
- The build menu and portal fixes are gone: since 1.0.30, the game fixes both bugs itself. Settings "Fix build menu slowdown" (section "Build menu") and "Fix players left in portals" (section "Teleports") removed
- The session report no longer counts the build menu's list

## v1.5.2
- Tamed creatures that catch fire, or burn from spirit damage, can be shown without their flames and light, so a burning pet flying around your head (RtD's fairies) no longer covers the screen with fire. Their burning sound and damage stay. Setting "Disable tamed on fire" (section "Tames"), each player's own, off by default
- Shielded tamed creatures can be shown without their shield bubble (the Staff of Protection's, the ones RtD's fairies and pixies cast, other mods' shields), the bursts when it's hit or breaks, and a fairy's burst when it casts one. The shields still protect them, and your own shield still shows. The bursts of a tame another player's game controls are only hidden if that player also has this version. Setting "Disable tamed shields" (section "Tames"), each player's own, off by default

## v1.5.1
- Thinner fog inside dungeons, for players who want to see farther. Inside burial chambers, crypts, caves, mines and the dungeons mods add, the game's fog is 10 to 60 times thicker than outdoors. Setting "Dungeon fog" (section "Dungeons"), in percent of the game's, each player's own, 100 by default
- Less dust and mist inside dungeons. Dust and big puffs of mist drift around you there, lit by every torch, fire and spell nearby, so they glow and pulse. Setting "Dungeon dust and mist" (section "Dungeons"), in percent of the game's, each player's own, 100 by default
- Torches, fires, braziers, lanterns and other lasting lights can shine steadily instead of flickering and wobbling. The game's "Reduce flashing lights" option only calms short flashes (lightning, explosions, spell hits). Setting "Steady torches" (section "Lights"), each player's own, off by default

## v1.5.0
- Less garbage to clean up, so fewer short pauses: Unity reuses one object for the collisions it reports to the game, instead of making a new one for each (every character standing on the ground made some 50 a second). Setting "Reuse collision objects" (section "Physics"), on by default
- Big bases load and unload faster. Each building piece signed up with its zone's terrain when it loaded and signed off when it unloaded, and each time the game copied the zone's whole list: millions of copies for a zone with a few thousand pieces. Setting "Fix base loading slowdown" (section "Bases"), on by default
- The Mistlands' mist takes less time to work out. For each mist particle, the game checked every mist area and wisp around and asked Unity for their positions again each time. The mist looks the same. Setting "Faster mist" (section "Mist"), on by default
- Unloading the zones you leave behind while travelling takes less time. Before unloading each one, the game went through every object it had loaded to see whether one still stood there. Setting "Faster zone unloading" (section "Zones"), on by default
- The session report also shows the garbage collections, and how long loading building pieces, the mist and unloading zones took, to compare each setting on and off
- Boss kills are only shared for bosses. Bats also carry a kill key ("KilledBat"), so each bat killed was shared with the players around and logged a line

## v1.4.0
- The build menu no longer gets slower the longer you play. Every time it showed its pieces (opening it, picking a category, searching), the game added each button to a list again and never took it out. Setting "Fix build menu slowdown" (section "Build menu"), on by default
- AzuCraftyBoxes forgets chests that have unloaded. A chest that loaded and unloaded while you weren't crafting or building (logging in at a base and leaving through a portal, walking past a base) stayed on its list for the rest of the session, and every craft went through it. Setting "Forget unloaded chests" (section "Containers"), on by default
- World updates are sent faster when a lot changes (sailing, fights). The game sends at most 10 KB per round trip to each player and to the server, so updates pile up, which is likely why picking up items took seconds after a long sail. Each connection's limit now fits its ping and Steam's send rate (21 KB at 40 ms, up to 64 KB). Setting "Fit send window to ping" (section "Network"), on by default
- A session report in the log every 5 minutes: frame rate, memory, objects, the build menu's and AzuCraftyBoxes' lists, each connection's send window and backlog, and item pickups that waited for another player's game. Setting "Session report interval" (section "Diagnostics"), each game's own, 0 turns it off
- RtDLegends' altars inside dungeons (the Fenrir King's and the Spider Queen's) keep summoning their boss after Creature Level & Loot Control rebuilds their dungeon to refill it. The rebuilt altar couldn't take over from the old one, so offering the tokens did nothing until the player who went in logged out and back in. Setting "Fix RtD altars" (section "Boss altars"), on by default

## v1.3.3
- A sailing ship that runs into a tamed creature no longer damages it or itself, as the game already does for players. Tames around a sailing player (RtD's fairies, or tames BetterTames teleports to them) damaged the ship as if it hit a rock. Setting "No damage from tames" (section "Ships"), on by default

## v1.3.2
- Every sealed fusion box you carry (Jewelcrafting) fuses at its own rate. Jewelcrafting gave each minute's progress, and a boss's reward, to one of them at random, so with three boxes each one took three times as long. Setting "Fuse all carried boxes", on by default
- Fusion boxes progress every minute, even while you stand still (at a crafting station, by a fire), instead of only after you've moved. Setting "Fuse while standing still", on by default

## v1.3.1
- Resting by a fire makes you rested after 5 seconds instead of 20. Setting "Time to get rested", 20 for the game's timing

## v1.3.0
- Tames following a player keep a gap of 3 m from them when they aren't fighting, instead of walking right up to them, and flying ones (RtD's fairies) stay beside the player instead of hovering over their head. Setting "Follow distance", 0 for the game's behavior
- A following tame that a player walks up to steps back where there's room, and stops as soon as the player stops walking toward it, so walking up to it to pet or command it still works. Setting "Step back when approached", on by default

## v1.2.1
- Ore deposits made of chunks (copper, silver, big rocks) check what holds each chunk up with a box fitted to that chunk, instead of the game's box lined up with the world, which is often several times bigger. A deposit dug free hangs in the air less often, mostly with long, diagonal chunks. Always on, no setting

## v1.2.0
- A key to aim through tamed creatures, so a pet hovering over your head no longer takes the interact key meant for the door or chest behind it. Press it again to interact with tames. Setting "Ignore tames key", End by default, each player's own
- Tamed RtD creatures no longer count toward the shared RtD spawn limit, so a pet following you doesn't hold back the monsters around you
- RtD monsters inside dungeons no longer count toward the shared RtD spawn limit, so a dungeon full of them doesn't hold back the monsters outside
- Holding the interact key on a chest to stack your items into it no longer logs "released the mouse"

## v1.1.0
- Players walk through tamed creatures and through each other instead of being pushed around, which closed the crafting window when it happened at a crafting station. Settings "Pass through tames" and "Pass through players", both on by default
- Fixed a game bug where a player who went through a portal stayed visible to the others, standing in the portal, until they came back or the others moved away. Setting "Fix players left in portals", on by default
- Opening a chest while holding the interact key no longer opens the sockets of the item in the middle of the screen (Jewelcrafting with AzuEPI) and leaves the mouse stuck. Setting "Fix sockets opening at chests", on by default
- All settings are the server's, and only admins can change them

## v1.0.0
- Effects that mods share between players but don't register with the game (RtDLegends' blood splats, OdinsFoodBarrels' building effects) are found and registered, instead of filling the log with "Missing prefab hash" and causing lag spikes
- RtDLegends' and RtDMythics' monsters share one spawn limit: at most 3 of them around at once, by default
- A boss kill counts for every player who hit the boss or was within 100 m, not only the player whose game controlled it (Creature Level & Loot Control's world level reads it)
