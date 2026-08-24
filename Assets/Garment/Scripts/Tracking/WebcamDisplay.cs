using UnityEngine;
using UnityEngine.UI;

namespace Garment.Tracking
{
    /// <summary>Shows the camera feed behind the scene, letterboxed and mirrored to taste.</summary>
    [RequireComponent(typeof(RawImage), typeof(AspectRatioFitter))]
    public sealed class WebcamDisplay : MonoBehaviour
    {
        [SerializeField] private FrameSource feed;
        [Tooltip("Takes the mirror setting from the tracker so image and landmarks never disagree.")]
        [SerializeField] private WebcamPoseProvider provider;
        [SerializeField] private bool mirrored = true;

        private RawImage image;
        private AspectRatioFitter fitter;

        private bool IsMirrored => provider != null ? provider.Mirrored : mirrored;

        private void Awake()
        {
            image = GetComponent<RawImage>();
            fitter = GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            // FitInParent drives the size itself; stretched anchors fight it and the image
            // ends up filling the frame regardless of its real proportions.
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            if (feed == null) feed = FindFirstObjectByType<FrameSource>();
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
        }

        private void Update()
        {
            var source = provider != null && provider.Feed != null ? provider.Feed : feed;
            if (source == null || !source.IsReady) return;

            if (image.texture != source.Texture) image.texture = source.Texture;
            fitter.aspectRatio = source.AspectRatio;

            // The driver may hand the frame back rotated or flipped; undo both here.
            float verticalFlip = source.IsVerticallyMirrored ? -1f : 1f;
            image.rectTransform.localScale = new Vector3(IsMirrored ? -1f : 1f, verticalFlip, 1f);
            image.rectTransform.localEulerAngles = new Vector3(0f, 0f, -source.RotationDegrees);
        }
    }
}
