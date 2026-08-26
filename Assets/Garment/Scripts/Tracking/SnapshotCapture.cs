using System;
using System.IO;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Takes a picture of the fitting room a few seconds after being asked, so the gesture that
    /// asked for it is out of shot by the time the shutter goes.
    ///
    /// Renders the camera straight to a texture rather than grabbing the screen: IMGUI draws
    /// outside the camera, so the control panel and the countdown never land in the picture and
    /// there is no need to hide and restore anything. It is also synchronous, where a screen
    /// grab would have to wait for the end of the frame.
    /// </summary>
    public sealed class SnapshotCapture : MonoBehaviour
    {
        private enum Stage { Idle, CountingDown }

        [SerializeField] private Camera view;
        [SerializeField] private WebcamPoseProvider provider;
        [SerializeField] private PoseDebugOverlay overlay;
        [Tooltip("Seconds between asking for a picture and taking it.")]
        [SerializeField, Range(0f, 10f)] private float countdownSeconds = 3f;
        [Tooltip("Folder to write into, relative to the project (Editor) or to persistent data (build).")]
        [SerializeField] private string folderName = "Snapshots";

        private Stage stage = Stage.Idle;
        private float fireAt;

        /// <summary>Full path of the most recent picture, empty until one has been taken.</summary>
        public string LastSavedPath { get; private set; } = string.Empty;

        public bool IsCountingDown => stage == Stage.CountingDown;

        /// <summary>Whole seconds still to go, for something to draw.</summary>
        public int SecondsRemaining => Mathf.Max(1, Mathf.CeilToInt(fireAt - Time.unscaledTime));

        private void Awake()
        {
            if (view == null) view = Camera.main;
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            if (overlay == null) overlay = FindFirstObjectByType<PoseDebugOverlay>();
        }

        /// <summary>Start the countdown. Asking again while it runs does not restart it.</summary>
        public void Request()
        {
            if (stage == Stage.CountingDown) return;
            stage = Stage.CountingDown;
            fireAt = Time.unscaledTime + countdownSeconds;
        }

        public void Cancel() => stage = Stage.Idle;

        private void Update()
        {
            if (stage != Stage.CountingDown || Time.unscaledTime < fireAt) return;
            stage = Stage.Idle;
            Capture();
        }

        private void Capture()
        {
            if (view == null)
            {
                Debug.LogError($"{name}: no camera to take a picture with.", this);
                return;
            }

            int width = Mathf.Max(Screen.width, 16);
            int height = Mathf.Max(Screen.height, 16);

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previousTarget = view.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                view.targetTexture = target;
                view.Render();

                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);

                // The panel never lands in the picture (IMGUI draws outside the camera), but
                // the skeleton should when it is switched on — so it is drawn into the pixels.
                if (overlay != null && overlay.Visible && provider != null && provider.HasPose)
                    DrawSkeleton(texture);
                texture.Apply();

                string path = NextPath();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                LastSavedPath = path;
                Debug.Log($"Snapshot saved -> {path}", this);
            }
            catch (Exception error)
            {
                Debug.LogError($"{name}: could not save the snapshot: {error.Message}", this);
            }
            finally
            {
                view.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Destroy(target);
                Destroy(texture);
            }
        }

        private void DrawSkeleton(Texture2D texture)
        {
            var frame = provider.LatestFrame;
            if (!frame.IsValid) return;

            // Same letterbox as the display: the feed keeps its aspect inside the capture.
            float feedAspect = provider.Feed != null ? provider.Feed.AspectRatio : 1f;
            float captureAspect = (float)texture.width / texture.height;
            Rect rect = captureAspect > feedAspect
                ? new Rect((texture.width - texture.height * feedAspect) * 0.5f, 0f,
                           texture.height * feedAspect, texture.height)
                : new Rect(0f, (texture.height - texture.width / feedAspect) * 0.5f,
                           texture.width, texture.width / feedAspect);

            bool mirrored = provider.Mirrored;
            var colour = new Color(0.2f, 1f, 0.6f, 0.9f);
            const float visibility = 0.5f;

            System.Func<Vector2, Vector2> place = uv => new Vector2(
                rect.x + (mirrored ? 1f - uv.x : uv.x) * rect.width,
                rect.y + uv.y * rect.height);

            bool lowerBody = frame.HasVisibleLowerBody(visibility);
            var bones = PoseDebugOverlay.Bones;
            for (int i = 0; i < bones.Length; i++)
            {
                if (!lowerBody && i >= 8) break;
                var (from, to) = bones[i];
                if (frame.VisibilityOf(from) < visibility || frame.VisibilityOf(to) < visibility) continue;
                DrawLine(texture, place(frame.ScreenOf(from)), place(frame.ScreenOf(to)), colour);
            }

            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
            {
                if (!lowerBody && i >= (int)PoseLandmark.LeftKnee) continue;
                if (frame.Visibility[i] < visibility) continue;
                DrawDot(texture, place(frame.Screen[i]), 4, colour);
            }
        }

        private static void DrawLine(Texture2D texture, Vector2 from, Vector2 to, Color colour)
        {
            float length = Vector2.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length));
            for (int i = 0; i <= steps; i++)
                DrawDot(texture, Vector2.Lerp(from, to, i / (float)steps), 2, colour);
        }

        private static void DrawDot(Texture2D texture, Vector2 centre, int radius, Color colour)
        {
            int cx = Mathf.RoundToInt(centre.x);
            int cy = Mathf.RoundToInt(centre.y);
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (y < 0 || y >= texture.height) continue;
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || x >= texture.width) continue;
                    texture.SetPixel(x, y, colour);
                }
            }
        }

        private string NextPath()
        {
            var folder = Path.Combine(RootFolder(), folderName);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"fitting-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        }

        /// <summary>
        /// Beside Assets in the Editor, so the pictures land in the project where they can be
        /// found. A built player has no project folder, so they go where its data lives.
        /// </summary>
        private static string RootFolder() =>
            Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                : Application.persistentDataPath;
    }
}
