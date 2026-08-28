using System.Collections.Generic;
using UnityEngine;

namespace ClothLab
{
    /// <summary>
    /// Hangs a garment from its own shoulders, with no body involved: fabric above the pin line
    /// is fixed to the transform, below it the freedom ramps to full at the hem. Drag the
    /// transform and the pinned part follows the hand while the rest is left to the solver.
    /// </summary>
    [RequireComponent(typeof(Cloth))]
    public sealed class ClothPin : MonoBehaviour
    {
        [Tooltip("Fraction of the garment's height, measured down from the top, held rigid.")]
        [SerializeField, Range(0f, 0.6f)] private float pinnedTop = 0.15f;
        [Tooltip("How far, in metres, the hem may stray from where the transform puts it.")]
        [SerializeField, Range(0.05f, 1.5f)] private float hemFreedom = 0.4f;

        [Header("Fabric")]
        [SerializeField, Range(0f, 1f)] private float damping = 0.2f;
        [SerializeField, Range(0f, 1f)] private float bendingStiffness = 0.2f;
        [SerializeField, Range(0f, 1f)] private float stretchingStiffness = 0.8f;
        [SerializeField] private float solverFrequency = 120f;

        [Header("Self collision")]
        [Tooltip("Gap the fabric keeps from itself, in metres. Must stay well under the mesh's " +
                 "shortest edge (10.6mm here) or neighbouring vertices push each other and it boils.")]
        [SerializeField, Range(0f, 0.008f)] private float selfCollisionDistance = 0.004f;
        [SerializeField, Range(0f, 1f)] private float selfCollisionStiffness = 0.5f;

        [Header("Stand-in body")]
        [Tooltip("Capsules the fabric is pushed out of. A hanging tube with nothing inside collapses " +
                 "onto itself; one capsule down the middle is enough to keep it open.")]
        [SerializeField] private CapsuleCollider[] standIn = new CapsuleCollider[0];

        [Tooltip("Motion below this settles to a stop. Zero never rests, which reads as a tremble.")]
        [SerializeField, Range(0f, 0.5f)] private float sleepThreshold = 0.05f;

        [Header("How hard your hand hits the fabric")]
        [Tooltip("Share of the transform's speed fed into the cloth. Lower it and dragging stops whipping the hem.")]
        [SerializeField, Range(0f, 1f)] private float worldVelocityScale = 0.3f;
        [Tooltip("Share of the transform's acceleration fed into the cloth.")]
        [SerializeField, Range(0f, 1f)] private float worldAccelerationScale = 0.3f;

        private Cloth cloth;

        private void Awake()
        {
            Apply();
        }

        private void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }

        /// <summary>Re-reads the values above, so the drape can be dialled in while playing.</summary>
        public void Apply()
        {
            if (cloth == null) cloth = GetComponent<Cloth>();

            Vector3[] vertices = cloth.vertices;
            float top = float.MinValue;
            float hem = float.MaxValue;
            foreach (Vector3 vertex in vertices)
            {
                top = Mathf.Max(top, vertex.y);
                hem = Mathf.Min(hem, vertex.y);
            }

            float pinLine = Mathf.Lerp(top, hem, pinnedTop);
            var coefficients = new ClothSkinningCoefficient[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                float reach = Mathf.InverseLerp(pinLine, hem, vertices[i].y);
                coefficients[i].maxDistance = hemFreedom * Mathf.SmoothStep(0f, 1f, reach);
            }
            cloth.coefficients = coefficients;

            cloth.useGravity = true;
            cloth.damping = damping;
            cloth.bendingStiffness = bendingStiffness;
            cloth.stretchingStiffness = stretchingStiffness;
            cloth.useTethers = true;
            cloth.clothSolverFrequency = solverFrequency;
            cloth.worldVelocityScale = worldVelocityScale;
            cloth.worldAccelerationScale = worldAccelerationScale;
            cloth.sleepThreshold = sleepThreshold;

            cloth.selfCollisionDistance = selfCollisionDistance;
            cloth.selfCollisionStiffness = selfCollisionStiffness;
            if (selfCollisionDistance > 0f)
            {
                var all = new List<uint>(vertices.Length);
                for (uint i = 0; i < vertices.Length; i++) all.Add(i);
                cloth.SetSelfAndInterCollisionIndices(all);
            }

            cloth.capsuleColliders = standIn;
        }
    }
}
