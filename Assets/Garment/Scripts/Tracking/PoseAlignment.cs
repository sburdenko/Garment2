using Garment.Body;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Places the avatar so that it lands on top of the person in the camera image: the torso
    /// is put at the depth where it covers the same fraction of the frame, then slid sideways
    /// until the hips project onto the tracked hips.
    /// </summary>
    public sealed class PoseAlignment : MonoBehaviour
    {
        [SerializeField] private BodyRig rig;
        [SerializeField] private WebcamPoseProvider provider;
        [SerializeField] private Camera view;
        [SerializeField] private BodyCalibrator calibrator;

        [Tooltip("How far the avatar may be pushed from the camera, in metres.")]
        [SerializeField] private Vector2 depthRange = new Vector2(1f, 6f);
        [Tooltip("Higher settles faster but shakes more.")]
        [SerializeField, Range(1f, 30f)] private float smoothing = 8f;
        [SerializeField, Range(0f, 1f)] private float visibilityThreshold = 0.5f;
        [Tooltip("Anchor movement slower than this (Hz) is treated as jitter and smoothed away.")]
        [SerializeField, Range(0.1f, 10f)] private float anchorJitterCutoff = 1f;
        [Tooltip("How much a moving anchor relaxes the smoothing. Higher follows faster.")]
        [SerializeField, Range(0f, 100f)] private float anchorSpeedResponse = 20f;

        private Vector3 targetPosition;
        private bool hasTarget;
        private OneEuroFilterVector3 anchorFilter;
        private OneEuroFilter spanFilter;

        // The pinhole depth formula assumes the real camera's tilt and FOV, which are unknown.
        // This factor closes the loop: compare how big the avatar actually projects against the
        // tracked person and nudge the depth until the two spans match on screen.
        private float depthCorrection = 1f;

        private void Awake()
        {
            if (rig == null) rig = GetComponent<BodyRig>();
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            if (view == null) view = Camera.main;
            if (calibrator == null) calibrator = FindFirstObjectByType<BodyCalibrator>();
        }

        private void LateUpdate()
        {
            if (provider == null || !provider.HasPose) return;
            AlignTo(provider.LatestFrame, Time.deltaTime, provider.Coverage);
        }

        /// <summary>
        /// Place the avatar for a frame supplied directly, bypassing the live tracker. A lone
        /// frame has no history to debounce, so its own visibility decides the coverage.
        /// </summary>
        public void AlignTo(PoseFrame frame, float deltaTime)
        {
            if (!frame.IsValid) return;
            AlignTo(frame, deltaTime,
                frame.HasVisibleLowerBody(visibilityThreshold) ? BodyCoverage.FullBody : BodyCoverage.UpperBody);
        }

        public void AlignTo(PoseFrame frame, float deltaTime, BodyCoverage coverage)
        {
            if (rig == null || provider == null || view == null) return;
            if (!frame.IsValid || !IsTorsoVisible(frame)) return;

            bool anklesVisible = coverage == BodyCoverage.FullBody;

            var hipsScreen = Midpoint(frame, PoseLandmark.LeftHip, PoseLandmark.RightHip);
            var shoulderScreen = Midpoint(frame, PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder);

            // One basis, always: the torso is the only span visible in every framing, and a span
            // that never changes is a depth that never jumps. The legs, when they are there,
            // refine the result through the correction loop rather than replacing the basis.
            float measuredSpan = TrackedTorsoSpan(frame, shoulderScreen, hipsScreen) * VerticalFrameScale();
            float avatarSpan = TorsoLength();
            if (measuredSpan < 0.02f || avatarSpan <= 0f) return;

            if (spanFilter == null) spanFilter = new OneEuroFilter(anchorJitterCutoff, anchorSpeedResponse);
            measuredSpan = spanFilter.Filter(measuredSpan, deltaTime);

            UpdateDepthCorrection(frame, anklesVisible, deltaTime);

            float depth = DepthForSpan(avatarSpan, measuredSpan) * depthCorrection;
            if (depth <= 0f) return;
            depth = Mathf.Clamp(depth, depthRange.x, depthRange.y);

            // Weighted anchor across shoulders, ankles and hips. The photo's perspective
            // distributes torso and legs differently than the avatar's true proportions, so
            // some joint must absorb the residual — and it should be the hips: a low neckline
            // or a bare arm above the sleeve is the first thing anyone notices, a waistband a
            // few centimetres off hides under the top. Shoulders and ankles get the weight.
            // Framed waist-up the hips sit at the frame edge where the tracker is noisiest,
            // so they get even less say.
            const float shoulderWeight = 2f, ankleWeight = 2f;
            float hipWeight = anklesVisible ? 1f : 0.5f;

            var hipsBone = rig.GetBone(BodyLandmark.Hips);
            var leftShoulderBone = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulderBone = rig.GetBone(BodyLandmark.RightShoulder);
            if (hipsBone == null || leftShoulderBone == null || rightShoulderBone == null) return;

            var anchorScreen = hipsScreen * hipWeight + shoulderScreen * shoulderWeight;
            var anchorBone = hipsBone.position * hipWeight
                           + (leftShoulderBone.position + rightShoulderBone.position) * 0.5f * shoulderWeight;
            float anchorTotal = hipWeight + shoulderWeight;

            if (anklesVisible)
            {
                var leftAnkleBone = rig.GetBone(BodyLandmark.LeftAnkle);
                var rightAnkleBone = rig.GetBone(BodyLandmark.RightAnkle);
                if (leftAnkleBone != null && rightAnkleBone != null)
                {
                    anchorScreen += Midpoint(frame, PoseLandmark.LeftAnkle, PoseLandmark.RightAnkle) * ankleWeight;
                    anchorBone += (leftAnkleBone.position + rightAnkleBone.position) * 0.5f * ankleWeight;
                    anchorTotal += ankleWeight;
                }
            }
            anchorScreen /= anchorTotal;
            anchorBone /= anchorTotal;

            if (anchorFilter == null) anchorFilter = new OneEuroFilterVector3(anchorJitterCutoff, anchorSpeedResponse);
            anchorScreen = anchorFilter.Filter(anchorScreen, deltaTime);

            if (provider.Mirrored) anchorScreen.x = 1f - anchorScreen.x;

            var viewportAnchor = FrameToViewport(anchorScreen);
            var wanted = view.ViewportToWorldPoint(new Vector3(viewportAnchor.x, viewportAnchor.y, depth));

            // Move the root, not the bone: the bones' own transforms are the tracker's to write.
            var offset = rig.transform.position - anchorBone;
            targetPosition = wanted + offset;

            if (!hasTarget)
            {
                rig.transform.position = targetPosition;
                hasTarget = true;
                return;
            }

            float blend = 1f - Mathf.Exp(-smoothing * Mathf.Max(deltaTime, 1e-4f));
            rig.transform.position = Vector3.Lerp(rig.transform.position, targetPosition, blend);
        }

        /// <summary>
        /// Measures how tall the avatar actually is on screen right now versus the person, and
        /// folds the ratio into the depth. Converges in a few frames and absorbs whatever the
        /// unknown real camera does that the ideal formula misses.
        /// </summary>
        private void UpdateDepthCorrection(PoseFrame frame, bool anklesVisible, float deltaTime)
        {
            if (!hasTarget) return;

            // Close the loop on the longest span currently in view: shoulders to ankles when
            // the legs are there, shoulders to hips otherwise. Both converge on the same true
            // depth — the long one just gets there with less noise. Measuring only when the
            // ankles show would leave the avatar sized by a stale correction exactly in the
            // waist-up framing that needs it most.
            var lower = anklesVisible ? BodyLandmark.LeftAnkle : BodyLandmark.Hips;
            var lowerScreen = anklesVisible
                ? Midpoint(frame, PoseLandmark.LeftAnkle, PoseLandmark.RightAnkle)
                : Midpoint(frame, PoseLandmark.LeftHip, PoseLandmark.RightHip);
            var shoulderScreen = Midpoint(frame, PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder);

            float measuredSpan = Mathf.Abs(shoulderScreen.y - lowerScreen.y) * VerticalFrameScale();
            if (measuredSpan < 0.02f) return;

            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);
            var lowerBone = rig.GetBone(lower);
            if (leftShoulder == null || rightShoulder == null || lowerBone == null) return;

            float shoulderY = view.WorldToViewportPoint((leftShoulder.position + rightShoulder.position) * 0.5f).y;
            float lowerY = view.WorldToViewportPoint(lowerBone.position).y;
            float avatarProjected = Mathf.Abs(shoulderY - lowerY);
            if (avatarProjected < 0.02f) return;

            // Avatar too small on screen -> ratio < 1 -> pull it closer.
            float wanted = depthCorrection * (avatarProjected / measuredSpan);
            wanted = Mathf.Clamp(wanted, 0.7f, 1.3f);

            float blend = 1f - Mathf.Exp(-smoothing * Mathf.Max(deltaTime, 1e-4f));
            depthCorrection = Mathf.Lerp(depthCorrection, wanted, blend);
        }

        /// <summary>
        /// At distance d the camera sees 2·d·tan(fov/2) metres of height, so a span of known
        /// length filling a known fraction of the frame pins down d.
        /// </summary>
        private float DepthForSpan(float spanLength, float measuredFraction)
        {
            float halfFovTangent = Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (halfFovTangent <= 1e-4f) return 0f;

            float depth = spanLength / (2f * measuredFraction * halfFovTangent);
            return Mathf.Clamp(depth, depthRange.x, depthRange.y);
        }

        /// <summary>
        /// The person's torso height on screen.
        ///
        /// Measuring it straight off the hip landmark is the obvious way and the shaky one: in a
        /// waist-up framing the hips sit at the very bottom of the picture, half out of it, and
        /// the tracker guesses. Shoulder width is measured between two points sitting well
        /// inside the frame, so once calibration knows how long this person's torso is per unit
        /// of shoulder width, that proportion turns a steady measurement into a steady span.
        ///
        /// Trust follows the hip landmark's own visibility, and the shoulder estimate only
        /// stands in when there is a calibrated proportion to stand in with. It foreshortens
        /// when the person turns side-on, which is why it never fully replaces the direct read.
        /// </summary>
        private float TrackedTorsoSpan(PoseFrame frame, Vector2 shoulderScreen, Vector2 hipsScreen)
        {
            float direct = Mathf.Abs(shoulderScreen.y - hipsScreen.y);

            float torsoPerShoulder = calibrator != null ? calibrator.LastMeasurements.TorsoPerShoulder : 0f;
            if (calibrator == null || !calibrator.HasCalibrated || torsoPerShoulder <= 0f) return direct;

            // Shoulder width runs across the frame, the torso down it; frame UV is not square,
            // so the width has to be carried into vertical units before the ratio applies.
            var left = frame.ScreenOf(PoseLandmark.LeftShoulder);
            var right = frame.ScreenOf(PoseLandmark.RightShoulder);
            float aspect = provider.Feed != null ? provider.Feed.AspectRatio : 1f;
            float shoulderSpan = new Vector2((left.x - right.x) * aspect, left.y - right.y).magnitude;
            if (shoulderSpan < 1e-3f) return direct;

            float derived = shoulderSpan * torsoPerShoulder;

            float hipConfidence = Mathf.Min(frame.VisibilityOf(PoseLandmark.LeftHip),
                                            frame.VisibilityOf(PoseLandmark.RightHip));
            return Mathf.Lerp(derived, direct, Mathf.Clamp01(hipConfidence));
        }

        /// <summary>
        /// Must measure the same span the tracker does — hips to the midpoint of the shoulders.
        /// Measuring to the chest instead makes the avatar sit too close and read as oversized.
        /// </summary>
        private float TorsoLength()
        {
            var hips = rig.GetBone(BodyLandmark.Hips);
            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);
            if (hips == null || leftShoulder == null || rightShoulder == null) return 0f;

            var shoulderCentre = (leftShoulder.position + rightShoulder.position) * 0.5f;
            return Mathf.Abs(shoulderCentre.y - hips.position.y);
        }

        /// <summary>
        /// The feed is letterboxed into the view, so a fraction of the camera image is not the
        /// same fraction of the viewport. Everything drawn over the feed has to agree on this.
        /// </summary>
        private Vector2 FrameToViewport(Vector2 frameUv)
        {
            float scale = FrameAspect() / view.aspect;
            if (scale <= 1f) return new Vector2(0.5f + (frameUv.x - 0.5f) * scale, frameUv.y);
            return new Vector2(frameUv.x, 0.5f + (frameUv.y - 0.5f) / scale);
        }

        private float VerticalFrameScale()
        {
            float scale = FrameAspect() / view.aspect;
            return scale <= 1f ? 1f : 1f / scale;
        }

        private float FrameAspect() =>
            provider.Feed != null ? provider.Feed.AspectRatio : view.aspect;

        private bool IsTorsoVisible(PoseFrame frame) =>
            frame.VisibilityOf(PoseLandmark.LeftHip) >= visibilityThreshold &&
            frame.VisibilityOf(PoseLandmark.RightHip) >= visibilityThreshold &&
            frame.VisibilityOf(PoseLandmark.LeftShoulder) >= visibilityThreshold &&
            frame.VisibilityOf(PoseLandmark.RightShoulder) >= visibilityThreshold;

        private static Vector2 Midpoint(PoseFrame frame, PoseLandmark a, PoseLandmark b) =>
            (frame.ScreenOf(a) + frame.ScreenOf(b)) * 0.5f;
    }
}
