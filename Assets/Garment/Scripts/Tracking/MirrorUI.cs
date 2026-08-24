using Garment.Fitting;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>Controls for the live fitting room: calibrate to the person, change the outfit.</summary>
    public sealed class MirrorUI : MonoBehaviour
    {
        [SerializeField] private Wardrobe.Wardrobe wardrobe;
        [SerializeField] private BodyCalibrator calibrator;
        [SerializeField] private WebcamPoseProvider provider;
        [SerializeField] private PoseDebugOverlay overlay;
        [SerializeField] private float panelWidth = 300f;

        private FrameSource[] sources;

        private void Awake()
        {
            if (wardrobe == null) wardrobe = FindFirstObjectByType<Wardrobe.Wardrobe>();
            if (calibrator == null) calibrator = FindFirstObjectByType<BodyCalibrator>();
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            if (overlay == null) overlay = FindFirstObjectByType<PoseDebugOverlay>();

            sources = FindObjectsByType<FrameSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (calibrator != null) calibrator.Calibrated += OnCalibrated;
        }

        private void OnDestroy()
        {
            if (calibrator != null) calibrator.Calibrated -= OnCalibrated;
        }

        /// <summary>The skeleton just changed shape, so every garment has to be fitted again.</summary>
        private void OnCalibrated()
        {
            if (wardrobe != null) wardrobe.Rebuild();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, panelWidth, Screen.height - 24f), GUI.skin.box);

            DrawSources();
            GUILayout.Space(8f);
            DrawTracking();
            GUILayout.Space(8f);
            DrawCalibration();
            GUILayout.Space(8f);
            DrawWardrobe();

            GUILayout.EndArea();
        }

        private void DrawSources()
        {
            if (provider == null || sources == null || sources.Length < 2) return;

            GUILayout.Label("Input");
            foreach (var source in sources)
            {
                if (source == null) continue;
                bool active = provider.Feed == source;
                string label = $"{(active ? "✓ " : string.Empty)}{source.DisplayName}";
                if (GUILayout.Button(label) && !active)
                {
                    source.gameObject.SetActive(true);
                    provider.UseSource(source);
                }
            }
        }

        private void DrawTracking()
        {
            GUILayout.Label("Tracking");
            if (provider == null)
            {
                GUILayout.Label("No tracker in scene.");
                return;
            }

            GUILayout.Label(provider.HasPose ? "Body found" : "No body — step back so you fit in frame");
            GUILayout.Label($"Inference: {provider.InferenceRate:0} fps    Display: {1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):0} fps");

            if (overlay != null)
                overlay.enabled = GUILayout.Toggle(overlay.enabled, " Show skeleton");
        }

        private void DrawCalibration()
        {
            GUILayout.Label("Fit to me");
            if (calibrator == null)
            {
                GUILayout.Label("No calibrator in scene.");
                return;
            }

            GUILayout.Label(calibrator.Status);

            GUI.enabled = !calibrator.IsSampling;
            if (GUILayout.Button(calibrator.HasCalibrated ? "Re-measure me" : "Measure me"))
                calibrator.BeginCalibration();
            GUI.enabled = true;

            GUILayout.Label("Stand facing the camera, arms out, whole body visible.");
        }

        private void DrawWardrobe()
        {
            GUILayout.Label("Wardrobe");
            if (wardrobe == null)
            {
                GUILayout.Label("No wardrobe in scene.");
                return;
            }

            foreach (var definition in wardrobe.Catalogue)
            {
                if (definition == null) continue;
                bool worn = wardrobe.WornIn(definition.Slot) == definition;
                if (GUILayout.Button($"{(worn ? "✓ " : string.Empty)}{definition.DisplayName}  ({definition.Slot})"))
                    wardrobe.Toggle(definition);
            }
        }
    }
}
