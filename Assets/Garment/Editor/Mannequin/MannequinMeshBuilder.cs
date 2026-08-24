using System.Collections.Generic;
using UnityEngine;

namespace Garment.EditorTools.Mannequin
{
    /// <summary>Builds a skinned tube-per-limb mesh over an existing bone hierarchy.</summary>
    public static class MannequinMeshBuilder
    {
        private const int Sides = 16;
        private const int Rings = 6;

        public static Mesh Build(Transform root, Transform[] bones, LimbSpec[] limbs)
        {
            var boneIndex = new Dictionary<string, int>(bones.Length);
            for (int i = 0; i < bones.Length; i++) boneIndex[bones[i].name] = i;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();

            foreach (var limb in limbs)
            {
                if (!boneIndex.TryGetValue(limb.From, out int from) || !boneIndex.TryGetValue(limb.To, out int to))
                {
                    Debug.LogWarning($"Mannequin: limb {limb.From}->{limb.To} references a missing bone.");
                    continue;
                }
                AppendTube(root, bones[from], bones[to], from, to, limb, vertices, uvs, weights, triangles);
            }

            var mesh = new Mesh { name = "MannequinBody" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();

            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                bindposes[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
            mesh.bindposes = bindposes;

            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendTube(
            Transform root, Transform from, Transform to, int fromIndex, int toIndex, LimbSpec limb,
            List<Vector3> vertices, List<Vector2> uvs, List<BoneWeight> weights, List<int> triangles)
        {
            Vector3 start = root.InverseTransformPoint(from.position);
            Vector3 end = root.InverseTransformPoint(to.position);
            Vector3 axis = end - start;
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();

            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) > 0.9f ? Vector3.up : Vector3.forward;
            Vector3 right = Vector3.Cross(axis, reference).normalized;
            Vector3 depth = Vector3.Cross(right, axis).normalized;

            int baseVertex = vertices.Count;

            for (int ring = 0; ring <= Rings; ring++)
            {
                float t = (float)ring / Rings;
                Vector3 center = Vector3.Lerp(start, end, t);
                float radius = Mathf.Lerp(limb.RadiusFrom, limb.RadiusTo, t);
                float blend = Mathf.SmoothStep(0f, 1f, t);

                var weight = new BoneWeight
                {
                    boneIndex0 = fromIndex,
                    weight0 = 1f - blend,
                    boneIndex1 = toIndex,
                    weight1 = blend
                };

                for (int side = 0; side <= Sides; side++)
                {
                    float angle = (float)side / Sides * Mathf.PI * 2f;
                    Vector3 offset = right * (Mathf.Cos(angle) * radius)
                                   + depth * (Mathf.Sin(angle) * radius * limb.DepthScale);
                    vertices.Add(center + offset);
                    uvs.Add(new Vector2((float)side / Sides, t));
                    weights.Add(weight);
                }
            }

            int stride = Sides + 1;
            for (int ring = 0; ring < Rings; ring++)
            {
                for (int side = 0; side < Sides; side++)
                {
                    int a = baseVertex + ring * stride + side;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }
    }
}
