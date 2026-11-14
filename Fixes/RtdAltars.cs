using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections;

namespace NhxModFixes.Fixes
{
    // RtDLegends' Fenrir King and Spider Queen altars stand in dungeon rooms (MountainEntrance_Boss_RtD,
    // MistlandsEntrance_Boss_RtD). A dungeon's rooms are local objects, children of its DungeonGenerator, and these
    // altars have no ZNetView of their own: the game's altar (OfferingBowl.Start) registers its RPCs on the generator's,
    // which outlives them. CLLC rebuilds a dungeon to refill it when someone goes in through its door after its respawn
    // time (DungeonGenerator.Generate on that player's game, which destroys the rooms and places them again). The new
    // altar then can't register, its RPCs being still registered to the destroyed one (the game throws, and
    // ValheimCuisine hides it: "Suppressed duplicate RPC registration"), so offerings go to the destroyed altar and the
    // boss isn't summoned, until that game loads the dungeon again (relog, or travel away and back).
    // This removes the destroyed altar's RPCs just before the new altar registers its own. Offerings are handled by the
    // game controlling the generator, so it's done when the altar is rebuilt, on the game that rebuilt it, rather than
    // when someone offers, possibly on another game.
    [HarmonyPatch]
    internal static class RtdAltars
    {
        private const string RtdLegendsGuid = "soloredis.rtdlegends";

        // What OfferingBowl.Start registers
        private static readonly string[] Rpcs = { "RPC_SpawnBoss", "RPC_BossSpawnInitiated", "RPC_RemoveBossSpawnInventoryItems" };

        // RtDLegends loads first when it's installed (a soft dependency of the plugin)
        private static bool Prepare()
        {
            return Chainloader.PluginInfos.ContainsKey(RtdLegendsGuid);
        }

        [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Start))]
        [HarmonyPrefix]
        private static void TakeOverRpcs(OfferingBowl __instance)
        {
            // An altar with a ZNetView of its own gets a new one with it
            if (!Plugin.FixRtdAltars || __instance.GetComponent<ZNetView>() != null)
            {
                return;
            }

            ZNetView nview = __instance.GetComponentInParent<ZNetView>();
            OfferingBowl previous = nview != null ? RegisteredAltar(nview) : null;

            // Only from an altar that was destroyed (gone for Unity, still there for C#), not from another one still there
            if (previous is null || previous != null || !IsRtdAltar(__instance))
            {
                return;
            }

            foreach (string rpc in Rpcs)
            {
                nview.Unregister(rpc);
            }

            Plugin.Log.LogInfo($"The altar of {__instance.m_bossPrefab.name} was rebuilt with its dungeon: moved its summoning from the old altar to the new one.");
        }

        // The altar RPC_SpawnBoss is registered to on this ZNetView, if any. The game's RoutedMethod classes are
        // internal, so the ZNetView's table and the method's delegate are read by reflection
        private static OfferingBowl RegisteredAltar(ZNetView nview)
        {
            if (!(AccessTools.Field(typeof(ZNetView), "m_functions")?.GetValue(nview) is IDictionary functions))
            {
                return null;
            }

            // The non-generic indexer returns null for a missing key
            object method = functions[Rpcs[0].GetStableHashCode()];
            Delegate action = method == null ? null : AccessTools.Field(method.GetType(), "m_action")?.GetValue(method) as Delegate;
            return action?.Target as OfferingBowl;
        }

        // RtD's altars take RtD's items (tokens), which RtDLegends adds through Jotunn
        private static bool IsRtdAltar(OfferingBowl altar)
        {
            if (altar.m_bossItem == null || altar.m_bossPrefab == null)
            {
                return false;
            }

            CustomItem item = ItemManager.Instance.GetItem(altar.m_bossItem.name);
            return item?.SourceMod?.GUID == RtdLegendsGuid;
        }
    }
}
