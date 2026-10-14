using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using System.Reflection;
using UnityEngine;

namespace NhxModFixes
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.HardDependency)]
    // Loaded first when installed, so that its classes can be found to patch
    [BepInDependency("org.bepinex.plugins.jewelcrafting", BepInDependency.DependencyFlags.SoftDependency)]
    // Each player's game spawns the monsters around them and moves its own player, so everyone needs the RtD limit and
    // the collisions, with the server's settings. A prefab is only found again by the games that have the fix, a
    // shared boss kill is sent from one player's game to the others', and an ore deposit collapses (or not) as the game
    // controlling it decides
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "NhxModFixes";
        private const string PluginName = "NhxModFixes";
        private const string PluginVersion = "1.2.1";

        private const int DefaultMaxRtdMonsters = 3;
        private static readonly KeyboardShortcut DefaultIgnoreTamesKey = new KeyboardShortcut(KeyCode.End);

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private ConfigEntry<int> _maxRtdMonsters;
        private ConfigEntry<bool> _passThroughTames;
        private ConfigEntry<bool> _passThroughPlayers;
        private ConfigEntry<bool> _fixTeleportGhosts;
        private ConfigEntry<bool> _fixChestSockets;
        private ConfigEntry<KeyboardShortcut> _ignoreTamesKey;
        private Harmony _harmonyInstance;

        internal static int MaxRtdMonsters => Instance?._maxRtdMonsters != null ? Instance._maxRtdMonsters.Value : DefaultMaxRtdMonsters;
        internal static bool PassThroughTames => Instance?._passThroughTames == null || Instance._passThroughTames.Value;
        internal static bool PassThroughPlayers => Instance?._passThroughPlayers == null || Instance._passThroughPlayers.Value;
        internal static bool FixTeleportGhosts => Instance?._fixTeleportGhosts == null || Instance._fixTeleportGhosts.Value;
        internal static bool FixChestSockets => Instance?._fixChestSockets == null || Instance._fixChestSockets.Value;
        internal static KeyboardShortcut IgnoreTamesKey => Instance?._ignoreTamesKey != null ? Instance._ignoreTamesKey.Value : DefaultIgnoreTamesKey;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Admin only: Jotunn syncs the server's value to every player
            _maxRtdMonsters = Config.Bind(
                "RtD spawns",
                "Max RtD monsters nearby",
                DefaultMaxRtdMonsters,
                new ConfigDescription(
                    "RtDLegends and RtDMythics add their monsters to the world's spawns, each kind with its own limit, on top of the game's. " +
                    "This is how many of them, all kinds together, can be around (within about 150 m) before more stop spawning. " +
                    "Tamed ones and the ones inside dungeons don't count. " +
                    "0 removes the limit. The server's setting applies to every player.",
                    new AcceptableValueRange<int>(0, 30),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _passThroughTames = Config.Bind(
                "Collisions",
                "Pass through tames",
                true,
                new ConfigDescription(
                    "Players walk through tamed creatures (pets, mounts, summons) instead of being pushed around by them. " +
                    "Being pushed away from a crafting station closes its window. The server's setting applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );
            _passThroughPlayers = Config.Bind(
                "Collisions",
                "Pass through players",
                true,
                new ConfigDescription(
                    "Players walk through each other instead of bumping into each other. The server's setting applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            // Only the server uses it; admin only so that admins can change it in game too
            _fixTeleportGhosts = Config.Bind(
                "Teleports",
                "Fix players left in portals",
                true,
                new ConfigDescription(
                    "Because of a bug in the game's server, a player who teleports stays visible to the others where they left " +
                    "(in the portal), until they come back or the others move away. This makes the server tell the others' games " +
                    "to remove them. Only the server's setting matters.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _fixChestSockets = Config.Bind(
                "Containers",
                "Fix sockets opening at chests",
                true,
                new ConfigDescription(
                    "Opening a chest with the interact key could open the sockets of an item in the middle of the screen instead " +
                    "(Jewelcrafting, with AzuEPI's equipment slots there), and leave the mouse stuck until the inventory was closed. " +
                    "With this, the key press that opens a chest doesn't open sockets, and the mouse is freed if the chest closes " +
                    "while the key is held. The server's setting applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            // Each player's own key, not synced
            _ignoreTamesKey = Config.Bind(
                "Tames",
                "Ignore tames key",
                DefaultIgnoreTamesKey,
                "Turns ignoring tamed creatures on or off, for you only (off when the game starts). While on, you aim through tames " +
                "at what's behind them, so a pet hovering over your head no longer takes the interact key, the hover text or " +
                "items used from the hotbar, and you can't pet, command, ride or protect tames until you turn it off."
            );

            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmonyInstance = new Harmony(PluginGuid);
            _harmonyInstance.PatchAll(assembly);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmonyInstance?.UnpatchSelf();

            Log?.LogInfo($"{PluginName} v{PluginVersion} unloaded.");

            Instance = null;
        }
    }
}
