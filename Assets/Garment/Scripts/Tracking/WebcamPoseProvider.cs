using Unity.InferenceEngine;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>Runs pose tracking on the camera feed and publishes the latest frame.</summary>
    public sealed class WebcamPoseProvider : MonoBehaviour
    {
        [SerializeField] private FrameSource feed;
        [SerializeField] private ModelAsset landmarker;
        [SerializeField] private Shader cropShader;
        [Tooltip("Mirror the feed so moving left moves the image left, as in a mirror.")]
        [SerializeField] private bool mirrored = true;
        [Tooltip("Run inference at most this often. Tracking rarely needs the full frame rate.")]
        [SerializeField, Range(5f, 60f)] private float inferencesPerSecond = 20f;
        [Tooltip("Keep the last good pose through brief missed detections so the avatar does not flicker.")]
        [SerializeField, Range(0f, 1f)] private float poseHoldSeconds = 0.3f;
        [SerializeField] private BackendType backend = BackendType.GPUCompute;
        [SerializeField, Range(0f, 1f)] private float visibilityThreshold = 0.5f;
        [Tooltip("The whole body must be steadily in shot this long before we dress it.")]
        [SerializeField, Range(0f, 3f)] private float readyAfterSeconds = 0.6f;
        [Tooltip("Once tracking has been this poor for this long, the clothes come off again.")]
        [SerializeField, Range(0f, 2f)] private float undressAfterSeconds = 0.25f;

        private PoseTracker tracker;
        private BodyReadinessTracker readiness;
        private float nextInferenceTime;
        private float lastPoseTime = float.NegativeInfinity;

        public PoseFrame LatestFrame { get; private set; }

        public bool HasPose { get; private set; }

        /// <summary>
        /// Debounced: the tracker is holding a whole body squarely in frame and can be dressed.
        /// Everything that depends on tracking being trustworthy keys off this one flag.
        /// </summary>
        public bool IsBodyReady => readiness != null && readiness.IsReady;

        public FrameSource Feed => feed;

        /// <summary>Swapping between a live camera and a still photo restarts tracking.</summary>
        public void UseSource(FrameSource source)
        {
            if (source == null || source == feed) return;
            feed = source;
            ResetTracking();
        }

        /// <summary>The current source changed its content (e.g. another photo) — start over.</summary>
        public void ResetTracking()
        {
            HasPose = false;
            lastPoseTime = float.NegativeInfinity;
            tracker?.Reset();
            readiness?.Reset();
        }

        /// <summary>
        /// Whether the picture the user sees is flipped. Landmarks are always reported in the
        /// source frame's own space, so anything drawing over the mirrored image must flip them.
        /// </summary>
        public bool Mirrored => mirrored;

        public RenderTexture LastCrop => tracker?.LastCrop;

        public bool TryMeasureSilhouetteWidth(Vector2 sourceUv, out float widthSourceUv)
        {
            widthSourceUv = 0f;
            return tracker != null && tracker.TryMeasureSilhouetteWidth(sourceUv, out widthSourceUv);
        }

        public bool TryMeasureSilhouetteHeight(Vector2 sourceUv, out float heightSourceUv)
        {
            heightSourceUv = 0f;
            return tracker != null && tracker.TryMeasureSilhouetteHeight(sourceUv, out heightSourceUv);
        }

        public PoseRoi CurrentRoi => tracker?.CurrentRoi ?? default;

        /// <summary>Frames per second the model is actually managing.</summary>
        public float InferenceRate { get; private set; }

        private void Awake()
        {
            if (feed == null) feed = FindFirstObjectByType<FrameSource>();
            if (cropShader == null) cropShader = Shader.Find("Garment/RoiCrop");

            if (landmarker == null)
            {
                Debug.LogError($"{name}: no landmark model assigned.", this);
                return;
            }
            if (cropShader == null)
            {
                Debug.LogError($"{name}: Garment/RoiCrop shader not found.", this);
                return;
            }

            var runtimeBackend = backend;
#if UNITY_WEBGL && !UNITY_EDITOR
            runtimeBackend = BackendType.GPUPixel;
#endif
            tracker = new PoseTracker(landmarker, cropShader, runtimeBackend) { Mirrored = mirrored };
            readiness = new BodyReadinessTracker(readyAfterSeconds, undressAfterSeconds);
        }

        private void OnDestroy()
        {
            tracker?.Dispose();
            tracker = null;
        }

        private void Update()
        {
            if (tracker == null || feed == null || !feed.IsReady) return;

            if (Time.unscaledTime >= nextInferenceTime) RunInference();

            readiness.Update(
                HasPose && LatestFrame.IsValid && LatestFrame.HasVisibleWholeBody(visibilityThreshold),
                Time.unscaledTime);
        }

        private void RunInference()
        {
            float started = Time.realtimeSinceStartup;
            nextInferenceTime = Time.unscaledTime + 1f / inferencesPerSecond;

            tracker.Mirrored = mirrored;
            tracker.LowerBodyTrusted = IsBodyReady;
            if (tracker.TryTrack(feed.Texture, out var frame))
            {
                LatestFrame = frame;
                HasPose = true;
                lastPoseTime = Time.unscaledTime;
            }
            else if (Time.unscaledTime - lastPoseTime > poseHoldSeconds)
            {
                HasPose = false;
            }

            float elapsed = Mathf.Max(Time.realtimeSinceStartup - started, 1e-4f);
            InferenceRate = Mathf.Lerp(InferenceRate, 1f / elapsed, 0.1f);
        }
    }
}
