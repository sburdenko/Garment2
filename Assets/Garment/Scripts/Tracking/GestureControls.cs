using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Hands the fitting room over to the person standing in it: right hand up takes a picture,
    /// left hand up toggles the panel. Gestures are only read while the tracker is holding a
    /// whole body, so a half-seen pose cannot trip anything. Pictures only happen on the live
    /// camera — a gallery photo cannot lower its raised arm for the countdown, and a snapshot
    /// of a stock photo is not a snapshot of anyone.
    /// </summary>
    public sealed class GestureControls : MonoBehaviour
    {
        [SerializeField] private WebcamPoseProvider provider;
        [SerializeField] private SnapshotCapture snapshots;
        [SerializeField] private MirrorUI ui;
        [Tooltip("How long a gesture must be held before it counts.")]
        [SerializeField, Range(0.1f, 3f)] private float holdSeconds = 0.7f;
        [Tooltip("Shortest gap between two gestures firing.")]
        [SerializeField, Range(0f, 10f)] private float repeatSeconds = 1.5f;
        [SerializeField, Range(0f, 1f)] private float visibilityThreshold = 0.6f;
        [SerializeField] private bool showHints = true;

        private GestureRecognizer recognizer;

        private void Awake()
        {
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            if (snapshots == null) snapshots = FindFirstObjectByType<SnapshotCapture>();
            if (ui == null) ui = FindFirstObjectByType<MirrorUI>();
            recognizer = new GestureRecognizer(holdSeconds, repeatSeconds);
        }

        private void Update()
        {
            if (provider == null || recognizer == null) return;

            if (!provider.HasPose || !provider.IsBodyReady)
            {
                recognizer.Reset();
                return;
            }

            var fired = recognizer.Update(provider.LatestFrame, visibilityThreshold, Time.unscaledTime);

            switch (fired)
            {
                case PoseGesture.RightHandRaised:
                    if (snapshots != null && provider.Feed is WebcamFeed) snapshots.Request();
                    break;
                case PoseGesture.LeftHandRaised:
                    if (ui != null) ui.Visible = !ui.Visible;
                    break;
            }
        }

        private void OnGUI()
        {
            if (!showHints || provider == null || snapshots == null) return;

            if (snapshots.IsCountingDown)
            {
                DrawCentred(snapshots.SecondsRemaining.ToString(), 140);
                return;
            }

            if (recognizer == null) return;
            float progress = recognizer.HoldProgress(Time.unscaledTime);
            if (progress > 0f && progress < 1f) DrawCentred("hold…", 40);
        }

        private static void DrawCentred(string text, int fontSize)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };

            var area = new Rect(0f, Screen.height * 0.25f, Screen.width, Screen.height * 0.5f);
            var shadow = new Rect(area.x + 3f, area.y + 3f, area.width, area.height);

            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.Label(shadow, text, style);
            GUI.color = new Color(1f, 1f, 1f, 0.95f);
            GUI.Label(area, text, style);
            GUI.color = previous;
        }
    }
}
