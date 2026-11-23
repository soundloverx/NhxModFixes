using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // A dungeon's interior is 5000 m up (Character.InInterior: above 3000 m), and its location forces an environment
    // there (Location.m_interiorEnvironment: Crypt for burial chambers, troll caves and every modded dungeon in
    // HeavyMods, SunkenCrypt, Caves, InfectedMine, TheHollow...). Those have fog 10 to 60 times denser than outdoors
    // (0.1 to 0.2, against 0.003 to 0.01) and particle systems that EnvMan keeps around the camera: big smoke or mist
    // puffs drawn with a shader every nearby light brightens, and dust motes. A few burial chambers and the Deep North's
    // holes also place dust of their own in their rooms. Each player's own settings thin all of it while the camera is
    // inside a dungeon.
    //
    // The fog: EnvMan.SetEnv (once per frame at most) sets RenderSettings.fogDensity from the environment, and a postfix
    // scales it. It runs before FarLands' postfix, which reads that value as the game's fog (FarLands turns its own air
    // off while an environment is forced anyway).
    // The particles: their emission is scaled by multiplying it, as Jewelcrafting's Snow Goggles gem does with the
    // same systems, so the two add up instead of overwriting each other. At 0 they're hidden instead (the renderer off),
    // so the rates can be scaled back. When they're thinned, the particles already there are thinned too.
    [HarmonyPatch]
    internal static class DungeonAtmosphere
    {
        private sealed class ByReference : IEqualityComparer<ParticleSystem>
        {
            public bool Equals(ParticleSystem x, ParticleSystem y) => ReferenceEquals(x, y);
            public int GetHashCode(ParticleSystem obj) => RuntimeHelpers.GetHashCode(obj);
        }

        // What was done to one particle system
        private sealed class Scaled
        {
            // The setting applied, 0 to 1
            internal float Applied = 1f;
            // Its emission's scale: the setting, except at 0 (left as it was, the renderer off)
            internal float Emission = 1f;
            internal ParticleSystemRenderer Renderer;
            internal bool Hidden;
        }

        private static readonly Dictionary<ParticleSystem, Scaled> State = new Dictionary<ParticleSystem, Scaled>(new ByReference());

        private static EnvMan _envMan;
        // The environment's particle systems, and the array EnvMan has them in
        private static GameObject[] _systemsOf;
        private static readonly List<ParticleSystem> EnvironmentSystems = new List<ParticleSystem>();
        private static readonly List<ParticleSystem> Previous = new List<ParticleSystem>();
        // Dust placed in dungeon rooms
        private static readonly List<ParticleSystem> RoomSystems = new List<ParticleSystem>();

        private static ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[1024];

        private static string _loggedEnvironment;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
        private static void AfterSetEnv(EnvMan __instance, EnvSetup env)
        {
            // SetEnv leaves the fog alone without a camera
            Camera camera = Utils.GetMainCamera();
            bool inside = camera != null && Character.InInterior(camera.transform.position);
            float fog = inside ? Plugin.DungeonFog / 100f : 1f;
            float particles = inside ? Plugin.DungeonDustAndMist / 100f : 1f;

            float gameFog = RenderSettings.fogDensity;
            if (fog < 1f)
            {
                RenderSettings.fogDensity = gameFog * fog;
            }
            UpdateParticles(__instance, particles);

            if (!inside)
            {
                _loggedEnvironment = null;
            }
            else if ((fog < 1f || particles < 1f) && env != null && env.m_name != _loggedEnvironment)
            {
                _loggedEnvironment = env.m_name;
                Plugin.Log.LogInfo(
                    $"Inside a dungeon ({env.m_name}): fog at {fog:0%} (density {gameFog:0.000} to {RenderSettings.fogDensity:0.000}), " +
                    $"dust and mist at {particles:0%} ({EnvironmentSystems.Count} of the environment's particle systems, " +
                    $"{RoomSystems.Count} in rooms).");
            }
        }

        // Dust that a room places itself (the game's are named "dust_particles" and "interior_dust"), in dungeons only
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Room), nameof(Room.Awake))]
        private static void AfterRoomAwake(Room __instance)
        {
            if (!Character.InInterior(__instance.transform.position))
            {
                return;
            }
            foreach (ParticleSystem system in __instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.gameObject.name.IndexOf("dust", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    RoomSystems.Add(system);
                }
            }
        }

        private static void UpdateParticles(EnvMan envMan, float factor)
        {
            if (!ReferenceEquals(envMan, _envMan))
            {
                // A new world session: the old systems are gone
                _envMan = envMan;
                State.Clear();
                EnvironmentSystems.Clear();
                _systemsOf = null;
            }

            GameObject[] current = envMan.m_currentPSystems;
            if (!ReferenceEquals(current, _systemsOf))
            {
                // The environment changed: the previous one's systems go back to the game's, unless the new one
                // has them too (Crypt and SunkenCrypt share theirs)
                Previous.Clear();
                Previous.AddRange(EnvironmentSystems);
                EnvironmentSystems.Clear();
                _systemsOf = current;
                if (current != null)
                {
                    foreach (GameObject root in current)
                    {
                        if (root != null)
                        {
                            EnvironmentSystems.AddRange(root.GetComponentsInChildren<ParticleSystem>(true));
                        }
                    }
                }
                foreach (ParticleSystem system in Previous)
                {
                    if (!EnvironmentSystems.Contains(system))
                    {
                        Apply(system, 1f);
                    }
                }
                Previous.Clear();
            }

            foreach (ParticleSystem system in EnvironmentSystems)
            {
                Apply(system, factor);
            }
            for (int i = RoomSystems.Count - 1; i >= 0; i--)
            {
                ParticleSystem system = RoomSystems[i];
                if (system == null)
                {
                    RoomSystems.RemoveAt(i);
                    State.Remove(system);
                    continue;
                }
                Apply(system, factor);
            }
        }

        private static void Apply(ParticleSystem system, float factor)
        {
            if (system == null)
            {
                return;
            }
            if (!State.TryGetValue(system, out Scaled scaled))
            {
                if (factor >= 1f)
                {
                    return;
                }
                scaled = new Scaled { Renderer = system.GetComponent<ParticleSystemRenderer>() };
                State[system] = scaled;
            }
            if (scaled.Applied == factor)
            {
                return;
            }

            float emission = factor > 0f ? factor : 1f;
            if (emission != scaled.Emission)
            {
                float ratio = emission / scaled.Emission;
                ParticleSystem.EmissionModule module = system.emission;
                module.rateOverTimeMultiplier *= ratio;
                module.rateOverDistanceMultiplier *= ratio;
                int bursts = module.burstCount;
                for (int i = 0; i < bursts; i++)
                {
                    ParticleSystem.Burst burst = module.GetBurst(i);
                    burst.count = Scale(burst.count, ratio);
                    module.SetBurst(i, burst);
                }
                if (ratio < 1f && factor > 0f)
                {
                    Thin(system, ratio);
                }
                scaled.Emission = emission;
            }

            bool hide = factor <= 0f;
            if (scaled.Renderer != null && hide != scaled.Hidden)
            {
                // Only shown again if it was hidden here
                scaled.Renderer.enabled = !hide;
                scaled.Hidden = hide;
            }
            scaled.Applied = factor;
        }

        private static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve curve, float ratio)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new ParticleSystem.MinMaxCurve(curve.constant * ratio);
                case ParticleSystemCurveMode.TwoConstants:
                    return new ParticleSystem.MinMaxCurve(curve.constantMin * ratio, curve.constantMax * ratio);
                default:
                    curve.curveMultiplier *= ratio;
                    return curve;
            }
        }

        // Keeps that share of the particles already there, spread evenly
        private static void Thin(ParticleSystem system, float share)
        {
            int count = system.particleCount;
            if (count == 0)
            {
                return;
            }
            if (_particles.Length < count)
            {
                _particles = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
            }
            int read = system.GetParticles(_particles, count);
            int kept = 0;
            float carry = 0f;
            for (int i = 0; i < read; i++)
            {
                carry += share;
                if (carry >= 1f)
                {
                    carry -= 1f;
                    _particles[kept++] = _particles[i];
                }
            }
            system.SetParticles(_particles, kept);
        }
    }
}
