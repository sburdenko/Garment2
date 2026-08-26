using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// A wearable item as data. Adding a garment to the app means authoring one of these,
    /// not writing code.
    /// </summary>
    [CreateAssetMenu(menuName = "Garment/Garment Definition", fileName = "Garment")]
    public sealed class GarmentDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private GameObject sourcePrefab;
        [SerializeField] private GarmentSlot slot = GarmentSlot.Bottom;
        [SerializeField] private SubmeshRole[] submeshRoles = new SubmeshRole[0];
        [Tooltip("Replaces the model's own materials per submesh. Leave an entry empty to keep the imported one.")]
        [SerializeField] private Material[] materialOverrides = new Material[0];

        [Header("Fit")]
        [Tooltip("Native: the mesh was authored on this body, place it unchanged. AutoFit: measure it onto the body.")]
        [SerializeField] private GarmentFitMode fitMode = GarmentFitMode.AutoFit;
        [Tooltip("Widen the garment when the body is broader than the mesh was authored for.")]
        [SerializeField] private bool compensateWidth = true;
        [Tooltip("Extra clearance from the skin, in metres. Prevents z-fighting and shallow clipping.")]
        [SerializeField, Range(0f, 0.03f)] private float skinOffset = 0.004f;
        [Tooltip("Manual correction applied after the automatic fit.")]
        [SerializeField] private Vector3 positionOffset = Vector3.zero;
        [SerializeField, Range(0.5f, 1.5f)] private float scaleMultiplier = 1f;

        [Header("Penetration")]
        [Tooltip("Push vertices that start inside the body back out before skinning.")]
        [SerializeField] private bool resolvePenetration = true;
        [SerializeField, Range(1, 4)] private int penetrationPasses = 2;
        [Tooltip("Spreads each push-out over neighbouring vertices so no bumps appear.")]
        [SerializeField, Range(0, 8)] private int penetrationSmoothing = 3;

        [Header("Cloth")]
        [Tooltip("Simulate this garment with Unity Cloth: the band stays pinned to the skinned " +
                 "pose and the rest drapes and collides with the legs. For light meshes only.")]
        [SerializeField] private bool simulateCloth;

        [Header("Skinning")]
        [Tooltip("Averages transferred bone weights across neighbours. Prevents the garment tearing where body parts meet.")]
        [SerializeField, Range(0, 6)] private int weightSmoothing = 2;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public GameObject SourcePrefab => sourcePrefab;
        public GarmentSlot Slot => slot;
        public GarmentFitMode FitMode => fitMode;
        public bool CompensateWidth => compensateWidth;
        public float SkinOffset => skinOffset;
        public Vector3 PositionOffset => positionOffset;
        public bool ResolvePenetration => resolvePenetration;
        public int PenetrationPasses => penetrationPasses;
        public int PenetrationSmoothing => penetrationSmoothing;
        public int WeightSmoothing => weightSmoothing;
        public bool SimulateCloth => simulateCloth;
        public float ScaleMultiplier => scaleMultiplier;

        public Material MaterialFor(int submeshIndex, Material fallback)
        {
            if (materialOverrides == null || submeshIndex < 0 || submeshIndex >= materialOverrides.Length)
                return fallback;
            return materialOverrides[submeshIndex] != null ? materialOverrides[submeshIndex] : fallback;
        }

        public SubmeshRole RoleOf(int submeshIndex)
        {
            if (submeshRoles == null || submeshIndex < 0 || submeshIndex >= submeshRoles.Length)
                return SubmeshRole.Fabric;
            return submeshRoles[submeshIndex];
        }
    }
}
