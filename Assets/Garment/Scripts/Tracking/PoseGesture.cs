using UnityEngine;

namespace Garment.Tracking
{
    public enum PoseGesture
    {
        None,
        RightHandRaised,
        LeftHandRaised,
        HeadTilted
    }

    /// <summary>
    /// Reads deliberate gestures out of the tracked landmarks.
    ///
    /// A gesture has to be HELD to count, then fires once and will not fire again until it has
    /// been let go: a person waving an arm about crosses every threshold there is, and a
    /// level-triggered gesture would fire on every frame of it.
    ///
    /// Raising a hand is the reliable one. On a full-body shot the head spans about 70 px across
    /// the ears where the arm spans 370, so anything read off the head carries a fifth of the
    /// signal — fine for toggling a panel, not for something that writes a file.
    /// </summary>
    public sealed class GestureRecognizer
    {
        /// <summary>How far above the nose a wrist must be, as a fraction of frame height.</summary>
        private const float HandClearance = 0.03f;
        private const float TiltDegrees = 18f;

        private readonly float holdSeconds;
        private readonly float repeatSeconds;

        private PoseGesture held = PoseGesture.None;
        private float heldSince;
        private bool fired;
        private float lastFiredAt = float.NegativeInfinity;

        public GestureRecognizer(float holdSeconds, float repeatSeconds)
        {
            this.holdSeconds = Mathf.Max(0f, holdSeconds);
            this.repeatSeconds = Mathf.Max(0f, repeatSeconds);
        }

        /// <summary>What is being held right now, whether or not it has fired.</summary>
        public PoseGesture Holding => held;

        /// <summary>How far through the hold the current gesture is, 0..1.</summary>
        public float HoldProgress(float time) =>
            held == PoseGesture.None || holdSeconds <= 0f
                ? 0f
                : Mathf.Clamp01((time - heldSince) / holdSeconds);

        public void Reset()
        {
            held = PoseGesture.None;
            fired = false;
        }

        /// <summary>The gesture that fired on this call, or None.</summary>
        public PoseGesture Update(PoseFrame frame, float frameAspect, float visibilityThreshold, float time)
        {
            var detected = frame.IsValid ? Detect(frame, frameAspect, visibilityThreshold) : PoseGesture.None;

            if (detected != held)
            {
                held = detected;
                heldSince = time;
                fired = false;
            }

            if (held == PoseGesture.None || fired) return PoseGesture.None;
            if (time - heldSince < holdSeconds) return PoseGesture.None;
            if (time - lastFiredAt < repeatSeconds) return PoseGesture.None;

            fired = true;
            lastFiredAt = time;
            return held;
        }

        private static PoseGesture Detect(PoseFrame frame, float frameAspect, float visibility)
        {
            if (frame.VisibilityOf(PoseLandmark.Nose) < visibility) return PoseGesture.None;
            float noseY = frame.ScreenOf(PoseLandmark.Nose).y;

            bool right = IsRaised(frame, PoseLandmark.RightWrist, noseY, visibility);
            bool left = IsRaised(frame, PoseLandmark.LeftWrist, noseY, visibility);

            // Both hands up is someone stretching, not asking for anything.
            if (right && !left) return PoseGesture.RightHandRaised;
            if (left && !right) return PoseGesture.LeftHandRaised;
            if (left && right) return PoseGesture.None;

            return IsHeadTilted(frame, frameAspect, visibility) ? PoseGesture.HeadTilted : PoseGesture.None;
        }

        private static bool IsRaised(PoseFrame frame, PoseLandmark wrist, float noseY, float visibility) =>
            frame.VisibilityOf(wrist) >= visibility &&
            frame.ScreenOf(wrist).y > noseY + HandClearance;

        /// <summary>
        /// Angle of the line between the ears. Frame UV is not square, so x has to be carried
        /// into the same units as y before the angle means anything.
        /// </summary>
        private static bool IsHeadTilted(PoseFrame frame, float frameAspect, float visibility)
        {
            if (frame.VisibilityOf(PoseLandmark.LeftEar) < visibility ||
                frame.VisibilityOf(PoseLandmark.RightEar) < visibility) return false;

            var left = frame.ScreenOf(PoseLandmark.LeftEar);
            var right = frame.ScreenOf(PoseLandmark.RightEar);
            var ears = new Vector2((left.x - right.x) * Mathf.Max(frameAspect, 1e-3f), left.y - right.y);
            if (ears.sqrMagnitude < 1e-8f) return false;

            float tilt = Mathf.Atan2(ears.y, ears.x) * Mathf.Rad2Deg;
            if (tilt > 90f) tilt -= 180f;
            else if (tilt < -90f) tilt += 180f;
            return Mathf.Abs(tilt) >= TiltDegrees;
        }
    }
}
