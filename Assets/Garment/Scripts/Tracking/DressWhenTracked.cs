using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Puts the outfit on only while the tracker is holding a whole body it can trust, and takes
    /// it straight back off when it is not.
    ///
    /// Clothes on a body the tracker has lost look broken in a way an empty picture never does:
    /// a garment left behind drifts, folds through the person and reads as a bug. Showing
    /// nothing reads as "step back into frame", which is exactly what it means.
    /// </summary>
    public sealed class DressWhenTracked : MonoBehaviour
    {
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
            wardrobe.Dressed = provider.IsBodyReady;
        }
    }
}
