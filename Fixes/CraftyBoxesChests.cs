using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // AzuCraftyBoxes 1.8.28 keeps the chests it can pull materials from in Boxes.Containers. A chest that loads goes into
    // ContainersToAdd, moved into Containers only by FlushRegistryIfNeeded, which runs when it looks for chests (crafting
    // and building). Its removal (when a building piece is destroyed or unloads, skipped while pulling is off) only works
    // on a chest that's still there and already in Containers. So a chest that loads and unloads between two lookups
    // (walking past a base without crafting) is moved in afterwards, destroyed, and stays for the rest of the session,
    // with its inventory, and every lookup loops over it.
    // So before each flush (at most once a second), destroyed chests are removed from its three lists. Only the local
    // player's game uses them.
    [HarmonyPatch]
    internal static class CraftyBoxesChests
    {
        private const string AzuCraftyBoxesGuid = "Azumatt.AzuCraftyBoxes";
        private const float PruneInterval = 1f;

        private static readonly Predicate<Container> IsUnloaded = container => container == null;

        private static MethodBase _target;
        private static HashSet<Container> _listed;
        private static HashSet<Container> _toAdd;
        private static HashSet<Container> _toRemove;
        private static float _nextPrune;
        private static int _dropped;

        // AzuCraftyBoxes loads first when it's installed (a soft dependency of the plugin)
        private static bool Prepare()
        {
            if (_target != null)
            {
                return true;
            }
            if (!Chainloader.PluginInfos.TryGetValue(AzuCraftyBoxesGuid, out PluginInfo plugin) || plugin.Instance == null)
            {
                return false;
            }

            Type boxes = plugin.Instance.GetType().Assembly.GetType("AzuCraftyBoxes.Util.Functions.Boxes");
            MethodInfo flush = boxes != null ? AccessTools.Method(boxes, "FlushRegistryIfNeeded", Type.EmptyTypes) : null;
            _listed = Set(boxes, "Containers");
            _toAdd = Set(boxes, "ContainersToAdd");
            _toRemove = Set(boxes, "ContainersToRemove");

            if (flush == null || _listed == null || _toAdd == null || _toRemove == null)
            {
                Plugin.Log.LogWarning("AzuCraftyBoxes' chest list wasn't found (a new version?), so it still keeps unloaded chests.");
                return false;
            }
            _target = flush;
            return true;
        }

        private static HashSet<Container> Set(Type boxes, string name)
        {
            return boxes != null ? AccessTools.Field(boxes, name)?.GetValue(null) as HashSet<Container> : null;
        }

        private static MethodBase TargetMethod()
        {
            return _target;
        }

        private static void Prefix()
        {
            if (!Plugin.ForgetUnloadedChests || Time.time < _nextPrune)
            {
                return;
            }
            _nextPrune = Time.time + PruneInterval;

            _dropped += CountLost(out _);
            _listed.RemoveWhere(IsUnloaded);
            _toAdd.RemoveWhere(IsUnloaded);
            _toRemove.RemoveWhere(IsUnloaded);
        }

        // The unloaded chests AzuCraftyBoxes would keep: the ones waiting to be added, and the listed ones its removal
        // missed. Listed ones in ContainersToRemove are its own to remove at the flush (removed here a moment earlier).
        private static int CountLost(out int unloaded)
        {
            unloaded = 0;
            int lost = 0;
            foreach (Container container in _listed)
            {
                if (container == null)
                {
                    unloaded++;
                    if (!_toRemove.Contains(container))
                    {
                        lost++;
                    }
                }
            }
            foreach (Container container in _toAdd)
            {
                if (container == null)
                {
                    unloaded++;
                    lost++;
                }
            }
            return lost;
        }

        // For the session report; null without AzuCraftyBoxes. Fixed, the lost ones go at its next lookup, so they
        // stay near 0; otherwise they pile up.
        internal static string Report()
        {
            if (_target == null)
            {
                return null;
            }
            int lost = CountLost(out int unloaded);
            return $"AzuCraftyBoxes: {_listed.Count + _toAdd.Count} chests listed, {unloaded} unloaded ({lost} it lost track of), {_dropped} lost ones dropped so far";
        }
    }
}
