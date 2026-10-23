using HarmonyLib;

namespace NhxModFixes.Fixes
{
    // Near a fire, sitting or under a roof (and not wet, cold or seen by enemies), a player gets "Resting", which turns
    // into "Rested" after 20 s: the game's Resting effect (an SE_Cozy) adds Rested on each update once it has lasted its
    // m_delay, which also keeps Rested topped up while the player rests. This sets that delay ("Time to get rested")
    // before each update, so a change applies at once, even while resting. Only the game controlling a player updates
    // its status effects: each player's own.
    [HarmonyPatch]
    internal static class RestedDelay
    {
        [HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect))]
        [HarmonyPrefix]
        private static void SetDelay(SE_Cozy __instance)
        {
            if (__instance.NameHash() == SEMan.s_statusEffectResting)
            {
                __instance.m_delay = Plugin.RestedDelay;
            }
        }
    }
}
