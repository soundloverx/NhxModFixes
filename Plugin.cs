using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using NhxModFixes.Diagnostics;
using NhxModFixes.Fixes;
using System.Reflection;
using UnityEngine;

namespace NhxModFixes
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.HardDependency)]
    // Loaded first when installed, so that Jewelcrafting's and AzuCraftyBoxes' classes can be found to patch, and
    // RtDLegends is known to be installed
    [BepInDependency("org.bepinex.plugins.jewelcrafting", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("soloredis.rtdlegends", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Azumatt.AzuCraftyBoxes", BepInDependency.DependencyFlags.SoftDependency)]
    // Each player's game spawns the monsters around them and moves its own player, so everyone needs the RtD limit and
    // the collisions, with the server's settings. A prefab is only found again by the games that have the fix, a
    // shared boss kill is sent from one player's game to the others', an ore deposit collapses (or not) and a tame keeps
    // its distance and a ship spares it as the game controlling them decides, and each player's game decides when
    // they're rested, how fast the fusion boxes they carry fuse, whether the RtD altars in a dungeon it rebuilds still
    // summon their boss and how far its player's repairs reach
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "NhxModFixes";
        private const string PluginName = "NhxModFixes";
        private const string PluginVersion = "1.6.1";

        private const int DefaultMaxRtdMonsters = 3;
        private const float DefaultFollowDistance = 3f;
        private const int DefaultRestedDelay = 5;
        private const float DefaultRepairRadius = 20f;
        private const int DefaultReportMinutes = 5;
        private const int DefaultDungeonFog = 100;
        private const int DefaultDungeonDustAndMist = 100;
        private static readonly KeyboardShortcut DefaultIgnoreTamesKey = new KeyboardShortcut(KeyCode.End);

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private ConfigEntry<int> _maxRtdMonsters;
        private ConfigEntry<bool> _passThroughTames;
        private ConfigEntry<bool> _passThroughPlayers;
        private ConfigEntry<bool> _fixChestSockets;
        private ConfigEntry<float> _followDistance;
        private ConfigEntry<bool> _stepBackFromPlayers;
        private ConfigEntry<bool> _shipsIgnoreTames;
        private ConfigEntry<int> _restedDelay;
        private ConfigEntry<bool> _fuseAllBoxes;
        private ConfigEntry<bool> _fuseWhileStill;
        private ConfigEntry<bool> _fixRtdAltars;
        private ConfigEntry<bool> _forgetUnloadedChests;
        private ConfigEntry<bool> _fitSendWindow;
        private ConfigEntry<bool> _reuseCollisions;
        private ConfigEntry<bool> _fixBaseLoading;
        private ConfigEntry<bool> _fasterMist;
        private ConfigEntry<bool> _fasterZoneUnloading;
        private ConfigEntry<float> _repairRadius;
        private ConfigEntry<KeyboardShortcut> _ignoreTamesKey;
        private ConfigEntry<int> _dungeonFog;
        private ConfigEntry<int> _dungeonDustAndMist;
        private ConfigEntry<bool> _steadyTorches;
        private ConfigEntry<bool> _disableTamedOnFire;
        private ConfigEntry<bool> _disableTamedShields;
        private ConfigEntry<int> _reportMinutes;
        private Harmony _harmonyInstance;

        internal static int MaxRtdMonsters => Instance?._maxRtdMonsters != null ? Instance._maxRtdMonsters.Value : DefaultMaxRtdMonsters;
        internal static bool PassThroughTames => Instance?._passThroughTames == null || Instance._passThroughTames.Value;
        internal static bool PassThroughPlayers => Instance?._passThroughPlayers == null || Instance._passThroughPlayers.Value;
        internal static bool FixChestSockets => Instance?._fixChestSockets == null || Instance._fixChestSockets.Value;
        internal static float FollowDistance => Instance?._followDistance != null ? Instance._followDistance.Value : DefaultFollowDistance;
        internal static bool StepBackFromPlayers => Instance?._stepBackFromPlayers == null || Instance._stepBackFromPlayers.Value;
        internal static bool ShipsIgnoreTames => Instance?._shipsIgnoreTames == null || Instance._shipsIgnoreTames.Value;
        internal static int RestedDelay => Instance?._restedDelay != null ? Instance._restedDelay.Value : DefaultRestedDelay;
        internal static bool FuseAllBoxes => Instance?._fuseAllBoxes == null || Instance._fuseAllBoxes.Value;
        internal static bool FuseWhileStill => Instance?._fuseWhileStill == null || Instance._fuseWhileStill.Value;
        internal static bool FixRtdAltars => Instance?._fixRtdAltars == null || Instance._fixRtdAltars.Value;
        internal static bool ForgetUnloadedChests => Instance?._forgetUnloadedChests == null || Instance._forgetUnloadedChests.Value;
        internal static bool FitSendWindow => Instance?._fitSendWindow == null || Instance._fitSendWindow.Value;
        internal static bool ReuseCollisions => Instance?._reuseCollisions == null || Instance._reuseCollisions.Value;
        internal static bool FixBaseLoading => Instance?._fixBaseLoading == null || Instance._fixBaseLoading.Value;
        internal static bool FasterMist => Instance?._fasterMist == null || Instance._fasterMist.Value;
        internal static bool FasterZoneUnloading => Instance?._fasterZoneUnloading == null || Instance._fasterZoneUnloading.Value;
        internal static float RepairRadius => Instance?._repairRadius != null ? Instance._repairRadius.Value : DefaultRepairRadius;
        internal static KeyboardShortcut IgnoreTamesKey => Instance?._ignoreTamesKey != null ? Instance._ignoreTamesKey.Value : DefaultIgnoreTamesKey;
        internal static int DungeonFog => Instance?._dungeonFog != null ? Instance._dungeonFog.Value : DefaultDungeonFog;
        internal static int DungeonDustAndMist => Instance?._dungeonDustAndMist != null ? Instance._dungeonDustAndMist.Value : DefaultDungeonDustAndMist;
        internal static bool SteadyTorches => Instance?._steadyTorches != null && Instance._steadyTorches.Value;
        internal static bool DisableTamedOnFire => Instance?._disableTamedOnFire != null && Instance._disableTamedOnFire.Value;
        internal static bool DisableTamedShields => Instance?._disableTamedShields != null && Instance._disableTamedShields.Value;
        internal static int ReportMinutes => Instance?._reportMinutes != null ? Instance._reportMinutes.Value : DefaultReportMinutes;

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

            _fixRtdAltars = Config.Bind(
                "Boss altars",
                "Fix RtD altars",
                true,
                new ConfigDescription(
                    "RtDLegends' altars inside dungeons (the Fenrir King's and the Spider Queen's) stop summoning their boss when " +
                    "Creature Level & Loot Control rebuilds their dungeon to refill it, until the player who went in logs out and " +
                    "back in. With this, the rebuilt altar takes over from the old one. Only used when RtDLegends is installed. " +
                    "The server's setting applies to every player.",
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

            _forgetUnloadedChests = Config.Bind(
                "Containers",
                "Forget unloaded chests",
                true,
                new ConfigDescription(
                    "AzuCraftyBoxes lists the chests it can take materials from, but a chest that loads and unloads while you're " +
                    "not crafting or building (walking past a base) stays on its list for the rest of the session. With this, " +
                    "unloaded chests are taken off its list. Only used when AzuCraftyBoxes is installed. The server's setting " +
                    "applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _fitSendWindow = Config.Bind(
                "Network",
                "Fit send window to ping",
                true,
                new ConfigDescription(
                    "The game sends world updates in batches of at most 10 KB, counting what the other side hasn't confirmed " +
                    "yet, so a player with a high ping gets them more slowly when a lot changes (sailing, fights), and items " +
                    "can take seconds to pick up. With this, each connection's limit fits its ping and Steam's send rate (up " +
                    "to 64 KB). It applies to what each game sends: the server to each player, each player's game to the " +
                    "server. The server's setting applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _reuseCollisions = Config.Bind(
                "Physics",
                "Reuse collision objects",
                true,
                new ConfigDescription(
                    "The game has Unity make a new object for each collision it reports to the game, and every character " +
                    "standing on the ground makes some 50 times a second, so there's a lot of garbage to clean up, which " +
                    "shows as short pauses. With this, Unity reuses one object. Collisions work the same. The server's setting " +
                    "applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _fixBaseLoading = Config.Bind(
                "Bases",
                "Fix base loading slowdown",
                true,
                new ConfigDescription(
                    "Each building piece signs up with its zone's terrain when it loads and signs off when it unloads, and " +
                    "each time the game copies the zone's whole list, so a big base takes much longer to load and unload " +
                    "than a small one (3000 pieces in a zone make millions of copies). With this, each zone keeps its pieces " +
                    "in a set instead. Changing the terrain still updates the pieces as in the game. The server's setting " +
                    "applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _fasterMist = Config.Bind(
                "Mist",
                "Faster mist",
                true,
                new ConfigDescription(
                    "In the Mistlands, for each mist particle it places, the game checks every mist area and wisp around, " +
                    "asking Unity for their positions again each time. With this, it reads them once per update and checks " +
                    "only the areas a particle can be in. The mist looks the same. Only matters while the mists are on. " +
                    "The server's setting applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _fasterZoneUnloading = Config.Bind(
                "Zones",
                "Faster zone unloading",
                true,
                new ConfigDescription(
                    "While you travel, before unloading each zone left behind, the game goes through every object it has " +
                    "loaded to see whether one still stands in that zone. With this, it only looks closely at the objects " +
                    "near that zone. The server's setting applies to every player.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            _repairRadius = Config.Bind(
                "Repair",
                "Repair radius",
                DefaultRepairRadius,
                new ConfigDescription(
                    "Repairing with the hammer also repairs every damaged building piece within this many meters of you, " +
                    "for one swing's stamina, as long as its crafting station is in range and no ward keeps you out. " +
                    "Clicking a piece that isn't damaged repairs the ones around it. 0 repairs only the piece you aim at, " +
                    "as in the game. The server's setting applies to every player.",
                    new AcceptableValueRange<float>(0f, 30f),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }
                )
            );

            // Each player's own, not synced: how the game looks to them
            _dungeonFog = Config.Bind(
                "Dungeons",
                "Dungeon fog",
                DefaultDungeonFog,
                new ConfigDescription(
                    "How thick the fog is inside dungeons, in percent of the game's, for you only. Inside a dungeon (burial " +
                    "chambers, sunken crypts, caves, mines, and the dungeons mods add) the game's fog is 10 to 60 times " +
                    "thicker than outdoors, so you can't see far. 100 is the game's, 0 removes it.",
                    new AcceptableValueRange<int>(0, 100)
                )
            );
            _dungeonDustAndMist = Config.Bind(
                "Dungeons",
                "Dungeon dust and mist",
                DefaultDungeonDustAndMist,
                new ConfigDescription(
                    "How much of the dust and drifting mist around you inside dungeons is kept, in percent, for you only. " +
                    "The mist is lit by every torch, fire and spell nearby, so it glows and pulses with them. 100 is the " +
                    "game's, 0 removes them.",
                    new AcceptableValueRange<int>(0, 100)
                )
            );
            _steadyTorches = Config.Bind(
                "Lights",
                "Steady torches",
                false,
                "Torches, fires, braziers, lanterns and other lasting lights shine steadily instead of flickering and " +
                "wobbling, everywhere, for you only. The game's \"Reduce flashing lights\" option (Accessibility) only calms " +
                "short flashes (lightning, explosions, spell hits) and leaves these flickering."
            );
            _disableTamedOnFire = Config.Bind(
                "Tames",
                "Disable tamed on fire",
                false,
                "Hides the flames and the light of tamed creatures that are burning, from fire or spirit damage, for you " +
                "only. A burning tame flying around your head (RtD's fairies) can cover most of the screen with fire. " +
                "Their burning sound and damage stay."
            );
            _disableTamedShields = Config.Bind(
                "Tames",
                "Disable tamed shields",
                false,
                "Hides the shield bubbles on tamed creatures (the Staff of Protection's, the ones RtD's fairies and pixies " +
                "cast, and other mods' shields), with the bursts when a shield is hit or breaks and a fairy's burst when it " +
                "casts one, for you only. The shields still protect them. The bursts of a tame another player's game " +
                "controls are only hidden if that player has this version of NhxModFixes."
            );

            // Each game's own (the dedicated server's too), not synced
            _reportMinutes = Config.Bind(
                "Diagnostics",
                "Session report interval",
                DefaultReportMinutes,
                new ConfigDescription(
                    "Every this many minutes in a world, writes the frame rate, memory use and garbage collections, how many " +
                    "objects the game holds, the build menu's and AzuCraftyBoxes' lists, how long loading building pieces, " +
                    "the mist and unloading zones took, and each connection's ping and send backlog to the log, and logs " +
                    "item pickups that waited a second or more for another player's game. For finding what slows a long " +
                    "session down. 0 turns it off.",
                    new AcceptableValueRange<int>(0, 60)
                )
            );

            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmonyInstance = new Harmony(PluginGuid);
            _harmonyInstance.PatchAll(assembly);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void Update()
        {
            CollisionReuse.Update();
            TameEffects.Update();
            SessionReport.Update();
        }

        private void OnDestroy()
        {
            _harmonyInstance?.UnpatchSelf();

            Log?.LogInfo($"{PluginName} v{PluginVersion} unloaded.");

            Instance = null;
        }
    }
}
