using Garment.Body;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Real draping for a light garment: Unity Cloth simulates everything below the waistband,
    /// colliding with capsules on the legs, while the band itself stays pinned to the skinned
    /// pose so tracking still carries the garment.
    ///
    /// Tracking teleports — a photo switch, a lost body reacquired across the frame — are the
    /// classic way to detonate a cloth sim, so any large jump of the rig clears the cloth's
    /// inherited motion instead of feeding it in as velocity.
    /// </summary>
    public sealed class ClothDrape : MonoBehaviour
    {
        /// <summary>
        /// One switch for all cloth simulation, off by default: the panel toggles it so the
        /// drape can be judged against plain skinning live. Off returns the exact skinned mesh.
        /// </summary>
        public static bool Active;

        [Tooltip("Below this bind height the fabric is free to drape; above it stays pinned.")]
        [SerializeField] private float pinAbove = 0.95f;
        [Tooltip("How far, in metres, the hem may stray from its skinned position.")]
        [SerializeField, Range(0.05f, 0.6f)] private float hemFreedom = 0.14f;
        [Tooltip("Leg capsule radius at the thigh.")]
        [SerializeField, Range(0.03f, 0.15f)] private float thighRadius = 0.08f;
        [Tooltip("Leg capsule radius at the shin.")]
        [SerializeField, Range(0.03f, 0.15f)] private float shinRadius = 0.06f;
        [Tooltip("A root jump larger than this clears the cloth's motion instead of exploding it.")]
        [SerializeField] private float teleportDistance = 0.4f;

        private BodyRig rig;
        private Cloth cloth;
        private Vector3 lastRootPosition;

        // Thighs only: a skirt hem barely reaches the shins, and the extended shin's capsule
        // tented the hem into a spike in wide stances.
        private static readonly (BodyLandmark from, BodyLandmark to, bool thigh)[] Legs =
        {
            (BodyLandmark.LeftUpperLeg, BodyLandmark.LeftKnee, true),
            (BodyLandmark.RightUpperLeg, BodyLandmark.RightKnee, true),
        };

        public void Drape(BodyRig body, SkinnedMeshRenderer renderer)
        {
            rig = body;
            cloth = renderer.gameObject.GetComponent<Cloth>();
            if (cloth == null) cloth = renderer.gameObject.AddComponent<Cloth>();

            // Coefficients index the cloth's own welded vertex list, not the mesh's.
            var vertices = cloth.vertices;
            var coefficients = new ClothSkinningCoefficient[vertices.Length];
            float hemY = float.MaxValue;
            foreach (var vertex in vertices) hemY = Mathf.Min(hemY, vertex.y);

            // Thick fabric is two shells, and both simulating fight each other into crumple.
            // The inner one is pinned to the skinned pose: per height band, the half closer
            // to the centreline. Crude on pleats — the known cost is an occasional stuck fold
            // at the hem edge where a pleat's zigzag defeats the radius test.
            var bandMedians = new System.Collections.Generic.Dictionary<int, float>();
            var bandRadii = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<float>>();
            var radii = new float[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                radii[i] = Mathf.Sqrt(vertices[i].x * vertices[i].x + vertices[i].z * vertices[i].z);
                int band = Mathf.RoundToInt(vertices[i].y * 50f);
                if (!bandRadii.TryGetValue(band, out var listForBand))
                    bandRadii[band] = listForBand = new System.Collections.Generic.List<float>();
                listForBand.Add(radii[i]);
            }
            foreach (var pair in bandRadii)
            {
                pair.Value.Sort();
                bandMedians[pair.Key] = pair.Value[pair.Value.Count / 2];
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                int band = Mathf.RoundToInt(vertices[i].y * 50f);
                bool innerShell = radii[i] < bandMedians[band];
                float reach = Mathf.InverseLerp(pinAbove, hemY, vertices[i].y);
                coefficients[i].maxDistance = innerShell ? 0f : hemFreedom * Mathf.SmoothStep(0f, 1f, reach);
                coefficients[i].collisionSphereDistance = float.MaxValue;
            }
            cloth.coefficients = coefficients;

            var capsules = new CapsuleCollider[Legs.Length];
            int count = 0;
            foreach (var (from, to, thigh) in Legs)
            {
                var top = rig.GetBone(from);
                var bottom = rig.GetBone(to);
                if (top == null || bottom == null) continue;

                var holder = new GameObject($"ClothCapsule_{from}");
                holder.transform.SetParent(top, false);
                var capsule = holder.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                float length = Vector3.Distance(top.position, bottom.position);
                capsule.radius = thigh ? thighRadius : shinRadius;
                capsule.height = length + capsule.radius * 2f;
                capsule.direction = 1;
                holder.transform.localPosition = top.InverseTransformPoint(
                    (top.position + bottom.position) * 0.5f);
                holder.transform.rotation = Quaternion.FromToRotation(
                    Vector3.up, (bottom.position - top.position).normalized);
                capsules[count++] = capsule;
            }
            System.Array.Resize(ref capsules, count);
            cloth.capsuleColliders = capsules;

            cloth.useGravity = true;
            cloth.stretchingStiffness = 1f;
            cloth.bendingStiffness = 0.9f;
            cloth.damping = 0.6f;
            cloth.friction = 0.4f;
            // Tracking noise is not wind; keep world motion from pumping energy into the sim.
            cloth.worldVelocityScale = 0.1f;
            cloth.worldAccelerationScale = 0.1f;

            lastRootPosition = rig.transform.position;
        }

        private void LateUpdate()
        {
            if (rig == null || cloth == null) return;

            if (cloth.enabled != Active)
            {
                cloth.enabled = Active;
                // A sim waking up must start from the current skinned pose, not from
                // wherever the garment was when it went to sleep.
                if (Active) cloth.ClearTransformMotion();
            }
            if (!Active) return;

            var rootPosition = rig.transform.position;
            if ((rootPosition - lastRootPosition).magnitude > teleportDistance)
                cloth.ClearTransformMotion();
            lastRootPosition = rootPosition;
        }

        private void OnDestroy()
        {
            if (cloth == null) return;
            foreach (var capsule in cloth.capsuleColliders)
                if (capsule != null) Destroy(capsule.gameObject);
        }
    }
}
