using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Where images come from. A live camera and a still photo are interchangeable here, which
    /// is what makes tracking testable without standing in front of the lens.
    /// </summary>
    public abstract class FrameSource : MonoBehaviour
    {
        public abstract Texture Texture { get; }

        public abstract bool IsReady { get; }

        /// <summary>Human-readable name for the source picker.</summary>
        public abstract string DisplayName { get; }

        /// <summary>Higher wins when several sources are ready. A supplied photo beats live video.</summary>
        public virtual int Priority => 0;

        /// <summary>Rotation the source reports; the image must be turned by this to look upright.</summary>
        public virtual int RotationDegrees => 0;

        public virtual bool IsVerticallyMirrored => false;

        public virtual float AspectRatio =>
            IsReady && Texture.height > 0 ? (float)Texture.width / Texture.height : 16f / 9f;
    }
}
