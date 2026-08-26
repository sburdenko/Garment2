using Garment.Fitting;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private PhotoFrameSource gallery;
        private bool uiVisible = true;

        /// <summary>Whether the control panel is on screen. The H key and gestures both flip it.</summary>
        public bool Visible
        {
            get => uiVisible;
            set => uiVisible = value;
        }

        private void Awake()
        {
            if (wardrobe == null) wardrobe = FindFirstObjectByType<Wardrobe.Wardrobe>();
            if (calibrator == null) calibrator = FindFirstObjectByType<BodyCalibrator>();
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            if (overlay == null) overlay = FindFirstObjectByType<PoseDebugOverlay>();

            sources = FindObjectsByType<FrameSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            gallery = FindFirstObjectByType<PhotoFrameSource>(FindObjectsInactive.Include);
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

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
                uiVisible = !uiVisible;

            if (keyboard == null || gallery == null || gallery.Count < 2) return;
            if (provider == null || provider.Feed != gallery) return;

            if (keyboard.leftArrowKey.wasPressedThisFrame) CyclePhoto(-1);
            else if (keyboard.rightArrowKey.wasPressedThisFrame) CyclePhoto(+1);
        }

        /// <summary>Another photo means another person: tracking and calibration start over.</summary>
        private void CyclePhoto(int delta)
        {
            gallery.Cycle(delta);
            provider.ResetTracking();
            if (calibrator != null) calibrator.ResetCalibration();
            if (wardrobe != null) wardrobe.Rebuild();
        }

        private void OnGUI()
        {
            if (!uiVisible) return;

            GUILayout.BeginArea(new Rect(12f, 12f, panelWidth, Screen.height - 24f), GUI.skin.box);

            if (GUILayout.Button("Hide UI (H)"))
            {
                uiVisible = false;
                GUILayout.EndArea();
                return;
            }

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

            if (gallery != null && gallery.Count > 1 && provider.Feed == gallery)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀", GUILayout.Width(40f))) CyclePhoto(-1);
                GUILayout.Label($"{gallery.CurrentIndex + 1} / {gallery.Count}  (arrow keys)",
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("▶", GUILayout.Width(40f))) CyclePhoto(+1);
                GUILayout.EndHorizontal();
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

            string trackingStatus = !provider.HasPose
                ? "No body found"
                : provider.IsBodyReady ? "Tracking — dressed" : "Step back, whole body must be in frame";
            GUILayout.Label(trackingStatus);
            GUILayout.Label($"Inference: {provider.InferenceRate:0} fps    Display: {1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):0} fps");

            if (overlay != null)
                overlay.Visible = GUILayout.Toggle(overlay.Visible, " Show skeleton");
            Fitting.LegCollisionPushout.Active =
                GUILayout.Toggle(Fitting.LegCollisionPushout.Active, " Leg collision");
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

            if (wardrobe.WornIn(GarmentSlot.Top) == null) return;

            GUILayout.Space(6f);
            GUILayout.Label($"Top fit: {wardrobe.TopFitScale * 100f:0}%");
            float scale = GUILayout.HorizontalSlider(wardrobe.TopFitScale, 0.85f, 1.15f);
            if (!Mathf.Approximately(scale, wardrobe.TopFitScale)) wardrobe.TopFitScale = scale;
        }
    }
}
