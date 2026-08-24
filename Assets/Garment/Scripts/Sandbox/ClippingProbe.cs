using System.Collections.Generic;
using Garment.Body;
using Garment.Fitting;
using UnityEngine;

namespace Garment.Sandbox
{
    public readonly struct ClippingReport
    {
        public readonly int SampledVertices;
        public readonly int PenetratingVertices;
        public readonly float MaxDepth;

        public ClippingReport(int sampled, int penetrating, float maxDepth)
        {
            SampledVertices = sampled;
            PenetratingVertices = penetrating;
            MaxDepth = maxDepth;
        }

        public float Ratio => SampledVertices == 0 ? 0f : (float)PenetratingVertices / SampledVertices;
    }

    /// <summary>
    /// Measures how far garment vertices sink below the skin. Clipping is the failure the
    /// client asked about specifically, so it gets a number rather than an eyeball check.
    /// </summary>
    public sealed class ClippingProbe : MonoBehaviour
    {
        [SerializeField] private BodyRig body;
        [SerializeField, Range(0.1f, 2f)] private float interval = 0.4f;
        [SerializeField, Range(1, 32)] private int vertexStride = 6;
        [SerializeField, Range(0f, 0.02f)] private float tolerance = 0.002f;

        private readonly List<(int index, float distance)> neighbours = new List<(int index, float distance)>(8);
        private readonly Dictionary<SkinnedMeshRenderer, Mesh> bakeCache = new Dictionary<SkinnedMeshRenderer, Mesh>();
        private Mesh bodyBake;
        private float timer;

        public ClippingReport LatestReport { get; private set; }

        private void Awake()
        {
            EnsureInitialised();
        }

        private void EnsureInitialised()
        {
            if (body == null) body = FindFirstObjectByType<BodyRig>();
            if (bodyBake == null) bodyBake = new Mesh { name = "ClippingProbe_BodyBake" };
        }

        private void OnDestroy()
        {
            if (bodyBake != null) Destroy(bodyBake);
            foreach (var mesh in bakeCache.Values)
                if (mesh != null) Destroy(mesh);
        }

        private void Update()
        {
            timer += Time.unscaledDeltaTime;
            if (timer < interval) return;
            timer = 0f;
            LatestReport = Measure();
        }

        public ClippingReport Measure()
        {
            EnsureInitialised();
            var bodyMesh = body != null ? body.BodyMesh : null;
            if (bodyMesh == null) return default;

            bodyMesh.BakeMesh(bodyBake, true);
            var bodyVertices = bodyBake.vertices;
            var bodyNormals = bodyBake.normals;
            if (bodyVertices.Length == 0 || bodyNormals.Length != bodyVertices.Length) return default;

            // Baked vertices are local to each renderer; body and garment need a shared space.
            var bodyToWorld = bodyMesh.transform;
            for (int i = 0; i < bodyVertices.Length; i++)
            {
                bodyVertices[i] = bodyToWorld.TransformPoint(bodyVertices[i]);
                bodyNormals[i] = bodyToWorld.TransformDirection(bodyNormals[i]);
            }

            var grid = new VertexGrid(bodyVertices);
            int sampled = 0;
            int penetrating = 0;
            float maxDepth = 0f;

            foreach (var garment in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (garment == bodyMesh) continue;

                var bake = BakeOf(garment);
                garment.BakeMesh(bake, true);
                var vertices = bake.vertices;
                var garmentToWorld = garment.transform;

                for (int i = 0; i < vertices.Length; i += vertexStride)
                {
                    sampled++;
                    vertices[i] = garmentToWorld.TransformPoint(vertices[i]);
                    grid.QueryNearest(vertices[i], 1, neighbours);
                    if (neighbours.Count == 0) continue;

                    int nearest = neighbours[0].index;
                    float signed = Vector3.Dot(vertices[i] - bodyVertices[nearest], bodyNormals[nearest]);
                    if (signed >= -tolerance) continue;

                    penetrating++;
                    maxDepth = Mathf.Max(maxDepth, -signed);
                }
            }

            return new ClippingReport(sampled, penetrating, maxDepth);
        }

        private Mesh BakeOf(SkinnedMeshRenderer renderer)
        {
            if (bakeCache.TryGetValue(renderer, out var mesh) && mesh != null) return mesh;
            mesh = new Mesh { name = $"ClippingProbe_{renderer.name}" };
            bakeCache[renderer] = mesh;
            return mesh;
        }
    }
}
