using HarmonyLib;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // The Mistlands' mist (ParticleMist.Update, every 5 frames: it adds fixedDeltaTime each frame up to 0.1 s) places
    // particles around the player, the demisters (wisps) and the edge of each mist area (Mister) within 100 m, a few
    // hundred per area. For each particle it checks the mist areas (Mister.InsideMister, IsInsideOtherMister: every
    // one of them) and the demisters (IsInsideOtherDemister), reading each one's position from Unity again every time,
    // and a demister's range too. That adds up to milliseconds per update.
    // So, during that update only, the positions, sizes and ranges are read once (when it sorts the areas and lists
    // the demisters), the checks use them, and the particles of an area's edge are only checked against the areas
    // that can contain them (the ones overlapping its edge). Same results: nothing moves during the update. Other
    // calls (sounds, creatures' sight, FarLands from its own update) go to the game's code. Idea from
    // ValheimPerformanceOptimizations.
    [HarmonyPatch]
    internal static class FasterMist
    {
        // Float tolerance for the overlap tests, in meters
        private const float Slack = 0.01f;
        // The game places an edge particle up to the area's radius + 0.1 m + the thickness from its center
        private const float EdgeOffset = 0.1f;

        private sealed class ByReference : IEqualityComparer<Mister>
        {
            public bool Equals(Mister x, Mister y) => ReferenceEquals(x, y);
            public int GetHashCode(Mister obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private static bool _inUpdate;
        private static bool _worked;
        private static long _start;

        // The mist areas, in the game's order (by distance to the player)
        private static bool _haveAreas;
        private static int _areaCount;
        private static Mister[] _areas = new Mister[64];
        private static Vector3[] _position = new Vector3[64];
        private static float[] _radius = new float[64];
        private static float[] _height = new float[64];
        private static float[] _top = new float[64];
        private static readonly Dictionary<Mister, int> Index = new Dictionary<Mister, int>(new ByReference());

        // The areas overlapping the edge of the area whose particles are being placed
        private static Mister _edgeOf;
        private static int _edgeIndex;
        private static float _edgeReach;
        private static int[] _overlapping = new int[64];
        private static int _overlapCount;

        // The demisters, in the list the update passes around
        private static List<Demister> _fieldList;
        private static int _fieldCount;
        private static Demister[] _fields = new Demister[16];
        private static Vector3[] _fieldPosition = new Vector3[16];
        private static float[] _fieldRange = new float[16];

        private static int _updates;
        private static long _ticks;
        private static long _peakTicks;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(ParticleMist), nameof(ParticleMist.Update))]
        private static void BeforeUpdate()
        {
            _inUpdate = true;
            _worked = false;
            _start = Stopwatch.GetTimestamp();
        }

        // Also when another mod's prefix skips the update (NhxMistToggle with the mists lifted, Server Devcommands)
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ParticleMist), nameof(ParticleMist.Update))]
        private static void AfterUpdate()
        {
            if (_worked)
            {
                long ticks = Stopwatch.GetTimestamp() - _start;
                _updates++;
                _ticks += ticks;
                if (ticks > _peakTicks)
                {
                    _peakTicks = ticks;
                }
            }
            _inUpdate = false;
            _worked = false;
            _edgeOf = null;
            if (_haveAreas)
            {
                System.Array.Clear(_areas, 0, _areaCount);
                Index.Clear();
                _haveAreas = false;
            }
            if (_fieldList != null)
            {
                System.Array.Clear(_fields, 0, _fieldCount);
                _fieldList = null;
            }
        }

        // The update's first step once the mist is worked out: the areas, sorted
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Mister), nameof(Mister.GetDemistersSorted))]
        private static void AfterSorting(List<Mister> __result)
        {
            if (!_inUpdate)
            {
                return;
            }
            _worked = true;
            if (!Plugin.FasterMist || __result == null)
            {
                return;
            }

            int count = __result.Count;
            if (_areas.Length < count)
            {
                int size = Mathf.NextPowerOfTwo(count);
                _areas = new Mister[size];
                _position = new Vector3[size];
                _radius = new float[size];
                _height = new float[size];
                _top = new float[size];
                _overlapping = new int[size];
            }
            Index.Clear();
            for (int i = 0; i < count; i++)
            {
                Mister area = __result[i];
                Vector3 position = area.transform.position;
                _areas[i] = area;
                _position[i] = position;
                _radius[i] = area.m_radius;
                _height[i] = area.m_height;
                // As the game adds them
                _top[i] = position.y + area.m_height;
                Index[area] = i;
            }
            _areaCount = count;
            _haveAreas = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ParticleMist), nameof(ParticleMist.GetAllForcefields))]
        private static void AfterListingDemisters(List<Demister> fields)
        {
            _fieldList = null;
            if (!_inUpdate || !Plugin.FasterMist || fields == null)
            {
                return;
            }

            int count = fields.Count;
            if (_fields.Length < count)
            {
                int size = Mathf.NextPowerOfTwo(count);
                _fields = new Demister[size];
                _fieldPosition = new Vector3[size];
                _fieldRange = new float[size];
            }
            for (int i = 0; i < count; i++)
            {
                Demister field = fields[i];
                if (field == null || field.m_forceField == null)
                {
                    // Left to the game's code
                    System.Array.Clear(_fields, 0, i);
                    return;
                }
                _fields[i] = field;
                _fieldPosition[i] = field.transform.position;
                _fieldRange[i] = field.m_forceField.endRange;
            }
            _fieldCount = count;
            _fieldList = fields;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Mister), nameof(Mister.InsideMister))]
        private static bool InsideMister(Vector3 p, float radius, ref bool __result)
        {
            if (!_haveAreas)
            {
                return true;
            }
            for (int i = 0; i < _areaCount; i++)
            {
                if (Vector3.Distance(_position[i], p) < _radius[i] + radius && p.y - radius < _top[i])
                {
                    __result = true;
                    return false;
                }
            }
            __result = false;
            return false;
        }

        // Also lists the areas overlapping this one's edge, for its particles' checks that follow
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Mister), nameof(Mister.IsCompletelyInsideOtherMister))]
        private static bool IsCompletelyInsideOtherMister(Mister __instance, float thickness, ref bool __result)
        {
            _edgeOf = null;
            if (!_haveAreas || !Index.TryGetValue(__instance, out int self))
            {
                return true;
            }

            Vector3 position = _position[self];
            float radius = _radius[self];
            float top = _top[self];
            // Farthest an edge particle can be from the center: the game raises none, but lowers those above the
            // area's top to it, which only moves them away from the center if the area's height is negative
            float reach = radius + EdgeOffset + thickness + Mathf.Max(0f, -_height[self]) + Slack;
            _overlapCount = 0;
            for (int i = 0; i < _areaCount; i++)
            {
                if (ReferenceEquals(_areas[i], __instance))
                {
                    continue;
                }
                float distance = Vector3.Distance(position, _position[i]);
                if (distance + radius + thickness < _radius[i] && top < _top[i])
                {
                    __result = true;
                    return false;
                }
                if (distance < reach + _radius[i] + Slack)
                {
                    _overlapping[_overlapCount++] = i;
                }
            }
            _edgeOf = __instance;
            _edgeIndex = self;
            _edgeReach = reach;
            __result = false;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Mister), nameof(Mister.IsInsideOtherMister))]
        private static bool IsInsideOtherMister(Vector3 p, Mister ignore, ref bool __result)
        {
            if (!_haveAreas)
            {
                return true;
            }
            __result = false;
            // A particle of the edge just checked: only the areas overlapping it can hold it
            if (!ReferenceEquals(_edgeOf, null) && ReferenceEquals(ignore, _edgeOf) && Vector3.Distance(p, _position[_edgeIndex]) <= _edgeReach)
            {
                for (int j = 0; j < _overlapCount; j++)
                {
                    int i = _overlapping[j];
                    if (Vector3.Distance(p, _position[i]) < _radius[i] && p.y < _top[i])
                    {
                        __result = true;
                        break;
                    }
                }
                return false;
            }
            for (int i = 0; i < _areaCount; i++)
            {
                if (!ReferenceEquals(_areas[i], ignore) && Vector3.Distance(p, _position[i]) < _radius[i] && p.y < _top[i])
                {
                    __result = true;
                    break;
                }
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ParticleMist), nameof(ParticleMist.IsInsideOtherDemister))]
        private static bool IsInsideOtherDemister(List<Demister> fields, Vector3 p, float radius, Demister ignore, ref bool __result)
        {
            if (_fieldList == null || !ReferenceEquals(fields, _fieldList) || fields.Count != _fieldCount)
            {
                return true;
            }
            for (int i = 0; i < _fieldCount; i++)
            {
                if (!ReferenceEquals(_fields[i], ignore) && Vector3.Distance(_fieldPosition[i], p) + radius < _fieldRange[i])
                {
                    __result = true;
                    return false;
                }
            }
            __result = false;
            return false;
        }

        internal static void Reset()
        {
            _updates = 0;
            _ticks = 0;
            _peakTicks = 0;
        }

        // For the session report: how long the mist's updates took since the last one; null if there was no mist
        internal static string Report()
        {
            if (_updates == 0)
            {
                return null;
            }
            double average = _ticks * 1000.0 / Stopwatch.Frequency / _updates;
            double peak = _peakTicks * 1000.0 / Stopwatch.Frequency;
            string text = $"mist: {_updates} updates, {average:0.00} ms on average, {peak:0.00} ms at most";
            Reset();
            return text;
        }
    }
}
