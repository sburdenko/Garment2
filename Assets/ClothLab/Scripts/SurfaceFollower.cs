using System.Collections.Generic;
using UnityEngine;

namespace ClothLab
{
    /// <summary>
    /// Rides a mesh on the surface of a simulated cloth. Embroidery, appliques and buttons are
    /// decoration on the fabric, not fabric: handing them to the solver makes them islands of
    /// their own particles, which drift off. Instead each of their vertices is bound once to a
    /// triangle of the host — barycentric position, height above it, and the triangle's own
    /// frame — and rebuilt from the simulated triangle every frame.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class SurfaceFollower : MonoBehaviour
    {
        private struct Binding
        {
            public int Triangle;
            public float U;
            public float V;
            public float W;
            public float Height;
            public Quaternion RestFrame;
        }

        [Tooltip("The cloth this decoration is glued to. Its own transform is the shared frame, " +
                 "so this object must sit on it with no offset of its own.")]
        [SerializeField] private SkinnedMeshRenderer host;

        private Cloth cloth;
        private Binding[] bindings;
        private int[] hostTriangles;
        private Vector3[] restVertices;
        private Vector3[] restNormals;
        private Mesh live;
        private Vector3[] positions;
        private Vector3[] normals;

        private void Awake()
        {
            if (host == null) host = GetComponentInParent<SkinnedMeshRenderer>();
            if (host == null) { enabled = false; return; }

            cloth = host.GetComponent<Cloth>();
            if (cloth == null) { enabled = false; return; }

            var filter = GetComponent<MeshFilter>();
            live = Instantiate(filter.sharedMesh);
            live.name = filter.sharedMesh.name + " (following)";
            filter.sharedMesh = live;

            restVertices = live.vertices;
            restNormals = live.normals;
            positions = new Vector3[restVertices.Length];
            normals = new Vector3[restVertices.Length];

            Bind();
        }

        private void LateUpdate()
        {
            if (bindings == null) return;

            Vector3[] particles = cloth.vertices;
            for (int i = 0; i < bindings.Length; i++)
            {
                Binding binding = bindings[i];
                int t = binding.Triangle * 3;
                Vector3 a = particles[hostTriangles[t]];
                Vector3 b = particles[hostTriangles[t + 1]];
                Vector3 c = particles[hostTriangles[t + 2]];

                Quaternion frame = FrameOf(a, b, c);
                Quaternion turn = frame * Quaternion.Inverse(binding.RestFrame);

                positions[i] = binding.U * a + binding.V * b + binding.W * c
                             + (frame * Vector3.forward) * binding.Height;
                normals[i] = turn * restNormals[i];
            }

            live.vertices = positions;
            live.normals = normals;
            live.RecalculateBounds();
        }

        /// <summary>
        /// The cloth reports a welded particle list, not the host mesh's own vertices, so the
        /// host's triangles are re-indexed onto the particles they collapsed into.
        /// </summary>
        private void Bind()
        {
            Vector3[] particles = cloth.vertices;
            var particleAt = new Dictionary<Vector3, int>(particles.Length);
            for (int i = 0; i < particles.Length; i++) particleAt[particles[i]] = i;

            Mesh hostMesh = host.sharedMesh;
            Vector3[] hostVertices = hostMesh.vertices;
            int[] meshTriangles = hostMesh.triangles;
            var toParticle = new int[hostVertices.Length];
            for (int i = 0; i < hostVertices.Length; i++)
                toParticle[i] = particleAt.TryGetValue(hostVertices[i], out int p) ? p : -1;

            hostTriangles = new int[meshTriangles.Length];
            int kept = 0;
            for (int t = 0; t < meshTriangles.Length; t += 3)
            {
                int a = toParticle[meshTriangles[t]];
                int b = toParticle[meshTriangles[t + 1]];
                int c = toParticle[meshTriangles[t + 2]];
                if (a < 0 || b < 0 || c < 0 || a == b || b == c || a == c) continue;
                hostTriangles[kept++] = a;
                hostTriangles[kept++] = b;
                hostTriangles[kept++] = c;
            }
            System.Array.Resize(ref hostTriangles, kept);

            var grid = SurfaceGrid.Build(particles, hostTriangles);
            bindings = new Binding[restVertices.Length];
            for (int i = 0; i < restVertices.Length; i++)
                bindings[i] = BindOne(restVertices[i], particles, grid);
        }

        private Binding BindOne(Vector3 point, Vector3[] particles, SurfaceGrid grid)
        {
            int best = grid.NearestTriangle(point, particles, hostTriangles, out Vector3 onSurface);
            var binding = new Binding { Triangle = best };
            if (best < 0) return binding;

            int t = best * 3;
            Vector3 a = particles[hostTriangles[t]];
            Vector3 b = particles[hostTriangles[t + 1]];
            Vector3 c = particles[hostTriangles[t + 2]];

            Barycentric(onSurface, a, b, c, out binding.U, out binding.V, out binding.W);
            binding.RestFrame = FrameOf(a, b, c);
            binding.Height = Vector3.Dot(point - onSurface, binding.RestFrame * Vector3.forward);
            return binding;
        }

        private static Quaternion FrameOf(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-16f) return Quaternion.identity;
            return Quaternion.LookRotation(normal.normalized, (b - a).normalized);
        }

        private static void Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c,
            out float u, out float v, out float w)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 1e-20f) { u = 1f; v = 0f; w = 0f; return; }
            v = (d11 * d20 - d01 * d21) / denominator;
            w = (d00 * d21 - d01 * d20) / denominator;
            u = 1f - v - w;
        }
    }
}
