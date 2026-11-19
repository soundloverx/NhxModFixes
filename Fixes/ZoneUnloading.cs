using HarmonyLib;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Every 0.1 s, ZoneSystem.UpdateTTL unloads a zone that's been out of range for 4 s, unless an object still stands
    // in it (ZNetScene.HaveInstanceInSector, its only caller): the game goes through every loaded object, reading each
    // one's position from Unity, until it finds one in that zone. While travelling, that's a pass over thousands of
    // objects for each zone left behind.
    // So objects whose network position (managed, kept up to date by whoever controls them) is more than a zone away
    // from it are skipped without asking Unity; the others get the game's check. An object can only be missed if its
    // body is over 64 m from where its controller last put it. Idea from ValheimPerformanceOptimizations, which looks
    // through the objects the game knows in the zones around instead (on a client, every object it was ever sent there).
    [HarmonyPatch]
    internal static class ZoneUnloading
    {
        private static int _checks;
        private static long _ticks;
        private static long _peakTicks;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.HaveInstanceInSector))]
        private static bool HaveInstanceInSector(ZNetScene __instance, Vector2s sector, ref bool __result, out long __state)
        {
            __state = Stopwatch.GetTimestamp();
            if (!Plugin.FasterZoneUnloading)
            {
                return true;
            }

            __result = false;
            foreach (KeyValuePair<ZDO, ZNetView> instance in __instance.m_instances)
            {
                Vector2s zone = ZoneSystem.GetZone(instance.Key.GetPosition());
                if (Mathf.Abs(zone.x - sector.x) > 1 || Mathf.Abs(zone.y - sector.y) > 1)
                {
                    continue;
                }
                // The game's check
                ZNetView view = instance.Value;
                if ((bool)view && !view.m_distant && ZoneSystem.GetZone(view.transform.position) == sector)
                {
                    __result = true;
                    break;
                }
            }
            return false;
        }

        // Timed with the setting on or off, for the report
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.HaveInstanceInSector))]
        private static void AfterCheck(long __state)
        {
            long ticks = Stopwatch.GetTimestamp() - __state;
            _checks++;
            _ticks += ticks;
            if (ticks > _peakTicks)
            {
                _peakTicks = ticks;
            }
        }

        internal static void Reset()
        {
            _checks = 0;
            _ticks = 0;
            _peakTicks = 0;
        }

        // For the session report: how long the checks took since the last one; null if no zone was unloaded
        internal static string Report()
        {
            if (_checks == 0)
            {
                return null;
            }
            double average = _ticks * 1000.0 / Stopwatch.Frequency / _checks;
            double peak = _peakTicks * 1000.0 / Stopwatch.Frequency;
            string text = $"zone unloading: {_checks} checks, {average:0.00} ms on average, {peak:0.00} ms at most";
            Reset();
            return text;
        }
    }
}
