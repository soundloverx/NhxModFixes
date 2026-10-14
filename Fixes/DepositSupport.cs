using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Ore deposits made of chunks (copper, silver, big rocks) keep hanging in the air after being dug free.
    // When a chunk breaks, the game drops every chunk that nothing holds up any more (MineRock5.UpdateSupport): a chunk
    // is held up by whatever overlaps its box, the ground, a held-up chunk or almost anything solid. That box is the
    // chunk's world-aligned bounding box (SetupColliders), 4 times a copper chunk's volume on average and up to 120 times
    // for long diagonal ones, so an empty corner of it can still reach the ground or a neighbour.
    // The game's box already has a rotation, always left at none. This fits each box to its chunk instead: the smallest
    // box lined up with one of the chunk's faces (2.6 times a copper chunk's volume on average), a few cm bigger so chunks
    // resting on each other still touch. A chunk whose box can't be fitted, or wouldn't be smaller, keeps the game's.
    // Only the game that controls a deposit runs this, the first time one of its chunks breaks.
    [HarmonyPatch]
    internal static class DepositSupport
    {
        private const float Margin = 0.05f;

        // In the mesh's own space; null when the mesh can't be read
        private struct FittedBox
        {
            public Vector3 Center;
            public Quaternion Rotation;
            public Vector3 HalfSize;
        }

        // The deposits of a kind share their chunks' meshes
        private static readonly Dictionary<Mesh, FittedBox?> _boxes = new Dictionary<Mesh, FittedBox?>();

        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.SetupColliders))]
        [HarmonyPostfix]
        private static void FitBoxes(MineRock5 __instance)
        {
            Vector3 position = __instance.transform.position;
            int chunks = 0;
            int fitted = 0;
            float gameVolume = 0f;
            float fittedVolume = 0f;
            foreach (MineRock5.HitArea area in __instance.m_hitAreas)
            {
                // Broken chunks are never checked again, and their colliders are off
                if (area.m_health <= 0f)
                {
                    continue;
                }

                chunks++;
                float volume = Volume(area.m_bound.m_size);
                gameVolume += volume;
                if (!TryFit(area.m_collider, out Vector3 center, out Quaternion rotation, out Vector3 halfSize) ||
                    Volume(halfSize) >= volume)
                {
                    fittedVolume += volume;
                    continue;
                }

                area.m_bound.m_pos = center - position;
                area.m_bound.m_rot = rotation;
                area.m_bound.m_size = halfSize;
                fitted++;
                fittedVolume += Volume(halfSize);
            }

            if (chunks > 0)
            {
                Plugin.Log.LogInfo(
                    $"Fitted the support boxes of {Utils.GetPrefabName(__instance.gameObject)}: {fitted} of {chunks} chunks, " +
                    $"{fittedVolume / Mathf.Max(gameVolume, 1e-6f):P0} of the game's volume.");
            }
        }

        private static float Volume(Vector3 halfSize) => 8f * halfSize.x * halfSize.y * halfSize.z;

        private static bool TryFit(Collider collider, out Vector3 center, out Quaternion rotation, out Vector3 halfSize)
        {
            center = default;
            rotation = default;
            halfSize = default;

            Mesh mesh = (collider as MeshCollider)?.sharedMesh;
            if (mesh == null)
            {
                return false;
            }

            if (!_boxes.TryGetValue(mesh, out FittedBox? box))
            {
                box = Fit(mesh);
                _boxes[mesh] = box;
            }

            // A uniform scale keeps the box's shape (chunks and deposits are only scaled uniformly)
            Transform transform = collider.transform;
            Vector3 scale = transform.lossyScale;
            if (box == null || scale.x <= 0f ||
                Mathf.Abs(scale.y - scale.x) > scale.x * 0.01f || Mathf.Abs(scale.z - scale.x) > scale.x * 0.01f)
            {
                return false;
            }

            center = transform.TransformPoint(box.Value.Center);
            rotation = transform.rotation * box.Value.Rotation;
            halfSize = box.Value.HalfSize * scale.x + Vector3.one * Margin;
            return true;
        }

        // Tries each face's normal (and the mesh's own axes) as one of the box's axes, with the smallest rectangle
        // around the chunk seen along it, which has a side along an edge of the chunk's outline (rotating calipers)
        private static FittedBox? Fit(Mesh mesh)
        {
            if (!mesh.isReadable)
            {
                return null;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices.Length < 4)
            {
                return null;
            }

            List<Vector3> axes = new List<Vector3> { Vector3.right, Vector3.up, Vector3.forward };
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 normal = Vector3.Cross(vertices[triangles[i + 1]] - a, vertices[triangles[i + 2]] - a);
                float length = normal.magnitude;
                if (length < 1e-8f)
                {
                    continue;
                }

                normal /= length;
                if (!axes.Exists(axis => Mathf.Abs(Vector3.Dot(axis, normal)) > 0.9999f))
                {
                    axes.Add(normal);
                }
            }

            Vector2[] points = new Vector2[vertices.Length];
            float bestVolume = float.MaxValue;
            FittedBox best = default;
            foreach (Vector3 z in axes)
            {
                Vector3 x = Vector3.Cross(z, Mathf.Abs(z.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 y = Vector3.Cross(z, x);
                float zMin = float.MaxValue;
                float zMax = float.MinValue;
                for (int i = 0; i < vertices.Length; i++)
                {
                    points[i] = new Vector2(Vector3.Dot(vertices[i], x), Vector3.Dot(vertices[i], y));
                    float d = Vector3.Dot(vertices[i], z);
                    zMin = Mathf.Min(zMin, d);
                    zMax = Mathf.Max(zMax, d);
                }

                List<Vector2> outline = ConvexHull(points);
                for (int i = 0; i < outline.Count; i++)
                {
                    Vector2 edge = outline[(i + 1) % outline.Count] - outline[i];
                    if (edge.sqrMagnitude < 1e-12f)
                    {
                        continue;
                    }

                    Vector2 u = edge.normalized;
                    Vector2 v = new Vector2(-u.y, u.x);
                    float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
                    foreach (Vector2 p in outline)
                    {
                        float du = Vector2.Dot(p, u);
                        float dv = Vector2.Dot(p, v);
                        uMin = Mathf.Min(uMin, du);
                        uMax = Mathf.Max(uMax, du);
                        vMin = Mathf.Min(vMin, dv);
                        vMax = Mathf.Max(vMax, dv);
                    }

                    float volume = (uMax - uMin) * (vMax - vMin) * (zMax - zMin);
                    if (volume >= bestVolume)
                    {
                        continue;
                    }

                    // The box's x axis is u, y is v, z is z (x's sign doesn't matter, the box is symmetric)
                    Vector3 axisU = u.x * x + u.y * y;
                    Vector3 axisV = v.x * x + v.y * y;
                    bestVolume = volume;
                    best.Center = axisU * ((uMin + uMax) / 2f) + axisV * ((vMin + vMax) / 2f) + z * ((zMin + zMax) / 2f);
                    best.Rotation = Quaternion.LookRotation(z, axisV);
                    best.HalfSize = new Vector3(uMax - uMin, vMax - vMin, zMax - zMin) / 2f;
                }
            }

            return bestVolume < float.MaxValue ? best : (FittedBox?)null;
        }

        // Andrew's monotone chain, counterclockwise, without collinear points
        private static List<Vector2> ConvexHull(Vector2[] points)
        {
            Vector2[] sorted = (Vector2[])points.Clone();
            Array.Sort(sorted, (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            List<Vector2> hull = new List<Vector2>(sorted.Length + 1);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < sorted.Length; i++)
                {
                    Vector2 p = sorted[pass == 0 ? i : sorted.Length - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                    {
                        hull.RemoveAt(hull.Count - 1);
                    }

                    hull.Add(p);
                }

                // Each half ends where the other starts
                hull.RemoveAt(hull.Count - 1);
            }

            return hull;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}
