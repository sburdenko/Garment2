using System.Collections.Generic;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Pushes garment vertices out of the body in bind pose and spreads the correction over
    /// neighbouring vertices so the silhouette stays smooth. Runs once when a garment is put
    /// on, so it costs nothing per frame.
    /// </summary>
    public static class PenetrationResolver
    {
        public static Vector3[] Resolve(
            Vector3[] vertices,
            int[] triangles,
            Vector3[] bodyVertices,
            Vector3[] bodyNormals,
            VertexGrid bodyGrid,
            float clearance,
            int passes,
            int smoothingIterations)
        {
            if (vertices == null || vertices.Length == 0) return vertices;

            // Adjacency is only needed once something actually has to move, and building it over
            // a 45k-vertex garment is the expensive part. A well-fitted garment never pays it.
            int[][] adjacency = null;
            var result = (Vector3[])vertices.Clone();
            var displacement = new Vector3[vertices.Length];
            var neighbours = new List<(int index, float distance)>(8);

            for (int pass = 0; pass < passes; pass++)
            {
                bool anyPenetration = false;

                for (int i = 0; i < result.Length; i++)
                {
                    displacement[i] = Vector3.zero;
                    bodyGrid.QueryNearest(result[i], 1, neighbours);
                    if (neighbours.Count == 0) continue;

                    int nearest = neighbours[0].index;
                    var normal = bodyNormals[nearest];
                    float signed = Vector3.Dot(result[i] - bodyVertices[nearest], normal);
                    if (signed >= clearance) continue;

                    displacement[i] = normal * (clearance - signed);
                    anyPenetration = true;
                }

                if (!anyPenetration) break;

                if (adjacency == null) adjacency = MeshAdjacency.Build(vertices, triangles);
                Smooth(displacement, adjacency, smoothingIterations);
                for (int i = 0; i < result.Length; i++) result[i] += displacement[i];
            }

            return result;
        }

        private static void Smooth(Vector3[] displacement, int[][] adjacency, int iterations)
        {
            if (iterations <= 0) return;
            var buffer = new Vector3[displacement.Length];

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                for (int i = 0; i < displacement.Length; i++)
                {
                    var neighbours = adjacency[i];
                    if (neighbours == null || neighbours.Length == 0)
                    {
                        buffer[i] = displacement[i];
                        continue;
                    }

                    var sum = displacement[i];
                    foreach (int neighbour in neighbours) sum += displacement[neighbour];
                    buffer[i] = sum / (neighbours.Length + 1);
                }
                System.Array.Copy(buffer, displacement, displacement.Length);
            }
        }

    }
}
