using HarmonyLib;

namespace NhxModFixes.Fixes
{
    // After a player goes through a portal, the others can keep seeing them standing in it, playing the animations of
    // what they do on the other side, with monsters attacking them for nothing.
    // Each game keeps its own copy of the objects around it (ZDOs), and the server only sends each player the ones in
    // their area. When one leaves a player's area, the server tells that player's game to drop it (ZDOPeer.
    // ZDOSectorInvalidated, run when a ZDO's zone changes), but it checks whether the ZDO is still in the area against its
    // position before the move: ZDO.InternalSetPosition changes the zone first and the position after. Walking away
    // goes through the outer ring of what a game shows (2 zones away by default), which is already outside the area the
    // check uses (1.5 zones), so that works. A teleport jumps from the middle to far away: the check sees the old
    // position, inside, so the copy stays where it was and the server stops sending it updates. The animations still
    // play because the game sends them to every player wherever they are, and the hits don't land because the
    // teleported player's game ignores damage from attackers it doesn't have.
    // This runs the check after the position has changed. It only matters on the server, the only one that runs it.
    // Off ("Fix players left in portals"), the check runs where the game puts it.
    [HarmonyPatch]
    internal static class TeleportGhosts
    {
        // While ZDO.InternalSetPosition runs: the ZDO the game wanted to check, if its zone changed
        private static bool _moving;
        private static ZDO _zoneChanged;

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.InternalSetPosition))]
        [HarmonyPrefix]
        private static void BeforeMove()
        {
            _moving = Plugin.FixTeleportGhosts;
            _zoneChanged = null;
        }

        // Only called from ZDO.SetSector, on the server, when a ZDO's zone changes (not for portals)
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ZDOSectorInvalidated))]
        [HarmonyPrefix]
        private static bool DelayCheck(ZDO zdo)
        {
            if (!_moving)
            {
                return true;
            }

            _zoneChanged = zdo;
            return false;
        }

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.InternalSetPosition))]
        [HarmonyPostfix]
        private static void AfterMove()
        {
            _moving = false;
            if (_zoneChanged == null)
            {
                return;
            }

            ZDO zdo = _zoneChanged;
            _zoneChanged = null;
            ZDOMan.instance.ZDOSectorInvalidated(zdo);
        }
    }
}
