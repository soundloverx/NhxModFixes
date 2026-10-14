# Changelog

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
