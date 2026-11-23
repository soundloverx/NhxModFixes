using HarmonyLib;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Torches, fires, braziers, lanterns, glowing weapons and spell effects have a LightFlicker that changes their
    // light's intensity every frame (by up to m_flickerIntensity, 0.1 for most) and moves it (by up to m_movement,
    // 0.1 m), so the light, the shadows and the lit smoke around it pulse. The game's "Reduce flashing lights"
    // (Accessibility) only changes the lights marked for it (66 of 475 in the game: lightning, explosions, spell hits,
    // burning); the others, torches included, keep flickering. With this, lasting lights (no time to live) shine
    // steadily and stay put, keeping their fade-in and the option's dimming; short-lived ones (flashes) are left to the
    // game. Each player's own setting, read on every update so it applies at once.
    [HarmonyPatch]
    internal static class SteadyTorches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(LightFlicker), nameof(LightFlicker.CustomUpdate))]
        private static bool CustomUpdate(LightFlicker __instance, float deltaTime)
        {
            Light light = __instance.m_light;
            if (!Plugin.SteadyTorches || __instance.m_ttl > 0f || light == null)
            {
                return true;
            }
            if (!light.enabled)
            {
                return false;
            }

            // The game's update without the flicker and the movement
            __instance.m_time += deltaTime;
            float intensity = 1f;
            if (__instance.m_fadeInDuration > 0f)
            {
                intensity *= Utils.LerpStep(0f, __instance.m_fadeInDuration, __instance.m_time);
            }
            __instance.m_targetIntensity = intensity;
            __instance.m_smoothedIntensity = intensity;
            light.intensity = __instance.m_baseIntensity * intensity * __instance.m_multiplier;
            __instance.transform.localPosition = __instance.m_basePosition;
            return false;
        }
    }
}
