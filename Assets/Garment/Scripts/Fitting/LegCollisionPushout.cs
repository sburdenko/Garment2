using Garment.Body;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Keeps a lower-body garment outside the legs, frame by frame.
    ///
    /// Bind-time fitting cannot know the pose: fold a knee and linear blend skinning cuts the
    /// corner, so the kneecap surfaces through the fabric. This is collision without physics —
    /// each frame the garment's vertices are skinned on the CPU, anything inside a leg capsule
    /// is pushed radially to its surface, and the result is written back through the inverse
    /// skin. Deterministic: no springs, no state, nothing for jittery tracking to detonate.
    ///
    /// The loop is a Burst job: 112k vertices took 130 ms a frame in plain editor Mono, and
    /// 5 fps is not a fitting room.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class LegCollisionPushout : MonoBehaviour
    {
        /// <summary>One switch for every garment's pushout, so the effect can be compared live.</summary>
        public static bool Active = true;

        /// <summary>
        /// How hard the garment's upper band is pulled toward the body. Set while a top is
        /// worn over it: whatever sits under an opaque jacket cannot be seen, and pulled in
        /// it cannot poke through the jacket either.
        /// </summary>
        public float TuckStrength { get; set; }

        private bool wasActive;

        // The capsules are the LEG, not the leg plus clothing room. Sized to the fabric
        // (0.11) they swallowed the baggy tube's own inseam wall and inflated the printed
        // lining out through the outer shell — the trousers marbled all over.
        [Tooltip("Leg radius at the thigh, metres — the leg itself, not the clothed leg.")]
        [SerializeField, Range(0.02f, 0.2f)] private float thighRadius = 0.075f;
        [Tooltip("Leg radius at the shin, metres — the leg itself, not the clothed leg.")]
        [SerializeField, Range(0.02f, 0.2f)] private float shinRadius = 0.055f;

        private BodyRig rig;
        private SkinnedMeshRenderer skin;
        private Mesh mesh;
        private Transform[] bones;
        private Matrix4x4[] bindposes;

        private NativeArray<float3> bindVertices;
        private NativeArray<float3> outVertices;
        private NativeArray<int4> boneIndices;
        private NativeArray<float4> boneWeights;
        private NativeArray<byte> sideMask;
        private NativeArray<float3x4> skinMatrices;
        private NativeArray<float3> capsuleA;
        private NativeArray<float3> capsuleB;
        private NativeArray<float> capsuleR;
        private NativeArray<byte> capsuleSide;
        private bool ready;

        private const byte LeftSide = 1;
        private const byte RightSide = 2;

        private static readonly (BodyLandmark from, BodyLandmark to, bool thigh, byte side)[] CapsuleBones =
        {
            (BodyLandmark.LeftUpperLeg, BodyLandmark.LeftKnee, true, LeftSide),
            (BodyLandmark.LeftKnee, BodyLandmark.LeftAnkle, false, LeftSide),
            (BodyLandmark.RightUpperLeg, BodyLandmark.RightKnee, true, RightSide),
            (BodyLandmark.RightKnee, BodyLandmark.RightAnkle, false, RightSide),
        };

        public void Bind(BodyRig body, SkinnedMeshRenderer renderer)
        {
            Release();
            rig = body;
            skin = renderer;
            mesh = renderer.sharedMesh;
            bones = renderer.bones;
            bindposes = mesh.bindposes;

            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            bindVertices = new NativeArray<float3>(vertices.Length, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            outVertices = new NativeArray<float3>(vertices.Length, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            boneIndices = new NativeArray<int4>(vertices.Length, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            boneWeights = new NativeArray<float4>(vertices.Length, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            // Which leg's capsules may touch a vertex. Fabric near the axis of the OTHER
            // leg's capsule would be shredded radially in opposite directions — the bent
            // shin passing behind the standing trouser speckled it to pieces. A vertex is
            // only kept out of the leg it is actually skinned to; fabric weighted to
            // neither leg (waistband, crotch web) answers to both.
            var boneSide = new byte[bones.Length];
            for (int b = 0; b < bones.Length; b++)
            {
                string boneName = bones[b] != null ? bones[b].name : string.Empty;
                bool leg = boneName.Contains("Leg");
                if (leg && boneName.StartsWith("Left")) boneSide[b] = LeftSide;
                else if (leg && boneName.StartsWith("Right")) boneSide[b] = RightSide;
            }

            sideMask = new NativeArray<byte>(vertices.Length, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < vertices.Length; i++)
            {
                bindVertices[i] = vertices[i];
                boneIndices[i] = new int4(weights[i].boneIndex0, weights[i].boneIndex1,
                                          weights[i].boneIndex2, weights[i].boneIndex3);
                boneWeights[i] = new float4(weights[i].weight0, weights[i].weight1,
                                            weights[i].weight2, weights[i].weight3);
                byte mask = 0;
                if (weights[i].weight0 > 0.01f) mask |= boneSide[weights[i].boneIndex0];
                if (weights[i].weight1 > 0.01f) mask |= boneSide[weights[i].boneIndex1];
                if (weights[i].weight2 > 0.01f) mask |= boneSide[weights[i].boneIndex2];
                if (weights[i].weight3 > 0.01f) mask |= boneSide[weights[i].boneIndex3];
                sideMask[i] = mask == 0 ? (byte)(LeftSide | RightSide) : mask;
            }
            skinMatrices = new NativeArray<float3x4>(bones.Length, Allocator.Persistent);
            capsuleA = new NativeArray<float3>(CapsuleBones.Length, Allocator.Persistent);
            capsuleB = new NativeArray<float3>(CapsuleBones.Length, Allocator.Persistent);
            capsuleR = new NativeArray<float>(CapsuleBones.Length, Allocator.Persistent);
            capsuleSide = new NativeArray<byte>(CapsuleBones.Length, Allocator.Persistent);
            ready = true;
        }

        private void OnDestroy() => Release();

        private void Release()
        {
            if (!ready) return;
            ready = false;
            bindVertices.Dispose();
            outVertices.Dispose();
            sideMask.Dispose();
            boneIndices.Dispose();
            boneWeights.Dispose();
            skinMatrices.Dispose();
            capsuleA.Dispose();
            capsuleB.Dispose();
            capsuleR.Dispose();
            capsuleSide.Dispose();
        }

        private void LateUpdate()
        {
            if (!ready || rig == null || skin == null || mesh == null) return;

            if (!Active)
            {
                // Hand the mesh back exactly as it was bound, once.
                if (wasActive) { mesh.SetVertices(bindVertices); wasActive = false; }
                return;
            }
            wasActive = true;

            for (int b = 0; b < bones.Length; b++)
            {
                var m = bones[b] != null ? bones[b].localToWorldMatrix * bindposes[b] : Matrix4x4.identity;
                skinMatrices[b] = new float3x4(
                    new float3(m.m00, m.m10, m.m20),
                    new float3(m.m01, m.m11, m.m21),
                    new float3(m.m02, m.m12, m.m22),
                    new float3(m.m03, m.m13, m.m23));
            }

            int capsuleCount = 0;
            float scale = Mathf.Max(rig.transform.lossyScale.y, 1e-3f);
            var boundsMin = new float3(float.MaxValue);
            var boundsMax = new float3(float.MinValue);
            foreach (var (from, to, thigh, side) in CapsuleBones)
            {
                var a = rig.GetBone(from);
                var b = rig.GetBone(to);
                if (a == null || b == null) continue;
                float radius = (thigh ? thighRadius : shinRadius) * scale;
                capsuleA[capsuleCount] = a.position;
                capsuleB[capsuleCount] = b.position;
                capsuleR[capsuleCount] = radius;
                capsuleSide[capsuleCount] = side;
                boundsMin = math.min(boundsMin, math.min((float3)a.position, (float3)b.position) - radius);
                boundsMax = math.max(boundsMax, math.max((float3)a.position, (float3)b.position) + radius);
                capsuleCount++;
            }
            if (capsuleCount == 0) return;

            var job = new PushoutJob
            {
                BindVertices = bindVertices,
                BoneIndices = boneIndices,
                BoneWeights = boneWeights,
                SkinMatrices = skinMatrices,
                CapsuleA = capsuleA,
                CapsuleB = capsuleB,
                CapsuleR = capsuleR,
                CapsuleSide = capsuleSide,
                SideMask = sideMask,
                CapsuleCount = capsuleCount,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                Tuck = TuckStrength,
                Out = outVertices,
            };
            job.Schedule(bindVertices.Length, 512).Complete();
            mesh.SetVertices(outVertices);
        }

        [BurstCompile]
        private struct PushoutJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> BindVertices;
            [ReadOnly] public NativeArray<int4> BoneIndices;
            [ReadOnly] public NativeArray<float4> BoneWeights;
            [ReadOnly] public NativeArray<float3x4> SkinMatrices;
            [ReadOnly] public NativeArray<float3> CapsuleA;
            [ReadOnly] public NativeArray<float3> CapsuleB;
            [ReadOnly] public NativeArray<float> CapsuleR;
            [ReadOnly] public NativeArray<byte> CapsuleSide;
            [ReadOnly] public NativeArray<byte> SideMask;
            public int CapsuleCount;
            public float3 BoundsMin;
            public float3 BoundsMax;
            public float Tuck;
            [WriteOnly] public NativeArray<float3> Out;

            public void Execute(int v)
            {
                var p = BindVertices[v];
                if (Tuck > 0f)
                {
                    // Bind space is rig-local, Y up; the waist band starts around 0.78.
                    float band = math.smoothstep(0.78f, 1.0f, p.y) * Tuck;
                    p.x *= 1f - band;
                    p.z *= 1f - band;
                }
                var idx = BoneIndices[v];
                var w = BoneWeights[v];

                var m = SkinMatrices[idx.x] * w.x;
                if (w.y > 0f) m += SkinMatrices[idx.y] * w.y;
                if (w.z > 0f) m += SkinMatrices[idx.z] * w.z;
                if (w.w > 0f) m += SkinMatrices[idx.w] * w.w;

                var world = m.c0 * p.x + m.c1 * p.y + m.c2 * p.z + m.c3;

                Out[v] = p;
                if (math.any(world < BoundsMin) || math.any(world > BoundsMax)) return;

                var corrected = world;
                byte mask = SideMask[v];
                for (int c = 0; c < CapsuleCount; c++)
                {
                    if ((mask & CapsuleSide[c]) == 0) continue;
                    corrected = Push(corrected, CapsuleA[c], CapsuleB[c], CapsuleR[c]);
                }

                if (math.lengthsq(corrected - world) <= 1e-10f) return;

                var full = new float4x4(
                    new float4(m.c0, 0f), new float4(m.c1, 0f),
                    new float4(m.c2, 0f), new float4(m.c3, 1f));
                Out[v] = math.transform(math.inverse(full), corrected);
            }

            private static float3 Push(float3 point, float3 a, float3 b, float radius)
            {
                var span = b - a;
                float lengthSquared = math.lengthsq(span);
                float t = lengthSquared > 1e-9f
                    ? math.clamp(math.dot(point - a, span) / lengthSquared, 0f, 1f)
                    : 0f;
                var closest = a + span * t;
                var offset = point - closest;
                float distance = math.length(offset);
                if (distance >= radius) return point;
                var direction = distance > 1e-6f ? offset / distance : new float3(1f, 0f, 0f);
                // Thick fabric is two shells. Landing both exactly on the surface makes them
                // coplanar (z-fight); an ordering band WIDER than the gap to an untouched
                // outer shell flips the layers and the printed lining marbles through. The
                // band must stay well under the shell gap: 1.5 mm, which reversed-Z depth
                // resolves without fighting.
                return closest + direction * (radius + distance / radius * 0.0015f);
            }
        }

        /// <summary>A point inside the capsule (a, b, radius) moved radially onto its surface.</summary>
        public static Vector3 PushOutOfCapsule(Vector3 point, Vector3 a, Vector3 b, float radius)
        {
            var span = b - a;
            float lengthSquared = span.sqrMagnitude;
            float t = lengthSquared > 1e-9f
                ? Mathf.Clamp01(Vector3.Dot(point - a, span) / lengthSquared)
                : 0f;
            var closest = a + span * t;
            var offset = point - closest;
            float distance = offset.magnitude;
            if (distance >= radius) return point;
            var direction = distance > 1e-6f ? offset / distance : Vector3.right;
            return closest + direction * (radius + distance / radius * 0.0015f);
        }
    }
}
