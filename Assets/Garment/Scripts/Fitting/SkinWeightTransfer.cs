using System;
using System.Collections.Generic;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Gives a static garment mesh the body's skinning: each garment vertex inherits a blend
    /// of the bone weights of the body vertices nearest to it in bind pose.
    /// </summary>
    public static class SkinWeightTransfer
    {
        private const int NeighbourCount = 4;
        private const int MaxInfluences = 4;
        private const float DistanceEpsilon = 1e-4f;

        public static BoneWeight[] Transfer(
            Vector3[] fittedGarmentVertices,
            Mesh garmentMesh,
            GarmentDefinition definition,
            Vector3[] bodyVertices,
            BoneWeight[] bodyWeights,
            VertexGrid bodyGrid,
            int smoothingIterations = 0,
            float armThresholdX = 0f)
        {
            var result = new BoneWeight[fittedGarmentVertices.Length];
            var neighbours = new List<(int index, float distance)>(32);
            var accumulator = new Dictionary<int, float>(8);

            for (int i = 0; i < fittedGarmentVertices.Length; i++)
            {
                var vertex = fittedGarmentVertices[i];

                // A sleeve vertex must inherit the arm's weights, not the chest's. Nearest
                // neighbours don't know that: a loose sleeve hangs close to the flank, so part
                // of it picks up torso bones and the sleeve then clings to the body instead of
                // following the arm. The suppression fades in across the shoulder so the seam
                // stays smooth — a hard cutoff makes the fabric bunch and tear at the shoulder.
                float armWeight = armThresholdX > 0f
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(armThresholdX - 0.02f, armThresholdX + 0.06f, Mathf.Abs(vertex.x)))
                    : 0f;

                if (armWeight > 0.01f)
                {
                    float side = Mathf.Sign(vertex.x);
                    result[i] = BlendFiltered(vertex, bodyVertices, bodyWeights, bodyGrid, neighbours, accumulator,
                        candidate => Mathf.Sign(candidate.x) == side && Mathf.Abs(candidate.x) > armThresholdX - 0.02f,
                        1f - armWeight);
                }
                else
                {
                    result[i] = Blend(vertex, bodyWeights, bodyGrid, neighbours, accumulator);
                }
            }

            if (smoothingIterations > 0)
            {
                var adjacency = MeshAdjacency.Build(fittedGarmentVertices, garmentMesh.triangles);
                Smooth(result, adjacency, smoothingIterations, accumulator);
            }

            // A body has no rigid hardware; only garments pass a definition. Rigid parts are
            // assigned after smoothing so they stay rigid.
            if (definition != null)
                ApplyRigidSubmeshes(fittedGarmentVertices, garmentMesh, definition, bodyWeights, bodyGrid, result, neighbours, accumulator);
            return result;
        }

        private static void ApplyRigidSubmeshes(
            Vector3[] vertices, Mesh mesh, GarmentDefinition definition, BoneWeight[] bodyWeights,
            VertexGrid bodyGrid, BoneWeight[] result,
            List<(int index, float distance)> neighbours, Dictionary<int, float> accumulator)
        {
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (definition.RoleOf(submesh) != SubmeshRole.Rigid) continue;

                var indices = mesh.GetTriangles(submesh);
                if (indices.Length == 0) continue;

                var centroid = Vector3.zero;
                var unique = new HashSet<int>();
                foreach (int index in indices)
                {
                    if (unique.Add(index)) centroid += vertices[index];
                }
                centroid /= unique.Count;

                var rigidWeight = Blend(centroid, bodyWeights, bodyGrid, neighbours, accumulator);
                foreach (int index in unique) result[index] = rigidWeight;
            }
        }

        /// <summary>
        /// Nearest-neighbour weights change abruptly where two body parts meet — across an
        /// armpit a vertex can jump from fully torso to fully arm, and the garment tears open
        /// as the arm swings. Averaging over neighbours turns that step into a gradient.
        /// </summary>
        private static void Smooth(BoneWeight[] weights, int[][] adjacency, int iterations, Dictionary<int, float> accumulator)
        {
            var buffer = new BoneWeight[weights.Length];

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                for (int i = 0; i < weights.Length; i++)
                {
                    var neighbours = adjacency[i];
                    if (neighbours == null || neighbours.Length == 0)
                    {
                        buffer[i] = weights[i];
                        continue;
                    }

                    accumulator.Clear();
                    Accumulate(accumulator, weights[i], 1f);
                    foreach (int neighbour in neighbours) Accumulate(accumulator, weights[neighbour], 1f);
                    buffer[i] = TopInfluences(accumulator);
                }
                Array.Copy(buffer, weights, weights.Length);
            }
        }

        private static BoneWeight BlendFiltered(
            Vector3 point, Vector3[] bodyVertices, BoneWeight[] bodyWeights, VertexGrid bodyGrid,
            List<(int index, float distance)> neighbours, Dictionary<int, float> accumulator,
            System.Func<Vector3, bool> accept, float rejectedInfluence)
        {
            bodyGrid.QueryNearest(point, NeighbourCount * 4, neighbours);
            accumulator.Clear();

            int used = 0;
            foreach (var (index, distance) in neighbours)
            {
                float influence = 1f / Mathf.Max(distance, DistanceEpsilon);
                if (!accept(bodyVertices[index]))
                {
                    if (rejectedInfluence <= 0f) continue;
                    influence *= rejectedInfluence;
                }
                Accumulate(accumulator, bodyWeights[index], influence);
                if (++used >= NeighbourCount) break;
            }

            // Nothing usable nearby — fall back to the unfiltered blend.
            if (used == 0) return Blend(point, bodyWeights, bodyGrid, neighbours, accumulator);
            return TopInfluences(accumulator);
        }

        private static BoneWeight Blend(
            Vector3 point, BoneWeight[] bodyWeights, VertexGrid bodyGrid,
            List<(int index, float distance)> neighbours, Dictionary<int, float> accumulator)
        {
            bodyGrid.QueryNearest(point, NeighbourCount, neighbours);
            accumulator.Clear();

            if (neighbours.Count == 0) return default;

            foreach (var (index, distance) in neighbours)
            {
                float influence = 1f / Mathf.Max(distance, DistanceEpsilon);
                Accumulate(accumulator, bodyWeights[index], influence);
            }

            return TopInfluences(accumulator);
        }

        private static void Accumulate(Dictionary<int, float> accumulator, BoneWeight source, float influence)
        {
            Add(accumulator, source.boneIndex0, source.weight0 * influence);
            Add(accumulator, source.boneIndex1, source.weight1 * influence);
            Add(accumulator, source.boneIndex2, source.weight2 * influence);
            Add(accumulator, source.boneIndex3, source.weight3 * influence);
        }

        private static void Add(Dictionary<int, float> accumulator, int bone, float weight)
        {
            if (weight <= 0f) return;
            accumulator.TryGetValue(bone, out float existing);
            accumulator[bone] = existing + weight;
        }

        private static BoneWeight TopInfluences(Dictionary<int, float> accumulator)
        {
            Span<int> bones = stackalloc int[MaxInfluences];
            Span<float> values = stackalloc float[MaxInfluences];
            for (int i = 0; i < MaxInfluences; i++) { bones[i] = 0; values[i] = 0f; }

            foreach (var pair in accumulator)
            {
                for (int slot = 0; slot < MaxInfluences; slot++)
                {
                    if (pair.Value <= values[slot]) continue;
                    for (int shift = MaxInfluences - 1; shift > slot; shift--)
                    {
                        bones[shift] = bones[shift - 1];
                        values[shift] = values[shift - 1];
                    }
                    bones[slot] = pair.Key;
                    values[slot] = pair.Value;
                    break;
                }
            }

            float total = values[0] + values[1] + values[2] + values[3];
            if (total <= 0f) return default;

            return new BoneWeight
            {
                boneIndex0 = bones[0], weight0 = values[0] / total,
                boneIndex1 = bones[1], weight1 = values[1] / total,
                boneIndex2 = bones[2], weight2 = values[2] / total,
                boneIndex3 = bones[3], weight3 = values[3] / total
            };
        }
    }
}
