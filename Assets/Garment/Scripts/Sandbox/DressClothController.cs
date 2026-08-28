using System.Collections.Generic;
using UnityEngine;

namespace GarmentDemo.Sandbox
{
    /// <summary>
    /// Cloth for the dress, modelled as three regions rather than one: the bodice stays pinned to
    /// the skinned pose, the skirt below the hip swings, and the sleeve ripples along the arm.
    /// Each region carries its own freedom budget, so the hem can be made lively without the
    /// shoulders tearing loose.
    ///
    /// The mesh is a Z-up export: local +Z is the body's vertical axis, local X the T-pose arm
    /// span. Both region tests read those two axes directly.
    ///
    /// Inside a simulated region only the outer wall of the fabric moves. The garment is a
    /// double-walled shell roughly 0.4 mm thick, and Unity welds only part of those wall pairs
    /// into shared particles: freeing both walls lets the unwelded ones drift apart and the
    /// surface shatters into shards. Pinning the inner wall keeps the sheet stitched to the
    /// skinned pose, and it is what caps how far any of this can be pushed — past roughly the
    /// defaults below the tearing shows through no matter how the fabric is tuned.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public sealed class DressClothController : MonoBehaviour
    {
        private enum Region { Pinned, Skirt, Sleeve }

        private const float BandSize = 0.02f;

        [Header("Freedom")]
        [Tooltip("Fraction of the garment height, measured up from the hem, that the skirt simulates over.")]
        [SerializeField, Range(0.1f, 0.85f)] private float skirtHeight = 0.55f;
        [Tooltip("How far, in metres, the hem may stray from its skinned position.")]
        [SerializeField, Range(0.02f, 0.4f)] private float hemSwing = 0.14f;
        [Tooltip("How far, in metres, the sleeve may stray from the arm.")]
        [SerializeField, Range(0.01f, 0.2f)] private float sleeveSwing = 0.05f;
        [Tooltip("Fraction of the sleeve, at the cuff, held to the wrist so the sleeve cannot slide off.")]
        [SerializeField, Range(0f, 0.5f)] private float cuffGrip = 0.15f;

        [Header("Fabric")]
        [SerializeField, Range(0f, 1f)] private float damping = 0.05f;
        [SerializeField, Range(0f, 1f)] private float bendingStiffness = 0.1f;
        [SerializeField, Range(0f, 1f)] private float stretchingStiffness = 0.75f;
        [SerializeField, Range(0f, 1f)] private float friction = 0.4f;

        [Header("Breeze")]
        [Tooltip("Peak acceleration of the demo breeze in m/s²; 0 leaves only gravity and body motion.")]
        [SerializeField] private float breezeStrength = 6f;

        [Header("Body collision")]
        [SerializeField, Range(0f, 0.2f)] private float thighRadius = 0.085f;
        [SerializeField, Range(0f, 0.2f)] private float shinRadius = 0.055f;
        [SerializeField, Range(0f, 0.2f)] private float upperArmRadius = 0.055f;
        [SerializeField, Range(0f, 0.2f)] private float forearmRadius = 0.045f;

        private Cloth cloth;
        private CapsuleCollider[] capsules;
        // The cloth reports live simulated positions once it is running, so the rest pose the
        // regions are measured against is captured while it is still the skinned one.
        private Vector3[] restVertices;

        public bool IsClothEnabled
        {
            get
            {
                EnsureInitialized();
                return cloth.enabled;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            RigCapsules.Destroy(capsules);
        }

        private void EnsureInitialized()
        {
            if (cloth != null)
                return;

            cloth = GetComponent<Cloth>();
            if (cloth == null)
                cloth = gameObject.AddComponent<Cloth>();

            restVertices = cloth.vertices;
            ApplyCoefficients();
            ApplyColliders();
            ApplyFabric();
        }

        /// <summary>Re-reads the tuning values, so the drape can be dialled in while playing.</summary>
        public void Rebuild()
        {
            if (cloth == null) return;
            ApplyCoefficients();
            ApplyFabric();
        }

        private void OnValidate()
        {
            if (Application.isPlaying) Rebuild();
        }

        public void SetClothEnabled(bool enabled)
        {
            EnsureInitialized();
            cloth.enabled = enabled;
            if (enabled)
                cloth.ClearTransformMotion();
        }

        private void LateUpdate()
        {
            if (!cloth.enabled || breezeStrength <= 0f)
                return;

            float sway = Mathf.Sin(Time.time * 2.4f);
            float lift = Mathf.Sin(Time.time * 1.3f + 2.1f);
            float flutter = Mathf.Sin(Time.time * 4.1f + 0.8f);
            cloth.externalAcceleration = new Vector3(sway, lift * 0.35f, flutter * 0.45f) * breezeStrength;
        }

        private void ApplyCoefficients()
        {
            Vector3[] vertices = restVertices;
            float hemLine = float.MaxValue;
            float shoulderLine = float.MinValue;
            float armSpan = 0f;

            foreach (Vector3 vertex in vertices)
            {
                hemLine = Mathf.Min(hemLine, vertex.z);
                shoulderLine = Mathf.Max(shoulderLine, vertex.z);
                armSpan = Mathf.Max(armSpan, Mathf.Abs(vertex.x));
            }

            float skirtTop = Mathf.Lerp(hemLine, shoulderLine, skirtHeight);
            // The body tube never reaches as wide as an arm, so its own half-width — measured
            // where no sleeve can reach — is what separates sleeve from bodice.
            float torsoHalfWidth = 0f;
            foreach (Vector3 vertex in vertices)
                if (vertex.z < skirtTop) torsoHalfWidth = Mathf.Max(torsoHalfWidth, Mathf.Abs(vertex.x));
            float sleeveStart = torsoHalfWidth * 1.2f;

            float[] wallRadius = MeasureWallRadius(vertices, skirtTop, sleeveStart);
            Dictionary<int, float> medians = MedianPerBand(vertices, wallRadius, skirtTop, sleeveStart);

            var coefficients = new ClothSkinningCoefficient[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                Region region = Classify(vertex, skirtTop, sleeveStart);
                bool innerWall = region != Region.Pinned && wallRadius[i] < medians[BandOf(vertex, sleeveStart)];

                coefficients[i].maxDistance = innerWall ? 0f : Freedom(region, vertex, skirtTop, hemLine, sleeveStart, armSpan);
                // Backstop: fabric may drape outward but never sink behind its skinned surface,
                // which otherwise shows up as dark torn patches where it passes through itself.
                coefficients[i].collisionSphereDistance = 0.012f;
            }

            cloth.coefficients = coefficients;
        }

        private float Freedom(Region region, Vector3 vertex, float skirtTop, float hemLine, float sleeveStart, float armSpan)
        {
            switch (region)
            {
                case Region.Skirt:
                    return hemSwing * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(skirtTop, hemLine, vertex.z));
                case Region.Sleeve:
                    float alongSleeve = Mathf.InverseLerp(sleeveStart, armSpan, Mathf.Abs(vertex.x));
                    float grip = 1f - Mathf.SmoothStep(1f - cuffGrip, 1f, alongSleeve);
                    return sleeveSwing * Mathf.SmoothStep(0f, 1f, alongSleeve) * grip;
                default:
                    return 0f;
            }
        }

        private static Region Classify(Vector3 vertex, float skirtTop, float sleeveStart)
        {
            if (Mathf.Abs(vertex.x) >= sleeveStart) return Region.Sleeve;
            return vertex.z < skirtTop ? Region.Skirt : Region.Pinned;
        }

        /// <summary>Bands run across the tube each region wraps: up the body, out along the arm.</summary>
        private static int BandOf(Vector3 vertex, float sleeveStart)
        {
            return Mathf.Abs(vertex.x) >= sleeveStart
                ? Mathf.RoundToInt(Mathf.Abs(vertex.x) / BandSize)
                : -Mathf.RoundToInt(vertex.z / BandSize) - 1;
        }

        /// <summary>
        /// Distance from the tube's own centre line — the body's for the skirt, the arm's for the
        /// sleeve — so that the median across a band splits the fabric's outer wall from its inner.
        /// </summary>
        private static float[] MeasureWallRadius(Vector3[] vertices, float skirtTop, float sleeveStart)
        {
            var sums = new Dictionary<int, Vector3>();
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int band = BandOf(vertices[i], sleeveStart);
                sums.TryGetValue(band, out Vector3 sum);
                counts.TryGetValue(band, out int count);
                sums[band] = sum + vertices[i];
                counts[band] = count + 1;
            }

            var radii = new float[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                int band = BandOf(vertices[i], sleeveStart);
                Vector3 centre = sums[band] / counts[band];
                Vector3 offset = vertices[i] - centre;
                radii[i] = Mathf.Abs(vertices[i].x) >= sleeveStart
                    ? new Vector2(offset.y, offset.z).magnitude
                    : new Vector2(offset.x, offset.y).magnitude;
            }

            return radii;
        }

        private static Dictionary<int, float> MedianPerBand(Vector3[] vertices, float[] radii, float skirtTop, float sleeveStart)
        {
            var perBand = new Dictionary<int, List<float>>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (Classify(vertices[i], skirtTop, sleeveStart) == Region.Pinned) continue;
                int band = BandOf(vertices[i], sleeveStart);
                if (!perBand.TryGetValue(band, out List<float> values))
                    perBand[band] = values = new List<float>();
                values.Add(radii[i]);
            }

            var medians = new Dictionary<int, float>(perBand.Count);
            foreach (KeyValuePair<int, List<float>> band in perBand)
            {
                band.Value.Sort();
                medians[band.Key] = band.Value[band.Value.Count / 2];
            }

            // Bands holding only pinned fabric are never asked for a median by the loop above,
            // but BandOf is evaluated for every vertex, so they still need an entry.
            for (int i = 0; i < vertices.Length; i++)
            {
                int band = BandOf(vertices[i], sleeveStart);
                if (!medians.ContainsKey(band)) medians[band] = float.MaxValue;
            }

            return medians;
        }

        private void ApplyColliders()
        {
            Transform skeletonRoot = GetComponent<SkinnedMeshRenderer>().rootBone;
            if (skeletonRoot == null)
                return;

            RigCapsules.Destroy(capsules);
            var segments = new List<RigCapsules.Segment>
            {
                new RigCapsules.Segment("mixamorig:LeftUpLeg", "mixamorig:LeftLeg", thighRadius),
                new RigCapsules.Segment("mixamorig:RightUpLeg", "mixamorig:RightLeg", thighRadius),
                new RigCapsules.Segment("mixamorig:LeftLeg", "mixamorig:LeftFoot", shinRadius),
                new RigCapsules.Segment("mixamorig:RightLeg", "mixamorig:RightFoot", shinRadius),
                new RigCapsules.Segment("mixamorig:LeftArm", "mixamorig:LeftForeArm", upperArmRadius),
                new RigCapsules.Segment("mixamorig:RightArm", "mixamorig:RightForeArm", upperArmRadius),
                new RigCapsules.Segment("mixamorig:LeftForeArm", "mixamorig:LeftHand", forearmRadius),
                new RigCapsules.Segment("mixamorig:RightForeArm", "mixamorig:RightHand", forearmRadius),
            };

            capsules = RigCapsules.Build(skeletonRoot, segments);
            cloth.capsuleColliders = capsules;
        }

        private void ApplyFabric()
        {
            cloth.useGravity = true;
            cloth.damping = damping;
            cloth.bendingStiffness = bendingStiffness;
            cloth.stretchingStiffness = stretchingStiffness;
            cloth.friction = friction;
            cloth.useTethers = true;
            cloth.clothSolverFrequency = 90f;
            // Body motion is the main driver of the drape; let all of it reach the fabric.
            cloth.worldVelocityScale = 1f;
            cloth.worldAccelerationScale = 1f;
            cloth.randomAcceleration = new Vector3(1f, 0.5f, 1f);
            // A low-amplitude drape otherwise falls asleep mid-motion and freezes on screen.
            cloth.sleepThreshold = 0f;
        }
    }
}
