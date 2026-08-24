using System.Collections.Generic;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Uniform-grid nearest-neighbour index over a static point cloud.
    /// Brute force over a 20k-vertex garment against a body mesh is 50M distance tests per
    /// garment change, which is too slow to do while the user is switching outfits.
    /// </summary>
    public sealed class VertexGrid
    {
        private readonly Dictionary<Vector3Int, List<int>> cells;
        private readonly Vector3[] points;
        private readonly float cellSize;

        public VertexGrid(Vector3[] points, float cellSize = 0f)
        {
            this.points = points;
            this.cellSize = cellSize > 0f ? cellSize : AutoCellSize(points);
            cells = new Dictionary<Vector3Int, List<int>>(points.Length / 4 + 1);

            for (int i = 0; i < points.Length; i++)
            {
                var cell = CellOf(points[i]);
                if (!cells.TryGetValue(cell, out var bucket))
                {
                    bucket = new List<int>(8);
                    cells[cell] = bucket;
                }
                bucket.Add(i);
            }
        }

        /// <summary>
        /// Cells should hold a handful of points each. Too coarse and every query sorts
        /// thousands of candidates; the surface of a 40k-vertex body needs far finer cells
        /// than a 2k-vertex stand-in.
        /// </summary>
        private static float AutoCellSize(Vector3[] points)
        {
            if (points.Length < 8) return 0.05f;

            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);

            float spread = bounds.size.magnitude;
            return Mathf.Clamp(2f * spread / Mathf.Sqrt(points.Length), 0.008f, 0.08f);
        }

        /// <summary>Fills <paramref name="neighbours"/> with up to k indices, nearest first.</summary>
        public void QueryNearest(Vector3 point, int k, List<(int index, float distance)> neighbours)
        {
            neighbours.Clear();
            var origin = CellOf(point);

            for (int ring = 0; ring <= 8; ring++)
            {
                CollectRing(origin, ring, point, neighbours);
                if (neighbours.Count >= k)
                {
                    neighbours.Sort((a, b) => a.distance.CompareTo(b.distance));
                    // One extra ring guarantees no closer point hides just outside the searched box.
                    if (ring > 0 && neighbours[Mathf.Min(k, neighbours.Count) - 1].distance <= ring * cellSize)
                        break;
                }
            }

            neighbours.Sort((a, b) => a.distance.CompareTo(b.distance));
            if (neighbours.Count > k) neighbours.RemoveRange(k, neighbours.Count - k);
        }

        private void CollectRing(Vector3Int origin, int ring, Vector3 point, List<(int, float)> neighbours)
        {
            for (int x = -ring; x <= ring; x++)
            for (int y = -ring; y <= ring; y++)
            for (int z = -ring; z <= ring; z++)
            {
                bool onShell = Mathf.Abs(x) == ring || Mathf.Abs(y) == ring || Mathf.Abs(z) == ring;
                if (!onShell) continue;

                var cell = new Vector3Int(origin.x + x, origin.y + y, origin.z + z);
                if (!cells.TryGetValue(cell, out var bucket)) continue;

                foreach (int index in bucket)
                    neighbours.Add((index, Vector3.Distance(points[index], point)));
            }
        }

        private Vector3Int CellOf(Vector3 point) => new Vector3Int(
            Mathf.FloorToInt(point.x / cellSize),
            Mathf.FloorToInt(point.y / cellSize),
            Mathf.FloorToInt(point.z / cellSize));
    }
}
