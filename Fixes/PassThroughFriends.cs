using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Tamed creatures and other players push the player around: the physics engine pushes characters apart when they
    // overlap, and a wolf or a lox following you walks right into you. At a crafting station, being pushed further than
    // its use distance (2 m for most) makes the game close the crafting window (Player.UpdateStations), which some mods'
    // windows don't handle well.
    // This makes the physics engine ignore collisions between the local player and them (Physics.IgnoreCollision, which
    // the game itself uses for what a player sits on or rides), checked every second. Only collisions: attacks,
    // projectiles and interacting find characters with queries of their own, which this doesn't change. Each player's
    // game does it for its own player, the only one it moves.
    [HarmonyPatch]
    internal static class PassThroughFriends
    {
        private const float CheckInterval = 1f;

        // The colliders the local player ignores collisions with
        private static readonly HashSet<Collider> Ignored = new HashSet<Collider>();
        private static readonly HashSet<Collider> Wanted = new HashSet<Collider>();
        private static readonly List<Collider> Colliders = new List<Collider>();
        private static readonly List<Collider> Stale = new List<Collider>();

        private static Player _player;
        private static bool _wasAttached;
        private static float _nextCheck;

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        [HarmonyPostfix]
        private static void CheckCollisions(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }

            // A new player (after a respawn) starts with nothing ignored
            if (__instance != _player)
            {
                _player = __instance;
                _wasAttached = false;
                Ignored.Clear();
                _nextCheck = 0f;
            }

            // Getting off a seat or a mount turns collisions back on with everything it was attached to (Player.AttachStop,
            // and Monstrum's own copy of it for its mounts), a tame included
            bool attached = __instance.IsAttached();
            if (_wasAttached && !attached)
            {
                Ignored.Clear();
                _nextCheck = 0f;
            }
            _wasAttached = attached;

            if (Time.time < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.time + CheckInterval;

            Refresh(__instance);
        }

        private static void Refresh(Player player)
        {
            Collider own = player.m_collider;
            Wanted.Clear();

            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == player || !character.isActiveAndEnabled || !PassThrough(character))
                {
                    continue;
                }

                // All of them, as Player.AttachStart does for a mount: some creatures have more than their capsule
                character.GetComponentsInChildren(Colliders);
                foreach (Collider collider in Colliders)
                {
                    if (collider.isTrigger || !collider.enabled)
                    {
                        continue;
                    }

                    Wanted.Add(collider);
                    if (Ignored.Add(collider))
                    {
                        Physics.IgnoreCollision(own, collider, true);
                    }
                }
            }

            // Setting turned off, untamed, gone, or disabled (a disabled collider forgets what it ignores, so it's done
            // again if it comes back)
            foreach (Collider collider in Ignored)
            {
                if (!Wanted.Contains(collider))
                {
                    Stale.Add(collider);
                }
            }

            foreach (Collider collider in Stale)
            {
                Ignored.Remove(collider);
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                {
                    Physics.IgnoreCollision(own, collider, false);
                }
            }
            Stale.Clear();
        }

        private static bool PassThrough(Character character)
        {
            return character.IsPlayer() ? Plugin.PassThroughPlayers : Plugin.PassThroughTames && character.IsTamed();
        }
    }
}
