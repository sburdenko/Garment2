using UnityEngine;

namespace Garment.Tracking
{
    public enum PoseGesture
    {
        None,
        RightHandRaised,
        LeftHandRaised
    }

    /// <summary>
    /// Reads deliberate gestures out of the tracked landmarks: one hand raised above the head,
    /// left or right. (A head tilt was tried for the panel toggle and retired — at 70 px across
    /// the ears it sat too close to tracker noise, and a static photo of a tilted head kept
    /// triggering it.)
    ///
    /// A gesture has to be HELD to count, then fires once and will not fire again until it has
    /// been let go: a person waving an arm about crosses every threshold there is, and a
    /// level-triggered gesture would fire on every frame of it.
    /// </summary>
    public sealed class GestureRecognizer
    {
        /// <summary>How far above the nose a wrist must be, as a fraction of frame height.</summary>
        private const float HandClearance = 0.03f;

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
        public PoseGesture Update(PoseFrame frame, float visibilityThreshold, float time)
        {
            var detected = frame.IsValid ? Detect(frame, visibilityThreshold) : PoseGesture.None;

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

        private static PoseGesture Detect(PoseFrame frame, float visibility)
        {
            if (frame.VisibilityOf(PoseLandmark.Nose) < visibility) return PoseGesture.None;
            float noseY = frame.ScreenOf(PoseLandmark.Nose).y;

            bool right = IsRaised(frame, PoseLandmark.RightWrist, noseY, visibility);
            bool left = IsRaised(frame, PoseLandmark.LeftWrist, noseY, visibility);

            // Both hands up is someone stretching, not asking for anything.
            if (right && !left) return PoseGesture.RightHandRaised;
            if (left && !right) return PoseGesture.LeftHandRaised;
            return PoseGesture.None;
        }

        private static bool IsRaised(PoseFrame frame, PoseLandmark wrist, float noseY, float visibility) =>
            frame.VisibilityOf(wrist) >= visibility &&
            frame.ScreenOf(wrist).y > noseY + HandClearance;

    }
}
