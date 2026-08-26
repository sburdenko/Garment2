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
