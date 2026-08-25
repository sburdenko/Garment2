using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// The square of the camera image fed to the landmark model: where it sits, how big it is,
    /// and how it is rotated. Keeping it tight around the body is what makes the landmarks
    /// accurate — the model is nearly blind on a full uncropped frame.
    ///
    /// The crop must be square *in pixels*. UV extents are not pixel extents on a non-square
    /// frame, and a stretched crop makes the model report a squashed body — short legs on a
    /// portrait photo, for instance.
    /// </summary>
    public readonly struct PoseRoi
    {
        private const float MinimumTrackedHalfSide = 0.1f;

        /// <summary>Centre in source UV, 0..1.</summary>
        public readonly Vector2 Centre;

        /// <summary>Half the crop's width and height in source UV.</summary>
        public readonly Vector2 HalfExtent;

        /// <summary>Rotation applied when sampling, in radians.</summary>
        public readonly float Angle;

        public PoseRoi(Vector2 centre, Vector2 halfExtent, float angle)
        {
            Centre = centre;
            HalfExtent = halfExtent;
            Angle = angle;
        }

        /// <summary>The largest upright square that fits the frame — used before a body is found.</summary>
        public static PoseRoi FullFrame(float aspectRatio)
        {
            // Half a side, measured as a fraction of frame width.
            float halfSide = aspectRatio >= 1f ? 0.5f / aspectRatio : 0.5f;
            return new PoseRoi(new Vector2(0.5f, 0.5f), ExtentFor(halfSide, aspectRatio), 0f);
        }

        /// <summary>Maps a coordinate inside the crop back to where it came from in the source.</summary>
        public Vector2 ToSource(Vector2 cropUv, bool mirrored)
        {
            var centred = cropUv * 2f - Vector2.one;
            if (mirrored) centred.x = -centred.x;

            float sin = Mathf.Sin(Angle);
            float cos = Mathf.Cos(Angle);
            var rotated = new Vector2(
                centred.x * cos - centred.y * sin,
                centred.x * sin + centred.y * cos);

            return Centre + Vector2.Scale(rotated, HalfExtent);
        }

        /// <summary>Maps a source coordinate into the crop — the inverse of ToSource.</summary>
        public Vector2 FromSource(Vector2 sourceUv, bool mirrored)
        {
            var rel = sourceUv - Centre;
            rel = new Vector2(
                HalfExtent.x > 1e-5f ? rel.x / HalfExtent.x : 0f,
                HalfExtent.y > 1e-5f ? rel.y / HalfExtent.y : 0f);

            float sin = Mathf.Sin(-Angle);
            float cos = Mathf.Cos(-Angle);
            var rotated = new Vector2(
                rel.x * cos - rel.y * sin,
                rel.x * sin + rel.y * cos);

            if (mirrored) rotated.x = -rotated.x;
            return (rotated + Vector2.one) * 0.5f;
        }

        /// <summary>
        /// Builds the next ROI to enclose the landmarks just measured.
        ///
        /// Sizing from the model's two auxiliary points instead is what MediaPipe does, but that
        /// loop only holds still if the crop is already right: the points are reported in crop
        /// space, so an oversized crop asks for an even bigger one and it runs away within a
        /// second. Framing the actual skeleton is self-correcting — a crop that is too big still
        /// sees where the body is and pulls back in.
        /// </summary>
        public static PoseRoi FromLandmarks(Vector2[] screen, float[] visibility, float visibilityThreshold,
                                            float aspectRatio, float padding)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            int counted = 0;

            for (int i = 0; i < screen.Length; i++)
            {
                if (visibility[i] < visibilityThreshold) continue;
                if (screen[i].x < 0f || screen[i].x > 1f || screen[i].y < 0f || screen[i].y > 1f) continue;
                min = Vector2.Min(min, screen[i]);
                max = Vector2.Max(max, screen[i]);
                counted++;
            }

            if (counted < 4) return FullFrame(aspectRatio);

            var centre = (min + max) * 0.5f;
            var size = max - min;

            // Compare both axes as fractions of frame width.
            float widthInWidths = size.x;
            float heightInWidths = aspectRatio > 1e-4f ? size.y / aspectRatio : size.y;

            float halfSide = Mathf.Max(widthInWidths, heightInWidths) * 0.5f * padding;
            if (halfSide < MinimumTrackedHalfSide) return FullFrame(aspectRatio);

            centre.x = Mathf.Clamp01(centre.x);
            centre.y = Mathf.Clamp01(centre.y);
            return new PoseRoi(centre, ExtentFor(halfSide, aspectRatio), 0f);
        }

        /// <summary>Turns a half-side given in frame widths into UV extents that stay square in pixels.</summary>
        private static Vector2 ExtentFor(float halfSideInWidths, float aspectRatio)
        {
            float halfX = Mathf.Clamp(halfSideInWidths, 0.02f, 1f);
            return new Vector2(halfX, halfX * aspectRatio);
        }

        public override string ToString() =>
            $"centre={Centre} half={HalfExtent} angle={Angle * Mathf.Rad2Deg:0.0}°";
    }
}
