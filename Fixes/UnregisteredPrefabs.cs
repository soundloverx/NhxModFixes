using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Some mods give an effect a ZNetView, so the game shares it with every player, but never register it with the game
    // (RtDLegends' blood splats, OdinsFoodBarrels' building effects). The game that creates it shows it; every other game
    // can't find its prefab, and logs "Missing prefab hash" every frame for as long as it lasts, which shows as a lag spike.
    // When the game can't find a prefab, this looks for it among the prefabs the mods have loaded, and registers it.
    [HarmonyPatch]
    internal static class UnregisteredPrefabs
    {
        // Every loaded prefab with a ZNetView, by the hash of its name, as the game would compute it for its objects.
        // Built on the first prefab the game can't find, for the ZNetScene it's in
        private static Dictionary<int, GameObject> _loadedPrefabs;
        private static ZNetScene _scannedScene;

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObject))]
        [HarmonyPrefix]
        private static void RegisterMissingPrefab(ZNetScene __instance, ZDO zdo)
        {
            int hash = zdo.GetPrefab();
            if (hash == 0 || __instance.m_namedPrefabs.ContainsKey(hash))
            {
                return;
            }

            if (_loadedPrefabs == null || _scannedScene != __instance)
            {
                ScanLoadedPrefabs();
                _scannedScene = __instance;
            }

            // Not loaded by any mod (e.g. left in the world by a mod that was removed): the game handles it as usual
            if (!_loadedPrefabs.TryGetValue(hash, out GameObject prefab) || prefab == null)
            {
                return;
            }

            // Only into the lookup the game uses to create objects, not m_prefabs, which mods read to list the game's prefabs
            __instance.m_namedPrefabs[hash] = prefab;
            Plugin.Log.LogInfo($"Registered {prefab.name}: a mod loaded it but didn't register it with the game, " +
                               "so it couldn't be shown when another player's game created it.");
        }

        private static void ScanLoadedPrefabs()
        {
            _loadedPrefabs = new Dictionary<int, GameObject>();

            // Includes everything loaded from the mods' asset bundles, and the objects in the scene, which are skipped
            foreach (ZNetView nview in Resources.FindObjectsOfTypeAll<ZNetView>())
            {
                GameObject gameObject = nview.gameObject;
                if (gameObject.transform.parent != null || gameObject.scene.IsValid())
                {
                    continue;
                }

                // ZNetView.Awake names an object's ZDO this way
                int hash = Utils.GetPrefabName(gameObject).GetStableHashCode();
                if (!_loadedPrefabs.ContainsKey(hash))
                {
                    _loadedPrefabs.Add(hash, gameObject);
                }
            }
        }
    }
}
