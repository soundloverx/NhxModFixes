using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Unity hands a Collision to every OnCollisionEnter/Stay/Exit. With Physics.reuseCollisionCallbacks off, as the game
    // leaves it (m_ReuseCollisionCallbacks is off in its settings, and nothing sets it), each callback gets a new
    // Collision, whose contact array is filled in its constructor: every character touching the ground gets some each
    // physics step (50 a second), the other players' and their creatures too. On, one Collision is reused, and the array
    // is only made when a script reads `contacts` (Character's OnCollisionStay for the characters this game controls,
    // ImpactEffect, FloatingTerrain). The game's handlers (Aoe, Character, Fish, FloatingTerrain, ImpactEffect) and the
    // ship fix only read it during the call; in HeavyMods only Impactful Skills touches them (a transpiler on
    // ImpactEffect's). Called from the plugin's Update, so a change of the setting applies at once.
    internal static class CollisionReuse
    {
        private static bool _applied;

        internal static void Update()
        {
            if (Plugin.ReuseCollisions)
            {
                if (!Physics.reuseCollisionCallbacks)
                {
                    Physics.reuseCollisionCallbacks = true;
                    if (!_applied)
                    {
                        Plugin.Log.LogInfo("Unity now reuses its collision objects.");
                    }
                }
                _applied = true;
            }
            else if (_applied)
            {
                // Back to the game's
                Physics.reuseCollisionCallbacks = false;
                _applied = false;
                Plugin.Log.LogInfo("Unity makes a new collision object for each contact again, as in the game.");
            }
        }
    }
}
