using HarmonyLib;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Every game sends the world objects it has news about in batches (ZDOMan.SendZDOs): the server to each player,
    // each player's game to the server for what it controls. A batch only goes when the connection's send queue
    // (ZSteamSocket.GetSendQueueSize) is under 10240 bytes, and fills the room left under that. The queue counts what
    // Steam hasn't sent yet and what it sent but the other side hasn't confirmed (m_cbSentUnackedReliable; the batches
    // are reliable). So at most about 10 KB goes per round trip: ~200 KB/s at 40 ms of ping, ~50 KB/s at 180 ms. When
    // more changes (sailing into new areas, fights, busy bases), the rest waits, players and creatures first, other
    // objects (items being handed over to whoever picks them up) last.
    // So the two 10240s become a window fitted to each connection: Steam's send rate × (ping + 100 ms), between 10 KB
    // and 64 KB. The 100 ms covers Steam's acknowledgement delay and the time until the game's next batch for that
    // connection (every 0.05 s plus a frame per connection). Other mods (ServerSync, Jotunn) pace their own sending by
    // the same queue size, so outside SendZDOs it reports only what's over the window's extra room.
    // Steam's status comes from the game server API on a dedicated server, as the game's server build uses it (the
    // client build calls the client API; read from WorldPerformance and Network, which both do this). If it can't be
    // read, the game's 10240 stays. A mod's socket wrapped around the game's (ServerSync's and ConditionalConfigSync's
    // BufferingSocket, which keep it in a field named Original) is looked through.
    [HarmonyPatch]
    internal static class SendWindow
    {
        private const int GameWindow = 10240;
        private const int GameMinRoom = 2048;
        private const int MaxWindow = 64 * 1024;
        private const int MarginMilliseconds = 100;
        private const int KB = 1024;

        private struct Reading
        {
            public int Frame;
            public bool Known;
            public int Ping;
            public int Rate;
        }

        private sealed class Stats
        {
            public int Passes;
            public int Full;
            public int PeakQueue;
        }

        private const int MaxWrappers = 64;

        private static readonly Dictionary<uint, Reading> Readings = new Dictionary<uint, Reading>();
        private static readonly Dictionary<Type, FieldInfo> InnerSockets = new Dictionary<Type, FieldInfo>();
        private static readonly ConditionalWeakTable<ZDOMan.ZDOPeer, Stats> PeerStats = new ConditionalWeakTable<ZDOMan.ZDOPeer, Stats>();
        private static bool _sending;
        private static bool _unreadable;

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs))]
        private static IEnumerable<CodeInstruction> FitWindow(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            List<CodeInstruction> limits = code.FindAll(instruction => instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int value && value == GameWindow);
            if (limits.Count != 2)
            {
                Plugin.Log.LogWarning($"The game's send window wasn't found where expected ({limits.Count} instead of 2, a new version?), so it stays at 10 KB.");
                return code;
            }

            // ldc.i4 10240 → ldarg.1 (peer), call WindowFor(peer); the instruction keeps its labels
            foreach (CodeInstruction limit in limits)
            {
                limit.opcode = OpCodes.Ldarg_1;
                limit.operand = null;
                code.Insert(code.IndexOf(limit) + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SendWindow), nameof(WindowFor))));
            }
            return code;
        }

        // For the report: how often a connection's window was too full to send, and its largest backlog
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs))]
        private static void BeforeSending(ZDOMan.ZDOPeer peer, bool flush)
        {
            _sending = true;
            if (flush || Plugin.ReportMinutes <= 0 || peer?.m_peer?.m_socket == null)
            {
                return;
            }

            Stats stats = PeerStats.GetOrCreateValue(peer);
            int queue = peer.m_peer.m_socket.GetSendQueueSize();
            int window = WindowFor(peer);
            stats.Passes++;
            if (queue > window || window - queue < GameMinRoom)
            {
                stats.Full++;
            }
            if (queue > stats.PeakQueue)
            {
                stats.PeakQueue = queue;
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.SendZDOs))]
        private static void AfterSending()
        {
            _sending = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZSteamSocket), nameof(ZSteamSocket.GetSendQueueSize))]
        private static void HideExtraBacklog(ZSteamSocket __instance, ref int __result)
        {
            if (_sending || __result <= GameWindow)
            {
                return;
            }
            int extra = Window(__instance) - GameWindow;
            if (extra > 0)
            {
                __result = Math.Max(0, __result - extra);
            }
        }

        private static int WindowFor(ZDOMan.ZDOPeer peer)
        {
            return Window(SteamSocket(peer?.m_peer?.m_socket, out _));
        }

        // The game's Steam socket, inside other mods' sockets if any (wrappers: how many were looked through)
        private static ZSteamSocket SteamSocket(ISocket socket, out int wrappers)
        {
            for (wrappers = 0; socket != null && wrappers < MaxWrappers; wrappers++)
            {
                if (socket is ZSteamSocket steam)
                {
                    return steam;
                }
                socket = Inner(socket);
            }
            wrappers = 0;
            return null;
        }

        private static ISocket Inner(ISocket socket)
        {
            Type type = socket.GetType();
            if (!InnerSockets.TryGetValue(type, out FieldInfo field))
            {
                field = AccessTools.Field(type, "Original");
                if (field != null && !typeof(ISocket).IsAssignableFrom(field.FieldType))
                {
                    field = null;
                }
                InnerSockets[type] = field;
            }
            return field?.GetValue(socket) as ISocket;
        }

        private static int Window(ZSteamSocket socket)
        {
            if (!Plugin.FitSendWindow || !TryRead(socket, out Reading reading))
            {
                return GameWindow;
            }
            long window = (long)reading.Rate * (reading.Ping + MarginMilliseconds) / 1000;
            return (int)Math.Max(GameWindow, Math.Min(MaxWindow, window));
        }

        // Read once per frame per connection. A member missing at runtime (a server build that differs) throws when
        // Read is compiled, here, and the game's window stays from then on.
        private static bool TryRead(ZSteamSocket socket, out Reading reading)
        {
            reading = default;
            if (socket == null || _unreadable)
            {
                return false;
            }
            try
            {
                return Read(socket, out reading);
            }
            catch (Exception e)
            {
                _unreadable = true;
                Plugin.Log.LogWarning($"Steam's connection status couldn't be read, so the send window stays at 10 KB: {e.GetBaseException().Message}");
                return false;
            }
        }

        private static bool Read(ZSteamSocket socket, out Reading reading)
        {
            HSteamNetConnection connection = socket.m_con;
            uint key = connection.m_HSteamNetConnection;
            int frame = Time.frameCount;
            if (Readings.TryGetValue(key, out reading) && reading.Frame == frame)
            {
                return reading.Known;
            }

            SteamNetConnectionRealTimeStatus_t status = default;
            SteamNetConnectionRealTimeLaneStatus_t lanes = default;
            EResult result = ZNet.instance != null && ZNet.instance.IsDedicated()
                ? SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes)
                : SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes);
            reading = new Reading
            {
                Frame = frame,
                Known = result == EResult.k_EResultOK && status.m_nPing >= 0 && status.m_nSendRateBytesPerSecond > 0,
                Ping = status.m_nPing,
                Rate = status.m_nSendRateBytesPerSecond,
            };

            if (Readings.Count > 64)
            {
                Readings.Clear();
            }
            Readings[key] = reading;
            return reading.Known;
        }

        // For the session report: one line per connection, then its counts start over
        internal static List<string> Report()
        {
            List<string> lines = new List<string>();
            if (ZDOMan.instance == null || ZNet.instance == null)
            {
                return lines;
            }

            bool server = ZNet.instance.IsServer();
            foreach (ZDOMan.ZDOPeer peer in ZDOMan.instance.m_peers)
            {
                string name = server ? peer.m_peer?.m_playerName : "the server";
                if (string.IsNullOrEmpty(name))
                {
                    name = "a player joining";
                }
                Stats stats = PeerStats.GetOrCreateValue(peer);
                string full = stats.Passes > 0 ? $"full in {stats.Full * 100 / stats.Passes}% of {stats.Passes} sends" : "no sends";
                ISocket outer = peer.m_peer?.m_socket;
                ZSteamSocket socket = SteamSocket(outer, out int wrappers);
                if (wrappers > 0)
                {
                    name += $" (through {wrappers} other mods' sockets)";
                }

                if (socket == null)
                {
                    lines.Add($"Sending to {name}: not a Steam connection ({outer?.GetType().FullName ?? "none"}), so the game's 10 KB window, {full}, backlog up to {stats.PeakQueue / KB} KB.");
                }
                else if (!TryRead(socket, out Reading reading))
                {
                    lines.Add($"Sending to {name}: Steam's status unavailable, so the game's 10 KB window, {full}, backlog up to {stats.PeakQueue / KB} KB.");
                }
                else
                {
                    string window = Plugin.FitSendWindow ? $"window {WindowFor(peer) / KB} KB" : "the game's 10 KB window";
                    lines.Add($"Sending to {name}: ping {reading.Ping} ms, Steam rate {reading.Rate / KB} KB/s, {window}, {full}, backlog up to {stats.PeakQueue / KB} KB.");
                }

                stats.Passes = 0;
                stats.Full = 0;
                stats.PeakQueue = 0;
            }
            return lines;
        }
    }
}
