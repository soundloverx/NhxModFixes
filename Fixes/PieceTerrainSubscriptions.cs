using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace NhxModFixes.Fixes
{
    // Every building piece (WearNTear) signs up with the terrain of its zone when it loads (Start:
    // m_connectedHeightMap.m_clearConnectedWearNTearCache += ClearCachedSupport), so that a terrain change clears its
    // support cache, and signs off when it unloads (OnDestroy: -=). Mono copies the whole list on each sign-up, and
    // searches it and copies it again on each sign-off, so a zone with n pieces costs about n²/2 copies to load (3000
    // pieces: some 4.5 million, ~36 MB of arrays thrown away) and the same again to unload, if its terrain is still
    // there. That's a hitch every time a base loads or unloads.
    // So both calls go to the methods below instead: each terrain gets one handler, which clears the support of every
    // piece in a set. A terrain change still clears every piece of the zone, as in the game. With the setting off, or
    // for a piece that signed up while it was off, the game's list is used. Idea from ValheimPerformanceOptimizations.
    [HarmonyPatch]
    internal static class PieceTerrainSubscriptions
    {
        private sealed class Pieces
        {
            public readonly HashSet<WearNTear> Set = new HashSet<WearNTear>();
            public readonly Action ClearAll;

            public Pieces()
            {
                ClearAll = ClearSupport;
            }

            // A copy, in case clearing a piece ever changes the set
            private void ClearSupport()
            {
                bool shared = !_bufferInUse;
                List<WearNTear> buffer = shared ? Buffer : new List<WearNTear>(Set.Count);
                _bufferInUse = true;
                try
                {
                    buffer.AddRange(Set);
                    foreach (WearNTear piece in buffer)
                    {
                        if (piece != null)
                        {
                            piece.ClearCachedSupport();
                        }
                    }
                }
                finally
                {
                    buffer.Clear();
                    if (shared)
                    {
                        _bufferInUse = false;
                    }
                }
            }
        }

        private static readonly MethodInfo SignOnAccessor = AccessTools.Method(typeof(Heightmap), "add_m_clearConnectedWearNTearCache");
        private static readonly MethodInfo SignOffAccessor = AccessTools.Method(typeof(Heightmap), "remove_m_clearConnectedWearNTearCache");
        private static readonly FieldInfo TerrainList = AccessTools.Field(typeof(Heightmap), "m_clearConnectedWearNTearCache");
        // The event's own add and remove (with the publicized assembly, `+=` is ambiguous with its backing field)
        private static readonly Action<Heightmap, Action> AddToTerrain = Accessor(SignOnAccessor);
        private static readonly Action<Heightmap, Action> RemoveFromTerrain = Accessor(SignOffAccessor);

        private static readonly ConditionalWeakTable<Heightmap, Pieces> Zones = new ConditionalWeakTable<Heightmap, Pieces>();
        private static readonly List<WearNTear> Buffer = new List<WearNTear>();
        private static bool _bufferInUse;
        private static bool? _found;

        private static int _signedOn;
        private static int _signedOff;
        private static long _ticks;

        // Both calls have to be found, or a piece listed here would never be taken off
        private static bool Prepare()
        {
            if (_found == null)
            {
                _found = AddToTerrain != null && RemoveFromTerrain != null &&
                         Count(AccessTools.Method(typeof(WearNTear), nameof(WearNTear.Start)), SignOnAccessor) == 1 &&
                         Count(AccessTools.Method(typeof(WearNTear), nameof(WearNTear.OnDestroy)), SignOffAccessor) == 1;
                if (_found == false)
                {
                    Plugin.Log.LogWarning("Building pieces don't sign up with their terrain where expected (a new version?), so loading and unloading bases stays as slow as in the game.");
                }
            }
            return _found.Value;
        }

        private static Action<Heightmap, Action> Accessor(MethodInfo method)
        {
            try
            {
                return method != null ? (Action<Heightmap, Action>)Delegate.CreateDelegate(typeof(Action<Heightmap, Action>), method) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int Count(MethodBase method, MethodInfo accessor)
        {
            if (method == null || accessor == null)
            {
                return 0;
            }
            return PatchProcessor.GetOriginalInstructions(method).FindAll(instruction => instruction.Calls(accessor)).Count;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Start))]
        private static IEnumerable<CodeInstruction> RedirectSignOn(IEnumerable<CodeInstruction> instructions)
        {
            return Redirect(instructions, SignOnAccessor, nameof(SignOn));
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnDestroy))]
        private static IEnumerable<CodeInstruction> RedirectSignOff(IEnumerable<CodeInstruction> instructions)
        {
            return Redirect(instructions, SignOffAccessor, nameof(SignOff));
        }

        // callvirt Heightmap::add_/remove_…(Action) → call SignOn/SignOff(Heightmap, Action), same stack
        private static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions, MethodInfo accessor, string replacement)
        {
            MethodInfo method = AccessTools.Method(typeof(PieceTerrainSubscriptions), replacement);
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(accessor))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = method;
                }
                yield return instruction;
            }
        }

        private static void SignOn(Heightmap terrain, Action clearSupport)
        {
            long start = Stopwatch.GetTimestamp();
            if (Plugin.FixBaseLoading && clearSupport.Target is WearNTear piece)
            {
                if (!Zones.TryGetValue(terrain, out Pieces pieces))
                {
                    pieces = new Pieces();
                    Zones.Add(terrain, pieces);
                    AddToTerrain(terrain, pieces.ClearAll);
                }
                pieces.Set.Add(piece);
            }
            else
            {
                AddToTerrain(terrain, clearSupport);
            }
            _signedOn++;
            _ticks += Stopwatch.GetTimestamp() - start;
        }

        private static void SignOff(Heightmap terrain, Action clearSupport)
        {
            long start = Stopwatch.GetTimestamp();
            if (!(clearSupport.Target is WearNTear piece) || !Zones.TryGetValue(terrain, out Pieces pieces) || !pieces.Set.Remove(piece))
            {
                RemoveFromTerrain(terrain, clearSupport);
            }
            _signedOff++;
            _ticks += Stopwatch.GetTimestamp() - start;
        }

        internal static void Reset()
        {
            _signedOn = 0;
            _signedOff = 0;
            _ticks = 0;
        }

        // For the session report: how long signing pieces on and off took since the last one, and where the loaded
        // pieces are listed. Fixed, none are on the terrains' own lists.
        internal static string Report()
        {
            if (_found != true)
            {
                return null;
            }
            int listed = 0;
            int zones = 0;
            int most = 0;
            int ownLists = 0;
            foreach (Heightmap terrain in Heightmap.s_heightmaps)
            {
                if (terrain == null)
                {
                    continue;
                }
                if (Zones.TryGetValue(terrain, out Pieces pieces) && pieces.Set.Count > 0)
                {
                    zones++;
                    listed += pieces.Set.Count;
                    most = Math.Max(most, pieces.Set.Count);
                }
                if (TerrainList?.GetValue(terrain) is Delegate handlers)
                {
                    foreach (Delegate handler in handlers.GetInvocationList())
                    {
                        if (handler.Target is WearNTear)
                        {
                            ownLists++;
                        }
                    }
                }
            }

            double ms = _ticks * 1000.0 / Stopwatch.Frequency;
            string text = $"building pieces: {_signedOn} loaded and {_signedOff} unloaded, signing them on and off took {ms:0.0} ms; " +
                          $"{listed} listed by {zones} zones (up to {most} in one), {ownLists} on the terrains' own lists";
            Reset();
            return text;
        }
    }
}
