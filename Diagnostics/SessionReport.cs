using NhxModFixes.Fixes;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace NhxModFixes.Diagnostics
{
    // Every "Session report interval" minutes in a world, a log line with what could grow over a long session: the
    // frame rate, memory and garbage collections, how many objects the game knows (a client keeps every object it was
    // sent, unless it's destroyed, until it leaves the world, so this grows while exploring) and has loaded, the time
    // spent signing building pieces on and off their terrain and checking zones before unloading them, AzuCraftyBoxes'
    // lists, the mist's update time, the item pickups that waited for another game, then a line per
    // connection (ping, send window, backlog). The timings and counts cover the time since the last report (since
    // joining, for the first). On every game, the dedicated server included. Called from the plugin's Update.
    internal static class SessionReport
    {
        private const long MB = 1024 * 1024;

        private static ZNet _world;
        private static float _worldStart;
        private static float _lastReport;
        private static int _frames;
        private static int _collections;
        private static bool _failed;

        internal static void Update()
        {
            ZNet net = ZNet.instance;
            if (Plugin.ReportMinutes <= 0 || net == null || _failed)
            {
                _world = null;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (net != _world)
            {
                _world = net;
                _worldStart = now;
                _lastReport = now;
                _frames = 0;
                _collections = GC.CollectionCount(0);
                PickupWaits.Reset();
                PieceTerrainSubscriptions.Reset();
                FasterMist.Reset();
                ZoneUnloading.Reset();
                return;
            }

            _frames++;
            if (now - _lastReport < Plugin.ReportMinutes * 60f)
            {
                return;
            }
            try
            {
                Write(net, now);
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning($"The session report failed and is off until the game restarts: {e}");
            }
            _lastReport = now;
            _frames = 0;
        }

        private static void Write(ZNet net, float now)
        {
            int collections = GC.CollectionCount(0);
            List<string> parts = new List<string>
            {
                $"{_frames / (now - _lastReport):0} fps",
                $"memory {GC.GetTotalMemory(false) / MB} MB managed, {Profiler.GetTotalAllocatedMemoryLong() / MB} MB in all, {collections - _collections} garbage collections",
                $"{ZDOMan.instance?.m_objectsByID.Count ?? 0} objects known, {ZNetScene.instance?.m_instances.Count ?? 0} loaded",
            };
            _collections = collections;
            AddIfAny(parts, PieceTerrainSubscriptions.Report());
            AddIfAny(parts, ZoneUnloading.Report());
            if (!net.IsDedicated())
            {
                AddIfAny(parts, CraftyBoxesChests.Report());
                AddIfAny(parts, FasterMist.Report());
                parts.Add(PickupWaits.Report());
            }

            TimeSpan played = TimeSpan.FromSeconds(now - _worldStart);
            Plugin.Log.LogInfo($"Session report, {(int)played.TotalHours} h {played.Minutes:00} min in: {string.Join("; ", parts)}.");
            foreach (string line in SendWindow.Report())
            {
                Plugin.Log.LogInfo(line);
            }
        }

        private static void AddIfAny(List<string> parts, string part)
        {
            if (part != null)
            {
                parts.Add(part);
            }
        }
    }
}
