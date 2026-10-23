using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Jewelcrafting's sealed fusion boxes fuse while a player carries them: once a minute if the player has moved (0.5 m
    // since the last minute that counted, 30 m on a ship), and when a boss dies within 50 m, FusionBoxSetup.
    // IncreaseBoxProgress adds progress at each box tier's rate (the "% / minute" shown on the box). But it gives it to
    // one sealed box in the inventory, picked at random, so with n of them each one fuses n times slower on average.
    // So: IncreaseBoxProgress gives every sealed box its rate, and every minute can count, moving or not. Only the game
    // of the player carrying them runs it.
    // Jewelcrafting's members are found by name: the ItemDataManager that reads a box from an item is internal, and
    // other mods bundle their own copy under the same name, so it's taken from Jewelcrafting's assembly.
    internal static class FusionBoxProgress
    {
        private const string JewelcraftingGuid = "org.bepinex.plugins.jewelcrafting";

        [HarmonyPatch]
        private static class AllBoxes
        {
            private static MethodBase _target;
            private static MethodInfo _itemInfo;
            private static MethodInfo _getBox;
            private static FieldInfo _sealed;
            private static MethodInfo _addProgress;
            private static Dictionary<GameObject, int> _boxTiers;

            // Jewelcrafting loads first when it's installed (a soft dependency of the plugin)
            private static bool Prepare()
            {
                if (_target != null)
                {
                    return true;
                }
                if (!Chainloader.PluginInfos.ContainsKey(JewelcraftingGuid))
                {
                    return false;
                }

                MethodInfo target = AccessTools.Method("Jewelcrafting.FusionBoxSetup:IncreaseBoxProgress", new[] { typeof(IEnumerable<float>) });
                Assembly assembly = target?.DeclaringType.Assembly;
                Type box = assembly?.GetType("Jewelcrafting.Box");
                Type extensions = assembly?.GetType("ItemDataManager.ItemExtensions");
                Type itemInfo = assembly?.GetType("ItemDataManager.ItemInfo");
                MethodInfo getData = itemInfo != null ? AccessTools.Method(itemInfo, "Get", new[] { typeof(string) }) : null;

                _itemInfo = extensions != null ? AccessTools.Method(extensions, "Data", new[] { typeof(ItemDrop.ItemData) }) : null;
                _getBox = box != null && getData != null && getData.IsGenericMethodDefinition ? getData.MakeGenericMethod(box) : null;
                _sealed = box != null ? AccessTools.Field(box, "boxSealed") : null;
                _addProgress = box != null ? AccessTools.Method(box, "AddProgress", new[] { typeof(float) }) : null;
                _boxTiers = target != null ? AccessTools.Field(target.DeclaringType, "boxTier")?.GetValue(null) as Dictionary<GameObject, int> : null;

                if (target == null || _itemInfo == null || _getBox == null || _sealed?.FieldType != typeof(bool) || _addProgress == null || _boxTiers == null)
                {
                    Plugin.Log.LogWarning("Jewelcrafting's fusion box progress wasn't found (a new version?), so it still goes to one carried box at a time.");
                    return false;
                }
                _target = target;
                return true;
            }

            private static MethodBase TargetMethod()
            {
                return _target;
            }

            // progress: the rate of each box tier, from Common to Celestial
            private static bool Prefix(IEnumerable<float> progress)
            {
                if (!Plugin.FuseAllBoxes)
                {
                    return true;
                }
                Player player = Player.m_localPlayer;
                if (player == null)
                {
                    return false;
                }

                float[] rates = progress.ToArray();
                foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                {
                    object box = _getBox.Invoke(_itemInfo.Invoke(null, new object[] { item }), new object[] { "" });
                    if (box != null && (bool)_sealed.GetValue(box) && item.m_dropPrefab != null &&
                        _boxTiers.TryGetValue(item.m_dropPrefab, out int tier) && tier < rates.Length)
                    {
                        _addProgress.Invoke(box, new object[] { rates[tier] });
                    }
                }
                return false;
            }
        }

        // The minute's check is one step of a coroutine (AdvanceBoxProgressByActivity.MeasureActivity, started on the
        // local player when it spawns), comparing the player's position with lastPos, where they were at the last minute
        // that counted. Moved out of reach before each step, the check passes, and Jewelcrafting sets it back to the
        // player's position, so with the setting off the next minute is checked as usual.
        [HarmonyPatch]
        private static class StandingStill
        {
            private static MethodBase _target;
            private static FieldInfo _lastPos;

            private static bool Prepare()
            {
                if (_target != null)
                {
                    return true;
                }
                if (!Chainloader.PluginInfos.ContainsKey(JewelcraftingGuid))
                {
                    return false;
                }

                Type activity = AccessTools.TypeByName("Jewelcrafting.FusionBoxSetup+AdvanceBoxProgressByActivity");
                MethodInfo measure = activity != null ? AccessTools.Method(activity, "MeasureActivity") : null;
                MethodBase target = measure != null ? AccessTools.EnumeratorMoveNext(measure) : null;
                _lastPos = activity != null ? AccessTools.Field(activity, "lastPos") : null;

                if (target == null || _lastPos?.FieldType != typeof(Vector3) || !_lastPos.IsStatic)
                {
                    Plugin.Log.LogWarning("Jewelcrafting's fusion box activity check wasn't found (a new version?), so boxes still only fuse while the player moves.");
                    return false;
                }
                _target = target;
                return true;
            }

            private static MethodBase TargetMethod()
            {
                return _target;
            }

            private static void Prefix()
            {
                if (Plugin.FuseWhileStill)
                {
                    _lastPos.SetValue(null, Vector3.positiveInfinity);
                }
            }
        }
    }
}
