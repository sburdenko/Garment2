using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>A gallery of stills standing in for the camera, for repeatable testing.</summary>
    public sealed class PhotoFrameSource : FrameSource
    {
        [Tooltip("Photos of a person, whole body in shot. Arrow keys cycle through them.")]
        [SerializeField] private Texture2D[] photos = new Texture2D[0];
        [SerializeField] private int current;

        public override Texture Texture => CurrentPhoto;

        public override bool IsReady => CurrentPhoto != null;

        public override int Priority => 10;

        public override string DisplayName =>
            CurrentPhoto != null
                ? $"Photo: {CurrentPhoto.name}" + (photos.Length > 1 ? $" ({CurrentIndex + 1}/{photos.Length})" : string.Empty)
                : "Photo (none set)";

        public int Count => photos?.Length ?? 0;

        public int CurrentIndex => photos == null || photos.Length == 0 ? 0 : Mathf.Clamp(current, 0, photos.Length - 1);

        public Texture2D CurrentPhoto => photos != null && photos.Length > 0 ? photos[CurrentIndex] : null;

        /// <summary>Kept for callers that treat the source as a single photo.</summary>
        public Texture2D Photo
        {
            get => CurrentPhoto;
            set
            {
                if (value == null) return;
                photos = new[] { value };
                current = 0;
            }
        }

        /// <summary>Step through the gallery; wraps around at either end.</summary>
        public void Cycle(int delta)
        {
            if (photos == null || photos.Length < 2) return;
            current = (CurrentIndex + delta + photos.Length) % photos.Length;
        }
    }
}
