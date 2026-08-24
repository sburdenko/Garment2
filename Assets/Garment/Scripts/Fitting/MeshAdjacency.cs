using System.Collections.Generic;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Vertex neighbour lists welded by position. UV seams split vertices that are physically
    /// the same point; smoothing anything across an unwelded seam tears the mesh open there.
    /// </summary>
    public static class MeshAdjacency
    {
        private const float WeldPrecision = 10000f;

        public static int[][] Build(Vector3[] vertices, int[] triangles)
        {
            var representative = new int[vertices.Length];
            var byPosition = new Dictionary<Vector3Int, int>(vertices.Length);
            var coincident = new Dictionary<int, List<int>>();

            for (int i = 0; i < vertices.Length; i++)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(vertices[i].x * WeldPrecision),
                    Mathf.RoundToInt(vertices[i].y * WeldPrecision),
                    Mathf.RoundToInt(vertices[i].z * WeldPrecision));

                if (!byPosition.TryGetValue(key, out int leader))
                {
                    leader = i;
                    byPosition[key] = leader;
                    coincident[leader] = new List<int>(2);
                }
                representative[i] = leader;
                coincident[leader].Add(i);
            }

            var sets = new HashSet<int>[vertices.Length];
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Connect(sets, representative[triangles[t]], representative[triangles[t + 1]]);
                Connect(sets, representative[triangles[t + 1]], representative[triangles[t + 2]]);
                Connect(sets, representative[triangles[t + 2]], representative[triangles[t]]);
            }

            var adjacency = new int[vertices.Length][];
            foreach (var pair in coincident)
            {
                var set = sets[pair.Key];
                var expanded = new List<int>(set == null ? 0 : set.Count * 2);
                if (set != null)
                {
                    foreach (int leader in set)
                        if (coincident.TryGetValue(leader, out var members)) expanded.AddRange(members);
                }
                var shared = expanded.ToArray();
                foreach (int member in pair.Value) adjacency[member] = shared;
            }
            return adjacency;
        }

        private static void Connect(HashSet<int>[] sets, int a, int b)
        {
            if (a == b) return;
            (sets[a] ??= new HashSet<int>()).Add(b);
            (sets[b] ??= new HashSet<int>()).Add(a);
        }
    }
}
