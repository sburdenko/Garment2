using Garment.Fitting;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Hides lower-body garments while the camera only sees the person from the waist up.
    /// Trousers dangling below a half-visible body read as a glitch, not as clothing.
    /// </summary>
    public sealed class CoverageGarmentGate : MonoBehaviour
    {
        private static readonly GarmentSlot[] LowerBodySlots = { GarmentSlot.Bottom, GarmentSlot.Footwear };

        [SerializeField] private WebcamPoseProvider provider;
        [SerializeField] private Wardrobe.Wardrobe wardrobe;

        private void Awake()
        {
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            if (wardrobe == null) wardrobe = FindFirstObjectByType<Wardrobe.Wardrobe>();
        }

        private void Update()
        {
            if (provider == null || wardrobe == null) return;

            bool visible = provider.Coverage == BodyCoverage.FullBody;
            foreach (var slot in LowerBodySlots)
                wardrobe.SetSlotVisible(slot, visible);
        }
    }
}
