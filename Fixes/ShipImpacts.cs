using HarmonyLib;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // A ship running into something at 1.5 m/s or more damages it and itself (its ImpactEffect: 20 to 50 blunt by ship,
    // more the faster it goes, at most every 0.5 s), with a hit sound. Characters count too, except players (every ship
    // has m_damagePlayers off), so a tame in a sailing ship's way damages it: RtD's fairies following a player on board,
    // or teleported next to them by BetterTames, inside the hull. This leaves tames out the same way: no damage to the
    // ship or the tame, and no hit effect. The tame is still pushed out of the way. Only the game controlling a ship runs
    // its ImpactEffect.
    [HarmonyPatch]
    internal static class ShipImpacts
    {
        [HarmonyPatch(typeof(ImpactEffect), nameof(ImpactEffect.OnCollisionEnter))]
        [HarmonyPrefix]
        private static bool SkipTames(ImpactEffect __instance, Collision info)
        {
            if (!Plugin.ShipsIgnoreTames || __instance.GetComponent<Ship>() == null)
            {
                return true;
            }

            Character character = info.collider.GetComponentInParent<Character>();
            return character == null || !character.IsTamed();
        }
    }
}
