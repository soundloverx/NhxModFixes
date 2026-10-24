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
    // shared boss kill is sent from one player's game to the others', an ore deposit collapses (or not) and a tame keeps
    // its distance and a ship spares it as the game controlling them decides, and each player's game decides when
    // they're rested and how fast the fusion boxes they carry fuse
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "NhxModFixes";
        private const string PluginName = "NhxModFixes";
        private const string PluginVersion = "1.3.3";

        private const int DefaultMaxRtdMonsters = 3;
        private const float DefaultFollowDistance = 3f;
        private const int DefaultRestedDelay = 5;
        private static readonly KeyboardShortcut DefaultIgnoreTamesKey = new KeyboardShortcut(KeyCode.End);

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private ConfigEntry<int> _maxRtdMonsters;
        private ConfigEntry<bool> _passThroughTames;
        private ConfigEntry<bool> _passThroughPlayers;
        private ConfigEntry<bool> _fixTeleportGhosts;
        private ConfigEntry<bool> _fixChestSockets;
        private ConfigEntry<float> _followDistance;
        private ConfigEntry<bool> _stepBackFromPlayers;
        private ConfigEntry<bool> _shipsIgnoreTames;
        private ConfigEntry<int> _restedDelay;
        private ConfigEntry<bool> _fuseAllBoxes;
        private ConfigEntry<bool> _fuseWhileStill;
        private ConfigEntry<KeyboardShortcut> _ignoreTamesKey;
        private Harmony _harmonyInstance;

        internal static int MaxRtdMonsters => Instance?._maxRtdMonsters != null ? Instance._maxRtdMonsters.Value : DefaultMaxRtdMonsters;
        internal static bool PassThroughTames => Instance?._passThroughTames == null || Instance._passThroughTames.Value;
        internal static bool PassThroughPlayers => Instance?._passThroughPlayers == null || Instance._passThroughPlayers.Value;
        internal static bool FixTeleportGhosts => Instance?._fixTeleportGhosts == null || Instance._fixTeleportGhosts.Value;
        internal static bool FixChestSockets => Instance?._fixChestSockets == null || Instance._fixChestSockets.Value;
        internal static float FollowDistance => Instance?._followDistance != null ? Instance._followDistance.Value : DefaultFollowDistance;
        internal static bool StepBackFromPlayers => Instance?._stepBackFromPlayers == null || Instance._stepBackFromPlayers.Value;
        internal static bool ShipsIgnoreTames => Instance?._shipsIgnoreTames == null || Instance._shipsIgnoreTames.Value;
        internal static int RestedDelay => Instance?._restedDelay != null ? Instance._restedDelay.Value : DefaultRestedDelay;
        internal static bool FuseAllBoxes => Instance?._fuseAllBoxes == null || Instance._fuseAllBoxes.Value;
        internal static bool FuseWhileStill => Instance?._fuseWhileStill == null || Instance._fuseWhileStill.Value;
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

            _followDistance = Config.Bind(
                "Tames",
                "Follow distance",
                DefaultFollowDistance,
                new ConfigDescription(
                    "How far, in meters, a tame following a player stays from them when it isn't fighting, from its body to " +
                    "theirs. The game stops them 3 m away center to center, so big ones stand against the player, and flying ones " +
                    "(RtD's fairies) hover right over the player's head; with this, flying ones stay beside the player. " +
                    "0 keeps the game's behavior. The server's setting applies to every player.",
                    new AcceptableValueRange<float>(0f, 6f),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );
            _stepBackFromPlayers = Config.Bind(
                "Tames",
                "Step back when approached",
                true,
                new ConfigDescription(
                    "A following tame that a player walks up to, closer than half the follow distance, steps back where there's " +
                    "room: along a short path for walking ones, through open air for flying ones. It stops as soon as the player " +
                    "stops walking toward it, so walking up to a tame to pet or command it still works. The server's setting " +
                    "applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _shipsIgnoreTames = Config.Bind(
                "Ships",
                "No damage from tames",
                true,
                new ConfigDescription(
                    "A sailing ship that runs into a creature damages both, as if it hit a rock, but the game leaves players out. " +
                    "With this, tamed creatures are left out too, so tames around a sailing player (RtD's fairies, or tames " +
                    "BetterTames teleports to them) no longer damage the ship or get hurt by it. The server's setting applies to " +
                    "every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _restedDelay = Config.Bind(
                "Rested",
                "Time to get rested",
                DefaultRestedDelay,
                new ConfigDescription(
                    "How many seconds of resting (near a fire, sitting or under a roof, not wet or cold) it takes to get the " +
                    "Rested effect. The game's is 20. The server's setting applies to every player.",
                    new AcceptableValueRange<int>(0, 60),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _fuseAllBoxes = Config.Bind(
                "Fusion boxes",
                "Fuse all carried boxes",
                true,
                new ConfigDescription(
                    "Jewelcrafting's sealed fusion boxes fuse while carried, by the rate shown on them for each minute the player " +
                    "moves, and by a boss's reward when one dies nearby. Jewelcrafting gives that progress to one of the carried " +
                    "boxes, picked at random, so with three boxes each one takes three times as long. With this, every carried box " +
                    "gets its rate. The server's setting applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );
            _fuseWhileStill = Config.Bind(
                "Fusion boxes",
                "Fuse while standing still",
                true,
                new ConfigDescription(
                    "Jewelcrafting only counts a minute toward the fusion boxes' progress if the player has moved since the last " +
                    "one that counted (0.5 m, or 30 m on a ship), so standing at a crafting station or sitting by a fire doesn't " +
                    "count. With this, every minute counts. The server's setting applies to every player.",
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
