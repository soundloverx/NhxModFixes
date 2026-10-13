using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // RtDLegends and RtDMythics add their monsters to the world's spawns through Jotunn, each kind with its own limit
    // (MaxSpawned), on top of the game's and of each other's. In the Black Forest at night that's a dozen kinds, all
    // allowed at once. This makes them share one limit: when that many of them are around (not counting tamed ones, nor
    // the ones inside dungeons), none of them spawns. Dungeons fill themselves with their own spawn points, which this
    // doesn't change.
    //
    // The game's spawner (SpawnSystem.UpdateSpawnList) asks GetNrOfZDOInstances how many of a kind are around, stops when
    // that reaches the kind's MaxSpawned, and spawns a group of at most MaxSpawned minus that. For an RtD kind, the answer
    // is raised so that both stay within the room left under the shared limit.
    [HarmonyPatch]
    internal static class RtdSpawnLimit
    {
        private static readonly HashSet<string> LimitedMods = new HashSet<string> { "soloredis.rtdlegends", "soloredis.rtdmythics" };

        // The RtD kinds sharing the limit, by the hash of their name (as ZDOs store it), with the highest MaxSpawned of
        // their spawn entries
        private static readonly Dictionary<int, int> MaxSpawnedByKind = new Dictionary<int, int>();
        private static readonly HashSet<SpawnSystemList> CheckedLists = new HashSet<SpawnSystemList>();

        // The spawner counts what's around once, before spawning anything, so it doesn't see what it spawned since
        private static int _spawnedThisRound;

        // Jotunn adds its spawn list in a prefix, so it's there by now. The lists are shared by every zone's spawner,
        // so each is only read once
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Awake))]
        [HarmonyPostfix]
        private static void FindRtdKinds(SpawnSystem __instance)
        {
            List<string> found = null;

            foreach (SpawnSystemList list in __instance.m_spawnLists)
            {
                if (list == null || !CheckedLists.Add(list))
                {
                    continue;
                }

                foreach (SpawnSystem.SpawnData spawn in list.m_spawners)
                {
                    if (spawn?.m_prefab == null)
                    {
                        continue;
                    }

                    CustomCreature creature = CreatureManager.Instance.GetCreature(spawn.m_prefab.name);
                    if (creature?.SourceMod == null || !LimitedMods.Contains(creature.SourceMod.GUID))
                    {
                        continue;
                    }

                    int hash = spawn.m_prefab.name.GetStableHashCode();
                    MaxSpawnedByKind.TryGetValue(hash, out int maxSpawned);
                    MaxSpawnedByKind[hash] = Math.Max(maxSpawned, spawn.m_maxSpawned);

                    // The game only counts what's around for entries with a MaxSpawned, so others can't be limited
                    if (spawn.m_maxSpawned <= 0)
                    {
                        Plugin.Log.LogWarning($"{spawn.m_prefab.name} spawns with no limit of its own, so it can't share the RtD limit.");
                    }

                    (found ?? (found = new List<string>())).Add(spawn.m_prefab.name);
                }
            }

            if (found != null)
            {
                Plugin.Log.LogInfo($"RtD monsters sharing the limit of {Plugin.MaxRtdMonsters}: {string.Join(", ", found.Distinct())}.");
            }
        }

        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.UpdateSpawnList))]
        [HarmonyPrefix]
        private static void StartRound()
        {
            _spawnedThisRound = 0;
        }

        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Spawn))]
        [HarmonyPostfix]
        private static void CountSpawn(SpawnSystem.SpawnData critter, bool eventSpawner)
        {
            if (!eventSpawner && critter?.m_prefab != null && MaxSpawnedByKind.ContainsKey(critter.m_prefab.name.GetStableHashCode()))
            {
                _spawnedThisRound++;
            }
        }

        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.GetNrOfZDOInstances))]
        [HarmonyPostfix]
        private static void ApplyLimit(GameObject prefab, List<ZDO> ZDOs, bool eventCreaturesOnly, ref int __result)
        {
            int limit = Plugin.MaxRtdMonsters;
            if (limit <= 0 || eventCreaturesOnly || prefab == null ||
                !MaxSpawnedByKind.TryGetValue(prefab.name.GetStableHashCode(), out int maxSpawned))
            {
                return;
            }

            // Tamed ones aren't counted: a pet following a player would hold back the monsters wherever they go. Nor are
            // the ones inside dungeons: a dungeon's inside is high above its entrance, in the same zones, so they'd hold
            // back the monsters outside (the game's own test for being inside, Character.InInterior)
            int around = _spawnedThisRound;
            foreach (ZDO zdo in ZDOs)
            {
                if (MaxSpawnedByKind.ContainsKey(zdo.GetPrefab()) && !zdo.GetBool(ZDOVars.s_tamed) &&
                    !Character.InInterior(zdo.GetPosition()))
                {
                    around++;
                }
            }

            int room = Math.Max(limit - around, 0);
            if (room == 0)
            {
                Plugin.Log.LogInfo($"Held back {prefab.name}: {around} RtD monsters are around (limit {limit}).");
            }

            // With no room, this reaches MaxSpawned and the spawner stops; otherwise the group is at most the room left
            __result = Math.Max(__result, maxSpawned - room);
        }
    }
}
