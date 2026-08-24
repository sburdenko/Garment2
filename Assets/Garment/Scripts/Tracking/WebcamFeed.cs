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
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogError("WebcamFeed: no camera devices available.", this);
                return;
            }

            int index = Mathf.Clamp(deviceIndex, 0, devices.Length - 1);
            texture = new WebCamTexture(devices[index].name, requestedWidth, requestedHeight, requestedFps);
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
