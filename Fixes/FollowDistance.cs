using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // A tame following a player walks up to them and stops once its feet are 3 m from theirs, center to center, so a big
    // one stands against them. A flying one (RtD's fairies) flies to a point a few meters above the player's feet and
    // stops within 1 m of it: it parks over their head, where it takes the interact key.
    // This keeps a gap between their bodies instead, around that same point for a flying one, which it reaches from its
    // own side (beside the player, not over them). When the player walks closer than half the gap, the tame steps back,
    // only where there's room and only while the player keeps walking toward it, so that walking up to it to pet or
    // command it works: it stops when they do.
    // The game only makes a tame follow when it has nothing to fight (BaseAI.Follow, from MonsterAI.UpdateAI), and
    // that's left alone while it has a target. The game controlling a tame runs its AI, so everyone needs the server's
    // settings.
    [HarmonyPatch]
    internal static class FollowDistance
    {
        // The game runs beyond 10 m, 7 m past where it stops
        private const float RunBeyond = 7f;
        // Speed toward the tame above which the player counts as walking up to it (crouching is 2 m/s)
        private const float MovingSpeed = 0.5f;
        // A step back ends after this long, wherever the tame is
        private const float StepBackTime = 3f;
        // Wait before looking for room again
        private const float RetryInterval = 1f;
        // A walking tame only steps back along a path at most this much longer than the straight line (plus 1 m), so
        // it doesn't go around a wall or down a slope to get there
        private const float MaxDetour = 1.5f;
        // Directions tried, away from the player first
        private static readonly float[] Angles = { 0f, 45f, -45f, 90f, -90f };

        private class State
        {
            public bool SteppingBack;
            public Vector3 Target;
            public float Until;
            public float NextTry;
        }

        private static readonly ConditionalWeakTable<BaseAI, State> States = new ConditionalWeakTable<BaseAI, State>();
        private static readonly List<Vector3> Path = new List<Vector3>();

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.Follow))]
        [HarmonyPrefix]
        private static bool KeepDistance(BaseAI __instance, GameObject go, float dt)
        {
            float distance = Plugin.FollowDistance;
            Player player = go != null ? go.GetComponent<Player>() : null;
            if (distance <= 0f || player == null)
            {
                return true;
            }

            // Fighting: the game also follows while a tame has a target but no attack ready
            if (__instance is MonsterAI monsterAI && (monsterAI.m_targetCreature != null || monsterAI.m_targetStatic != null))
            {
                return true;
            }

            Character character = __instance.m_character;
            bool flying = character.m_flying;
            Vector3 point = FollowPoint(__instance, go.transform.position, flying);
            float radii = Radius(character) + Radius(player);
            Vector3 position = __instance.transform.position;
            float gap = Vector3.Distance(position, point) - radii;
            State state = States.GetOrCreateValue(__instance);

            if (gap >= distance)
            {
                state.SteppingBack = false;
                bool run = gap > distance + RunBeyond;
                // A flying one goes to its spot beside the player, on its side
                Vector3 target = flying ? point + Away(position, point, go.transform) * (distance + radii) : point;
                __instance.MoveTo(dt, target, 0f, run);
                return false;
            }

            if (Plugin.StepBackFromPlayers && PlayerApproaching(player, position) && character.GetStandingOnShip() == null)
            {
                if (state.SteppingBack)
                {
                    if (Time.time < state.Until && !__instance.MoveTo(dt, state.Target, 0f, false))
                    {
                        return false;
                    }
                    StopSteppingBack(state);
                }
                else if (gap < distance / 2f && Time.time >= state.NextTry)
                {
                    if (FindRoom(__instance, player, point, flying, distance + radii, out Vector3 target))
                    {
                        state.SteppingBack = true;
                        state.Target = target;
                        state.Until = Time.time + StepBackTime;
                        __instance.MoveTo(dt, target, 0f, false);
                        return false;
                    }
                    state.NextTry = Time.time + RetryInterval;
                }
            }
            else if (state.SteppingBack)
            {
                StopSteppingBack(state);
            }

            __instance.StopMoving();
            return false;
        }

        private static void StopSteppingBack(State state)
        {
            state.SteppingBack = false;
            state.NextTry = Time.time + RetryInterval / 2f;
        }

        // Where the game sends a following tame: the player's feet, or for a flying one the same point raised to its
        // lowest altitude above what's under it (BaseAI.MoveTo)
        private static Vector3 FollowPoint(BaseAI ai, Vector3 feet, bool flying)
        {
            if (flying && ai.GetSolidHeight(feet, 0f, ai.m_flyAltitudeMin * 2f, out float height))
            {
                feet.y = Mathf.Max(feet.y, height + ai.m_flyAltitudeMin);
            }
            return feet;
        }

        // Horizontal direction from the point to the tame, or the player's right when the tame is right above them
        private static Vector3 Away(Vector3 position, Vector3 point, Transform player)
        {
            Vector3 away = position - point;
            away.y = 0f;
            return away.sqrMagnitude > 0.01f ? away.normalized : player.right;
        }

        // A spot at the given distance from the point, away from the player first, that the tame can reach: along a
        // short path for a walking one, through open air for a flying one
        private static bool FindRoom(BaseAI ai, Player player, Vector3 point, bool flying, float distance, out Vector3 target)
        {
            Vector3 position = ai.transform.position;
            Vector3 away = Away(position, point, player.transform);

            foreach (float angle in Angles)
            {
                target = point + Quaternion.Euler(0f, angle, 0f) * away * distance;
                if (flying ? HaveAir(ai, target, out target) : HaveShortPath(ai, position, target))
                {
                    return true;
                }
            }

            target = position;
            return false;
        }

        private static bool HaveShortPath(BaseAI ai, Vector3 from, Vector3 to)
        {
            // havePath: the end has to be within 1 m of the navmesh, not wherever the nearest walkable spot is
            if (!Pathfinding.instance.GetPath(from, to, Path, ai.m_pathAgentType, requireFullPath: true, cleanup: true, havePath: true)
                || Path.Count == 0)
            {
                return false;
            }

            float length = 0f;
            for (int i = 1; i < Path.Count; i++)
            {
                length += Vector3.Distance(Path[i - 1], Path[i]);
            }
            return length <= Utils.DistanceXZ(from, to) * MaxDetour + 1f;
        }

        // Raised as the game raises a flying tame's destination, then nothing solid on the way there
        private static bool HaveAir(BaseAI ai, Vector3 target, out Vector3 raised)
        {
            raised = FollowPoint(ai, target, true);
            Vector3 center = ai.m_character.GetCenterPoint();
            Vector3 offset = raised - center;
            return !Physics.SphereCast(center, Radius(ai.m_character), offset.normalized, out _, offset.magnitude, BaseAI.m_solidRayMask);
        }

        // Walking toward the tame, or past it (until it's alongside), or under it for a flying one right above them
        private static bool PlayerApproaching(Player player, Vector3 position)
        {
            // The player's game shares its body's velocity with the others
            Vector3 velocity = player.GetVelocity();
            velocity.y = 0f;
            Vector3 toTame = position - player.transform.position;
            toTame.y = 0f;
            float speed = toTame.sqrMagnitude > 0.25f ? Vector3.Dot(velocity, toTame.normalized) : velocity.magnitude;
            return speed > MovingSpeed;
        }

        // The body's horizontal radius (Character.GetRadius multiplies a creature's by the length of its scale vector,
        // 1.7 times too much for an unscaled one)
        private static float Radius(Character character)
        {
            CapsuleCollider collider = character.m_collider;
            if (collider == null)
            {
                return 0.5f;
            }
            Vector3 scale = collider.transform.lossyScale;
            return collider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
    }
}
