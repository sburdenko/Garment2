using System.Collections.Generic;
using UnityEngine;

namespace ClothLab
{
    /// <summary>
    /// A uniform bucket grid over a triangle list, for finding the surface point nearest to an
    /// arbitrary position without testing every triangle.
    /// </summary>
    public sealed class SurfaceGrid
    {
        private readonly Dictionary<long, List<int>> cells;
        private readonly float cell;

        private SurfaceGrid(Dictionary<long, List<int>> cells, float cell)
        {
            this.cells = cells;
            this.cell = cell;
        }

        public static SurfaceGrid Build(Vector3[] points, int[] triangles)
        {
            float edge = 0f;
            int sampled = 0;
            for (int t = 0; t < triangles.Length; t += 3, sampled++)
                edge += Vector3.Distance(points[triangles[t]], points[triangles[t + 1]]);
            float cell = sampled > 0 ? Mathf.Max(edge / sampled, 1e-4f) : 0.01f;

            var cells = new Dictionary<long, List<int>>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int triangle = t / 3;
                for (int corner = 0; corner < 3; corner++)
                {
                    long key = KeyOf(points[triangles[t + corner]], cell);
                    if (!cells.TryGetValue(key, out List<int> bucket))
                        cells[key] = bucket = new List<int>();
                    if (bucket.Count == 0 || bucket[bucket.Count - 1] != triangle) bucket.Add(triangle);
                }
            }
            return new SurfaceGrid(cells, cell);
        }

        /// <summary>Widens the search ring until something is found, so a sparse area still binds.</summary>
        public int NearestTriangle(Vector3 point, Vector3[] points, int[] triangles, out Vector3 onSurface)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            onSurface = point;

            for (int ring = 1; ring <= 6 && best < 0; ring++)
            {
                Vector3Int centre = CellOf(point, cell);
                for (int x = -ring; x <= ring; x++)
                for (int y = -ring; y <= ring; y++)
                for (int z = -ring; z <= ring; z++)
                {
                    if (!cells.TryGetValue(Key(centre.x + x, centre.y + y, centre.z + z), out List<int> bucket))
                        continue;
                    foreach (int triangle in bucket)
                    {
                        int t = triangle * 3;
                        Vector3 candidate = ClosestOnTriangle(point,
                            points[triangles[t]], points[triangles[t + 1]], points[triangles[t + 2]]);
                        float distance = (candidate - point).sqrMagnitude;
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        best = triangle;
                        onSurface = candidate;
                    }
                }
            }
            return best;
        }

        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            float denominator = 1f / (va + vb + vc);
            return a + ab * (vb * denominator) + ac * (vc * denominator);
        }

        private static Vector3Int CellOf(Vector3 p, float cell)
        {
            return new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
        }

        private static long KeyOf(Vector3 p, float cell)
        {
            Vector3Int c = CellOf(p, cell);
            return Key(c.x, c.y, c.z);
        }

        private static long Key(int x, int y, int z)
        {
            return ((long)(x + 32768) << 34) | ((long)(y + 32768) << 17) | (long)(z + 32768);
        }
    }
}
