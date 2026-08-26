using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Draws the tracked skeleton over the camera image. This is the only honest way to judge
    /// whether tracking is good enough before wiring it to the avatar.
    /// </summary>
    public sealed class PoseDebugOverlay : MonoBehaviour
    {
        private static readonly (PoseLandmark from, PoseLandmark to)[] Bones =
        {
            (PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder),
            (PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow),
            (PoseLandmark.LeftElbow, PoseLandmark.LeftWrist),
            (PoseLandmark.RightShoulder, PoseLandmark.RightElbow),
            (PoseLandmark.RightElbow, PoseLandmark.RightWrist),
            (PoseLandmark.LeftShoulder, PoseLandmark.LeftHip),
            (PoseLandmark.RightShoulder, PoseLandmark.RightHip),
            (PoseLandmark.LeftHip, PoseLandmark.RightHip),
            (PoseLandmark.LeftHip, PoseLandmark.LeftKnee),
            (PoseLandmark.LeftKnee, PoseLandmark.LeftAnkle),
            (PoseLandmark.RightHip, PoseLandmark.RightKnee),
            (PoseLandmark.RightKnee, PoseLandmark.RightAnkle)
        };

        [SerializeField] private WebcamPoseProvider provider;
        [SerializeField] private Color boneColour = new Color(0.2f, 1f, 0.6f, 0.9f);
        [SerializeField] private float pointRadius = 5f;
        [SerializeField, Range(0f, 1f)] private float visibilityThreshold = 0.5f;
        [SerializeField] private bool showRoi = true;
        [SerializeField] private bool visible;

        private Material lineMaterial;

        public bool Visible
        {
            get => visible;
            set => visible = value;
        }

        private void Awake()
        {
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_ZWrite", 0);
            lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        private void OnGUI()
        {
            if (!visible || provider == null || !provider.HasPose) return;

            var frame = provider.LatestFrame;
            if (!frame.IsValid) return;

            var rect = FeedRect();
            bool mirrored = provider.Mirrored;
            bool lowerBodyVisible = frame.HasVisibleLowerBody(visibilityThreshold);

            GUI.color = boneColour;
            for (int i = 0; i < Bones.Length; i++)
            {
                if (!lowerBodyVisible && i >= 8) break;
                var (from, to) = Bones[i];
                if (frame.VisibilityOf(from) < visibilityThreshold || frame.VisibilityOf(to) < visibilityThreshold) continue;
                DrawLine(ToScreen(frame.ScreenOf(from), rect, mirrored), ToScreen(frame.ScreenOf(to), rect, mirrored));
            }

            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
            {
                if (!lowerBodyVisible && i >= (int)PoseLandmark.LeftKnee) continue;
                if (frame.Visibility[i] < visibilityThreshold) continue;
                var point = ToScreen(frame.Screen[i], rect, mirrored);
                GUI.DrawTexture(new Rect(point.x - pointRadius, point.y - pointRadius,
                    pointRadius * 2f, pointRadius * 2f), Texture2D.whiteTexture);
            }

            if (showRoi) DrawRoi(rect, mirrored);
            GUI.color = Color.white;
        }

        private void DrawRoi(Rect rect, bool mirrored)
        {
            var roi = provider.CurrentRoi;
            GUI.color = new Color(1f, 0.8f, 0.2f, 0.6f);

            var centre = ToScreen(roi.Centre, rect, mirrored);
            var extent = new Vector2(roi.HalfExtent.x * rect.width, roi.HalfExtent.y * rect.height);
            var box = new Rect(centre.x - extent.x, centre.y - extent.y, extent.x * 2f, extent.y * 2f);

            DrawLine(new Vector2(box.xMin, box.yMin), new Vector2(box.xMax, box.yMin));
            DrawLine(new Vector2(box.xMax, box.yMin), new Vector2(box.xMax, box.yMax));
            DrawLine(new Vector2(box.xMax, box.yMax), new Vector2(box.xMin, box.yMax));
            DrawLine(new Vector2(box.xMin, box.yMax), new Vector2(box.xMin, box.yMin));
        }

        /// <summary>The area the camera image actually covers, letterboxed to keep its aspect.</summary>
        private Rect FeedRect()
        {
            float aspect = provider.Feed != null ? provider.Feed.AspectRatio : 16f / 9f;
            float screenAspect = (float)Screen.width / Screen.height;

            if (screenAspect > aspect)
            {
                float width = Screen.height * aspect;
                return new Rect((Screen.width - width) * 0.5f, 0f, width, Screen.height);
            }

            float height = Screen.width / aspect;
            return new Rect(0f, (Screen.height - height) * 0.5f, Screen.width, height);
        }

        private static Vector2 ToScreen(Vector2 normalised, Rect rect, bool mirrored)
        {
            float x = mirrored ? 1f - normalised.x : normalised.x;
            return new Vector2(rect.x + x * rect.width, rect.y + (1f - normalised.y) * rect.height);
        }

        private static void DrawLine(Vector2 from, Vector2 to)
        {
            var delta = to - from;
            float length = delta.magnitude;
            if (length < 1f) return;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var pivot = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - 1.5f, length, 3f), Texture2D.whiteTexture);
            GUI.matrix = pivot;
        }
    }
}
