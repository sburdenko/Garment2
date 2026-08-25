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

        [Tooltip("How far the avatar may be pushed from the camera, in metres.")]
        [SerializeField] private Vector2 depthRange = new Vector2(1f, 6f);
        [Tooltip("Higher settles faster but shakes more.")]
        [SerializeField, Range(1f, 30f)] private float smoothing = 8f;
        [SerializeField, Range(0f, 1f)] private float visibilityThreshold = 0.5f;

        private Vector3 targetPosition;
        private bool hasTarget;

        // The pinhole depth formula assumes the real camera's tilt and FOV, which are unknown.
        // This factor closes the loop: compare how big the avatar actually projects against the
        // tracked person and nudge the depth until the two spans match on screen.
        private float depthCorrection = 1f;

        private void Awake()
        {
            if (rig == null) rig = GetComponent<BodyRig>();
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            if (view == null) view = Camera.main;
        }

        private void LateUpdate()
        {
            if (provider == null || !provider.HasPose) return;
            AlignTo(provider.LatestFrame, Time.deltaTime);
        }

        /// <summary>Place the avatar for a frame supplied directly, bypassing the live tracker.</summary>
        public void AlignTo(PoseFrame frame, float deltaTime)
        {
            if (rig == null || provider == null || view == null) return;
            if (!frame.IsValid || !IsTorsoVisible(frame)) return;

            var hipsScreen = Midpoint(frame, PoseLandmark.LeftHip, PoseLandmark.RightHip);
            var shoulderScreen = Midpoint(frame, PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder);

            // Scale off the longest visible span. The camera that took the picture is unknown,
            // so perspective will never match exactly; a long baseline spreads that error out
            // instead of concentrating it in the legs.
            float measuredSpan;
            float avatarSpan;
            if (AreAnklesVisible(frame))
            {
                var ankleScreen = Midpoint(frame, PoseLandmark.LeftAnkle, PoseLandmark.RightAnkle);
                measuredSpan = Mathf.Abs(shoulderScreen.y - ankleScreen.y);
                avatarSpan = AvatarSpan(BodyLandmark.LeftAnkle);
            }
            else
            {
                measuredSpan = Mathf.Abs(shoulderScreen.y - hipsScreen.y);
                avatarSpan = TorsoLength();
            }

            measuredSpan *= VerticalFrameScale();
            if (measuredSpan < 0.02f || avatarSpan <= 0f) return;

            UpdateDepthCorrection(frame, measuredSpan, deltaTime);

            float depth = DepthForSpan(avatarSpan, measuredSpan) * depthCorrection;
            if (depth <= 0f) return;
            depth = Mathf.Clamp(depth, depthRange.x, depthRange.y);

            // Weighted anchor across shoulders, ankles and hips. The photo's perspective
            // distributes torso and legs differently than the avatar's true proportions, so
            // some joint must absorb the residual — and it should be the hips: a low neckline
            // or a bare arm above the sleeve is the first thing anyone notices, a waistband a
            // few centimetres off hides under the top. Shoulders and ankles get the weight.
            const float shoulderWeight = 2f, ankleWeight = 2f, hipWeight = 1f;

            var hipsBone = rig.GetBone(BodyLandmark.Hips);
            var leftShoulderBone = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulderBone = rig.GetBone(BodyLandmark.RightShoulder);
            if (hipsBone == null || leftShoulderBone == null || rightShoulderBone == null) return;

            var anchorScreen = hipsScreen * hipWeight + shoulderScreen * shoulderWeight;
            var anchorBone = hipsBone.position * hipWeight
                           + (leftShoulderBone.position + rightShoulderBone.position) * 0.5f * shoulderWeight;
            float anchorTotal = hipWeight + shoulderWeight;

            if (AreAnklesVisible(frame))
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
        private void UpdateDepthCorrection(PoseFrame frame, float measuredSpan, float deltaTime)
        {
            if (!hasTarget || !AreAnklesVisible(frame)) return;

            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);
            var leftAnkle = rig.GetBone(BodyLandmark.LeftAnkle);
            var rightAnkle = rig.GetBone(BodyLandmark.RightAnkle);
            if (leftShoulder == null || rightShoulder == null || leftAnkle == null || rightAnkle == null) return;

            float shoulderY = view.WorldToViewportPoint((leftShoulder.position + rightShoulder.position) * 0.5f).y;
            float ankleY = view.WorldToViewportPoint((leftAnkle.position + rightAnkle.position) * 0.5f).y;
            float avatarProjected = Mathf.Abs(shoulderY - ankleY);
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

        /// <summary>Vertical distance from the shoulders down to the named joint on the avatar.</summary>
        private float AvatarSpan(BodyLandmark lower)
        {
            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);
            var lowerBone = rig.GetBone(lower);
            if (leftShoulder == null || rightShoulder == null || lowerBone == null) return 0f;

            float shoulderY = (leftShoulder.position.y + rightShoulder.position.y) * 0.5f;
            return Mathf.Abs(shoulderY - lowerBone.position.y);
        }

        private bool AreAnklesVisible(PoseFrame frame) =>
            frame.HasVisibleLowerBody(visibilityThreshold);

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
