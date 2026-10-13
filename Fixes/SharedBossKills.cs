using HarmonyLib;
using System.Collections.Generic;

namespace NhxModFixes.Fixes
{
    // When a boss dies, the game records it in the character of only one player: the one whose game controls the boss at
    // that moment (Character.OnDeath queues the boss's key, e.g. "defeated_eikthyr", for the local player). Everyone else
    // gets the kill in their stats and sees the world's key, which is where the achievement comes from, but their
    // character doesn't remember the boss. Mods that read a player's own bosses then treat them as if they hadn't beaten
    // it: CLLC's "Fluid" world level, which gives the next biomes level 3 instead of 1.
    // This gives the boss to every player who hit it (the game's own list, the one it registers the kill for) or was
    // within 100 m when it died.
    [HarmonyPatch]
    internal static class SharedBossKills
    {
        private const string RpcName = "NhxModFixes_BossKill";
        private const float Range = 100f;

        private static readonly List<Player> NearbyPlayers = new List<Player>();

        // ZNet.Awake creates the game's routed RPCs, once per session
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        [HarmonyPostfix]
        private static void RegisterRpc()
        {
            ZRoutedRpc.instance.Register<string>(RpcName, OnBossKill);
        }

        // A prefix: OnDeath ends by destroying the boss, and its ZDO with it. Only the game that controls the boss runs
        // OnDeath (Character.CheckDeath is called for the owner only)
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        [HarmonyPrefix]
        private static void ShareBossKill(Character __instance)
        {
            string key = __instance.m_defeatSetGlobalKey;
            ZDO zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
            if (string.IsNullOrEmpty(key) || zdo == null || !__instance.m_nview.IsOwner())
            {
                return;
            }

            // The local player gets it from the game itself
            long self = ZDOMan.GetSessionID();
            HashSet<long> peers = new HashSet<long>();

            // The game marks each player who hits a creature with "Attackers" + their name, as Character.OnDeath reads it
            foreach (ZNet.PlayerInfo player in ZNet.instance.GetPlayerList())
            {
                if (zdo.GetBool(ZDOVars.s_attackers + player.m_name))
                {
                    peers.Add(player.m_characterID.UserID);
                }
            }

            NearbyPlayers.Clear();
            Player.GetPlayersInRange(__instance.transform.position, Range, NearbyPlayers);
            foreach (Player player in NearbyPlayers)
            {
                ZDOID id = player.GetZDOID();
                if (!id.IsNone())
                {
                    peers.Add(id.UserID);
                }
            }

            foreach (long peer in peers)
            {
                if (peer != self && peer != 0)
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcName, key);
                }
            }

            Plugin.Log.LogInfo($"{__instance.m_name} defeated: shared \"{key}\" with {peers.Count - (peers.Contains(self) ? 1 : 0)} other player(s).");
        }

        private static void OnBossKill(long sender, string key)
        {
            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.AddUniqueKey(key);
            }
            else
            {
                // Dead and respawning: the game's own queue, which it applies when the player spawns
                Player.m_addUniqueKeyQueue.Add(key);
            }

            Plugin.Log.LogInfo($"Another player's game shared a boss kill with you: \"{key}\".");
        }
    }
}
