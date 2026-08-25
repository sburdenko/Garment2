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
        [SerializeField] private BackendType backend = BackendType.GPUCompute;

        private PoseTracker tracker;
        private float nextInferenceTime;

        public PoseFrame LatestFrame { get; private set; }

        public bool HasPose { get; private set; }

        public FrameSource Feed => feed;

        /// <summary>Swapping between a live camera and a still photo restarts tracking.</summary>
        public void UseSource(FrameSource source)
        {
            if (source == null || source == feed) return;
            feed = source;
            HasPose = false;
            tracker?.Reset();
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
        }

        private void Start()
        {
            SelectBestSource();
        }

        /// <summary>
        /// Prefer whichever ready source ranks highest — a photo dropped in for testing should
        /// take over without anyone having to unplug the camera first.
        /// </summary>
        private void SelectBestSource()
        {
            FrameSource best = null;
            foreach (var candidate in FindObjectsByType<FrameSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate == null || !candidate.IsReady) continue;
                if (best == null || candidate.Priority > best.Priority) best = candidate;
            }

            if (best == null || best == feed) return;

            feed = best;
            tracker?.Reset();
            Debug.Log($"{name}: using {best.DisplayName}.", this);
        }

        private void OnDestroy()
        {
            tracker?.Dispose();
            tracker = null;
        }

        private void Update()
        {
            if (tracker == null || feed == null || !feed.IsReady) return;
            if (Time.unscaledTime < nextInferenceTime) return;

            float started = Time.realtimeSinceStartup;
            nextInferenceTime = Time.unscaledTime + 1f / inferencesPerSecond;

            tracker.Mirrored = mirrored;
            if (tracker.TryTrack(feed.Texture, out var frame))
            {
                LatestFrame = frame;
                HasPose = true;
            }
            else
            {
                HasPose = false;
            }

            float elapsed = Mathf.Max(Time.realtimeSinceStartup - started, 1e-4f);
            InferenceRate = Mathf.Lerp(InferenceRate, 1f / elapsed, 0.1f);
        }
    }
}
