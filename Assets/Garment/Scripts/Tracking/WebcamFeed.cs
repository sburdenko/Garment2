using System.Collections;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>Opens a camera and exposes its live texture. The only place the app touches hardware.</summary>
    public sealed class WebcamFeed : FrameSource
    {
        [SerializeField] private int requestedWidth = 1280;
        [SerializeField] private int requestedHeight = 720;
        [SerializeField] private int requestedFps = 30;
        [Tooltip("Index into WebCamTexture.devices. 0 is the system default.")]
        [SerializeField] private int deviceIndex;

        private WebCamTexture texture;

        public override Texture Texture => texture;

        public override bool IsReady => texture != null && texture.isPlaying && texture.width > 16;

        public override string DisplayName => texture != null ? $"Camera: {texture.deviceName}" : "Camera";

        public override int RotationDegrees => texture != null ? texture.videoRotationAngle : 0;

        public override bool IsVerticallyMirrored => texture != null && texture.videoVerticallyMirrored;

        private void OnEnable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(StartCameraWithPermission());
#else
            StartCamera();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator StartCameraWithPermission()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("WebcamFeed: camera permission was denied.", this);
                yield break;
            }

            StartCamera();
        }
#endif

        private void StartCamera()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogError("WebcamFeed: no camera devices available.", this);
                return;
            }

            int index = Mathf.Clamp(deviceIndex, 0, devices.Length - 1);
#if UNITY_WEBGL && !UNITY_EDITOR
            // Asking the browser for an exact size gets the stream scaled into a texture of
            // THAT shape, so a 4:3 webcam handed a 16:9 request comes out stretched wide and
            // no amount of letterboxing downstream can undo it. Take what the camera gives.
            texture = new WebCamTexture(devices[index].name);
#else
            texture = new WebCamTexture(devices[index].name, requestedWidth, requestedHeight, requestedFps);
#endif
            texture.Play();

            if (!texture.isPlaying)
                Debug.LogError($"WebcamFeed: '{devices[index].name}' did not start. Check camera permission for the Unity Editor.", this);
        }

        private void OnDisable()
        {
            if (texture == null) return;
            texture.Stop();
            Destroy(texture);
            texture = null;
        }
    }
}
