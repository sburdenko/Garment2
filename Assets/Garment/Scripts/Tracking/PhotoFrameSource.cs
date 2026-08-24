using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>A still image standing in for the camera, for repeatable testing.</summary>
    public sealed class PhotoFrameSource : FrameSource
    {
        [Tooltip("A photo of a person, whole body in shot.")]
        [SerializeField] private Texture2D photo;

        public override Texture Texture => photo;

        public override bool IsReady => photo != null;

        public override int Priority => 10;

        public override string DisplayName => photo != null ? $"Photo: {photo.name}" : "Photo (none set)";

        public Texture2D Photo
        {
            get => photo;
            set => photo = value;
        }
    }
}
