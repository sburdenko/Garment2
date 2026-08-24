using Garment.Body;
using Garment.Fitting;
using UnityEngine;

namespace Garment.Sandbox
{
    /// <summary>Test harness controls: pose, speed, outfit, and clipping readout.</summary>
    public sealed class SandboxUI : MonoBehaviour
    {
        [SerializeField] private Wardrobe.Wardrobe wardrobe;
        [SerializeField] private ProceduralPoseSource poseSource;
        [SerializeField] private ClippingProbe clippingProbe;
        [SerializeField] private float panelWidth = 280f;

        private float timeScale = 1f;
        private bool bodyVisible = true;

        private void Awake()
        {
            if (wardrobe == null) wardrobe = FindFirstObjectByType<Wardrobe.Wardrobe>();
            if (poseSource == null) poseSource = FindFirstObjectByType<ProceduralPoseSource>();
            if (clippingProbe == null) clippingProbe = FindFirstObjectByType<ClippingProbe>();
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, panelWidth, Screen.height - 24f), GUI.skin.box);

            DrawPoseControls();
            GUILayout.Space(8f);
            DrawWardrobeControls();
            GUILayout.Space(8f);
            DrawDiagnostics();

            GUILayout.EndArea();
        }

        private void DrawPoseControls()
        {
            GUILayout.Label("<b>Pose</b>", RichLabel);
            if (poseSource == null)
            {
                GUILayout.Label("No pose source in scene.");
                return;
            }

            foreach (DemoPose pose in System.Enum.GetValues(typeof(DemoPose)))
            {
                bool active = poseSource.Pose == pose;
                if (GUILayout.Toggle(active, pose.ToString(), GUI.skin.button) && !active)
                {
                    poseSource.Pose = pose;
                    poseSource.ResetToBindPose();
                }
            }

            GUILayout.Label($"Motion speed: {poseSource.Speed:0.00}x");
            poseSource.Speed = GUILayout.HorizontalSlider(poseSource.Speed, 0.1f, 3f);

            GUILayout.Label($"Time scale: {timeScale:0.00}x");
            timeScale = GUILayout.HorizontalSlider(timeScale, 0.05f, 1.5f);
            Time.timeScale = timeScale;
        }

        private void DrawWardrobeControls()
        {
            GUILayout.Label("<b>Wardrobe</b>", RichLabel);
            if (wardrobe == null)
            {
                GUILayout.Label("No wardrobe in scene.");
                return;
            }

            foreach (var definition in wardrobe.Catalogue)
            {
                if (definition == null) continue;
                bool worn = wardrobe.WornIn(definition.Slot) == definition;
                string label = $"{(worn ? "✓ " : string.Empty)}{definition.DisplayName}  ({definition.Slot})";
                if (GUILayout.Button(label)) wardrobe.Toggle(definition);
            }

            bool wantBodyVisible = GUILayout.Toggle(bodyVisible, " Show body");
            if (wantBodyVisible != bodyVisible)
            {
                bodyVisible = wantBodyVisible;
                var bodyMesh = wardrobe.Body != null ? wardrobe.Body.BodyMesh : null;
                if (bodyMesh != null) bodyMesh.enabled = bodyVisible;
            }
        }

        private void DrawDiagnostics()
        {
            GUILayout.Label("<b>Clipping</b>", RichLabel);
            if (clippingProbe == null)
            {
                GUILayout.Label("No clipping probe in scene.");
                return;
            }

            var report = clippingProbe.LatestReport;
            if (report.SampledVertices == 0)
            {
                GUILayout.Label("Nothing worn.");
                return;
            }

            GUILayout.Label($"Vertices below skin: {report.Ratio * 100f:0.0}%  ({report.PenetratingVertices}/{report.SampledVertices})");
            GUILayout.Label($"Deepest penetration: {report.MaxDepth * 1000f:0.0} mm");
            GUILayout.Label($"FPS: {1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):0}");
        }

        private static GUIStyle RichLabel
        {
            get
            {
                var style = new GUIStyle(GUI.skin.label) { richText = true };
                return style;
            }
        }
    }
}
