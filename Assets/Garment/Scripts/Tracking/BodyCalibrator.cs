using System;
using Garment.Body;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Reshapes the rig to the proportions of the person in front of the camera: limb lengths
    /// are measured over a few seconds and the bones are moved to match. Garments must be
    /// re-bound afterwards — their bind poses belong to the skeleton as it was before.
    /// </summary>
    public sealed class BodyCalibrator : MonoBehaviour
    {
        [SerializeField] private BodyRig rig;
        [SerializeField] private WebcamPoseProvider provider;
        [Tooltip("How long to average measurements before applying them.")]
        [SerializeField, Range(0.5f, 5f)] private float sampleSeconds = 2f;
        [Tooltip("Landmarks below this visibility make the sample unusable.")]
        [SerializeField, Range(0f, 1f)] private float visibilityThreshold = 0.6f;
        [Tooltip("Limits how far a bone may be stretched, guarding against a bad measurement.")]
        [SerializeField] private Vector2 scaleLimits = new Vector2(0.6f, 1.6f);
        [Tooltip("Measure automatically once a full body has been stably visible this long.")]
        [SerializeField, Range(0f, 5f)] private float autoCalibrateAfter = 1f;

        private BodyMeasurements accumulated;
        private float elapsed;
        private int samples;
        private float stableVisibleTime;
        private BodyCoverage samplingScope;
        private float armStretchSum;
        private int armStretchSamples;
        private float armRadiusSum;
        private int armRadiusSamples;

        // The avatar's own silhouette-to-bone ratio at the hips, taken in bind pose. The person's
        // ratio divided by this is how much broader they are than the avatar's cross-section.
        private float avatarHipGirthRatio;

        /// <summary>Raised once the rig has been reshaped, so garments can be re-fitted.</summary>
        public event Action Calibrated;

        public bool IsSampling { get; private set; }

        public bool HasCalibrated => Scope != BodyCoverage.None;

        /// <summary>
        /// How much of the body the standing calibration was measured from. An upper-body
        /// calibration is real but partial: it sizes the avatar from the torso, which is all a
        /// waist-up view needs, and is replaced the moment the whole body is seen.
        /// </summary>
        public BodyCoverage Scope { get; private set; } = BodyCoverage.None;

        public BodyMeasurements LastMeasurements => accumulated;

        public string Status { get; private set; } = "Not calibrated";

        private void Awake()
        {
            if (rig == null) rig = GetComponent<BodyRig>();
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();
            avatarHipGirthRatio = MeasureAvatarHipGirthRatio();
        }

        /// <summary>
        /// Bind-pose silhouette width of the body mesh at hip height, relative to the bone width
        /// there. Must be taken before any tracking poses the rig.
        /// </summary>
        private float MeasureAvatarHipGirthRatio()
        {
            var bodyMesh = rig != null ? rig.BodyMesh : null;
            var leftHip = rig != null ? rig.GetBone(BodyLandmark.LeftUpperLeg) : null;
            var rightHip = rig != null ? rig.GetBone(BodyLandmark.RightUpperLeg) : null;
            if (bodyMesh == null || bodyMesh.sharedMesh == null || leftHip == null || rightHip == null) return 0f;

            float boneWidth = Vector3.Distance(leftHip.position, rightHip.position);
            if (boneWidth <= 1e-4f) return 0f;

            float hipY = rig.transform.InverseTransformPoint((leftHip.position + rightHip.position) * 0.5f).y;

            // The widest part of the pelvis sits below the hip joints; a thin slice at joint
            // height measures the waist instead and badly understates how broad the avatar is.
            float widest = 0f;
            foreach (var vertex in bodyMesh.sharedMesh.vertices)
            {
                if (vertex.y < hipY - 0.10f || vertex.y > hipY + 0.03f) continue;
                widest = Mathf.Max(widest, Mathf.Abs(vertex.x) * 2f);
            }
            if (widest <= 0f) return 0f;
            return widest / boneWidth;
        }

        /// <summary>Forget the current person: the rig returns to its own proportions and the
        /// next stably visible body is measured from scratch.</summary>
        public void ResetCalibration()
        {
            IsSampling = false;
            Scope = BodyCoverage.None;
            samplingScope = BodyCoverage.None;
            stableVisibleTime = 0f;
            if (rig != null)
            {
                rig.transform.localScale = Vector3.one;
                rig.GirthScale = 1f;
                rig.ArmStretch = 1f;
                rig.ArmRadius = 0f;
            }
            Status = "Not calibrated";
        }

        /// <summary>Begin measuring. The subject should stand facing the camera, arms out.</summary>
        public void BeginCalibration()
        {
            samples = 0;
            elapsed = 0f;
            armStretchSum = 0f;
            armStretchSamples = 0;
            armRadiusSum = 0f;
            armRadiusSamples = 0;
            IsSampling = true;
            samplingScope = provider != null && provider.Coverage != BodyCoverage.None
                ? provider.Coverage
                : BodyCoverage.FullBody;
            Status = samplingScope == BodyCoverage.FullBody
                ? "Stand in a T-pose, whole body in frame..."
                : "Measuring upper body...";
        }

        private void Update()
        {
            if (provider == null || !provider.HasPose) return;

            // Nobody presses a calibrate button in a fitting room mirror: measure whatever is
            // steadily in view. The coverage state is what separates real
            // legs from the ones the model invents on a waist-up person, so an upper-body
            // calibration simply leaves the legs out rather than believing them.
            if (!IsSampling && autoCalibrateAfter > 0f && CanImproveOn(provider.Coverage))
            {
                var current = provider.LatestFrame;
                if (current.IsValid && IsUsable(current, provider.Coverage))
                {
                    stableVisibleTime += Time.deltaTime;
                    if (stableVisibleTime >= autoCalibrateAfter) BeginCalibration();
                }
                else stableVisibleTime = 0f;
            }

            if (!IsSampling) return;

            var frame = provider.LatestFrame;
            if (!frame.IsValid || !IsUsable(frame, provider.Coverage))
            {
                Status = "Stand facing the camera, shoulders and hips in view";
                return;
            }

            // A run that started on a full body must not silently finish on an upper-body one.
            if (provider.Coverage != samplingScope)
            {
                BeginCalibration();
                return;
            }

            var measured = BodyMeasurements.FromFrame(frame, samplingScope);
            measured = measured.WithGirth(MeasurePersonHipGirthRatio(frame));

            float armRatio = MeasureScreenArmRatio(frame);
            if (armRatio > 0f) { armStretchSum += armRatio; armStretchSamples++; }

            float armRadius = MeasureArmRadiusMetres(frame);
            if (armRadius > 0f) { armRadiusSum += armRadius; armRadiusSamples++; }
            if (!measured.IsPlausible)
            {
                Status = "Measurements out of range, hold still";
                return;
            }

            accumulated = samples == 0 ? measured : accumulated.Blend(measured, 1f / (samples + 1));
            samples++;

            elapsed += Time.deltaTime;
            Status = $"Measuring... {Mathf.RoundToInt(elapsed / sampleSeconds * 100f)}%";

            if (elapsed >= sampleSeconds) Apply();
        }

        private void Apply()
        {
            IsSampling = false;

            if (samples == 0 || rig == null)
            {
                Status = "Calibration failed: no usable samples";
                return;
            }

            ApplyMeasurements(accumulated);
            Status = $"Calibrated ({Scope}) from {samples} samples: {accumulated}";
            Debug.Log($"BodyCalibrator: {Status}", this);
        }

        /// <summary>Reshape the rig to given measurements, bypassing the sampling loop.</summary>
        /// <remarks>
        /// Bones are never moved individually: garments and the body mesh are skinned against
        /// bindposes recorded for the original skeleton, and any bone that shifts drags its
        /// share of the cloth with it — sleeves crumple to half their length. The only safe
        /// adjustments are a uniform scale on the root, which sits outside the skinning chain,
        /// and the garment-level girth.
        /// </remarks>
        public void ApplyMeasurements(BodyMeasurements measurements)
        {
            if (rig == null) return;
            accumulated = measurements;

            ApplyHeight(measurements);

            // Girth and the two arm figures are read off the silhouette at the hips and off the
            // avatar's own projected arms. Waist-up, the hips sit half out of the picture and
            // the avatar is still being placed, so both measurements run to their limits and
            // stay there — measured 3.20 girth and a sleeve stretch pegged at 1.8, which blew
            // the jacket up to 1.77 m across. A partial calibration sizes the body and leaves
            // these to a pass that can actually see what it is measuring.
            if (measurements.Coverage != BodyCoverage.FullBody)
            {
                Scope = BodyCoverage.UpperBody;
                Calibrated?.Invoke();
                return;
            }

            ApplyGirth(measurements.HipGirthRatio);
            if (armStretchSamples > 0)
                rig.ArmStretch = Mathf.Clamp(armStretchSum / armStretchSamples, 0.8f, 1.8f);
            if (armRadiusSamples > 0)
                rig.ArmRadius = Mathf.Clamp(armRadiusSum / armRadiusSamples, 0f, 0.09f);

            Scope = BodyCoverage.FullBody;
            Calibrated?.Invoke();
        }

        /// <summary>
        /// Sizes the rig against the person. With the whole body in view that is the
        /// shoulder-to-ankle run — the longest well-tracked line there is, so the least noisy.
        /// Waist-up there are no legs to measure, and the torso and shoulder width carry it
        /// instead: two independent estimates of one number, averaged.
        /// </summary>
        private void ApplyHeight(BodyMeasurements measurements)
        {
            float scale = measurements.Coverage == BodyCoverage.FullBody
                ? FullBodyScale(measurements)
                : UpperBodyScale(measurements);
            if (scale <= 0f) return;

            rig.transform.localScale = Vector3.one * Mathf.Clamp(scale, scaleLimits.x, scaleLimits.y);
        }

        private float FullBodyScale(BodyMeasurements measurements)
        {
            float personSpan = measurements.TorsoLength + measurements.UpperLeg + measurements.LowerLeg;

            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);
            var leftAnkle = rig.GetBone(BodyLandmark.LeftAnkle);
            if (leftShoulder == null || rightShoulder == null || leftAnkle == null || personSpan <= 0f) return 0f;

            float avatarSpan = Unscaled(Mathf.Abs(
                (leftShoulder.position.y + rightShoulder.position.y) * 0.5f - leftAnkle.position.y));
            return avatarSpan > 1e-3f ? personSpan / avatarSpan : 0f;
        }

        private float UpperBodyScale(BodyMeasurements measurements)
        {
            float torsoScale = 0f;
            var hips = rig.GetBone(BodyLandmark.Hips);
            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);
            if (hips != null && leftShoulder != null && rightShoulder != null && measurements.TorsoLength > 0f)
            {
                float avatarTorso = Unscaled(Mathf.Abs(
                    (leftShoulder.position.y + rightShoulder.position.y) * 0.5f - hips.position.y));
                if (avatarTorso > 1e-3f) torsoScale = measurements.TorsoLength / avatarTorso;
            }

            float shoulderScale = 0f;
            float avatarShoulders = Unscaled(rig.ShoulderWidth);
            if (avatarShoulders > 1e-3f && measurements.ShoulderWidth > 0f)
                shoulderScale = measurements.ShoulderWidth / avatarShoulders;

            // The torso decides, and shoulder width only stands in when the torso cannot be
            // measured. Averaging the two sounds fairer and measures worse: a person's
            // shoulder-to-torso proportion is not the avatar's (measured 0.71 against 0.95 on
            // the mannequin), so the two estimates disagree by a third and their mean satisfies
            // neither. The torso wins the tie because PoseAlignment closes its depth loop on the
            // torso span — calibrating against a different quantity makes the two fight.
            return torsoScale > 0f ? torsoScale : shoulderScale;
        }

        /// <summary>A rig measurement with the scale already on it taken back off.</summary>
        private float Unscaled(float measured) => measured / Mathf.Max(rig.transform.localScale.y, 1e-4f);

        /// <summary>Person's silhouette width at the hips over their bone width there, from the mask.</summary>
        private float MeasurePersonHipGirthRatio(PoseFrame frame)
        {
            var hipCentre = frame.Midpoint2D(PoseLandmark.LeftHip, PoseLandmark.RightHip);
            float boneWidthUv = Mathf.Abs(
                frame.ScreenOf(PoseLandmark.LeftHip).x - frame.ScreenOf(PoseLandmark.RightHip).x);
            if (boneWidthUv < 1e-3f) return 0f;

            if (!provider.TryMeasureSilhouetteWidth(hipCentre, out float silhouetteUv)) return 0f;
            return silhouetteUv / boneWidthUv;
        }

        /// <summary>
        /// The person's upper-arm length on screen versus the avatar's, both measured in the
        /// camera frame's space. Screen space is what a mirror overlay has to match — the
        /// model's metric arm lengths disagree with what the photo actually shows.
        /// </summary>
        private float MeasureScreenArmRatio(PoseFrame frame)
        {
            var view = Camera.main;
            if (view == null || provider.Feed == null) return 0f;

            float personLen = (ScreenToFrame(frame.ScreenOf(PoseLandmark.LeftShoulder))
                             - ScreenToFrame(frame.ScreenOf(PoseLandmark.LeftElbow))).magnitude
                            + (ScreenToFrame(frame.ScreenOf(PoseLandmark.RightShoulder))
                             - ScreenToFrame(frame.ScreenOf(PoseLandmark.RightElbow))).magnitude;

            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var leftElbow = rig.GetBone(BodyLandmark.LeftElbow);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);
            var rightElbow = rig.GetBone(BodyLandmark.RightElbow);
            if (leftShoulder == null || leftElbow == null || rightShoulder == null || rightElbow == null) return 0f;

            float avatarLen = (ViewportToFrame(view.WorldToViewportPoint(leftShoulder.position))
                             - ViewportToFrame(view.WorldToViewportPoint(leftElbow.position))).magnitude
                            + (ViewportToFrame(view.WorldToViewportPoint(rightShoulder.position))
                             - ViewportToFrame(view.WorldToViewportPoint(rightElbow.position))).magnitude;

            if (avatarLen < 1e-3f || personLen < 1e-3f) return 0f;
            return personLen / avatarLen;
        }

        /// <summary>Frame UV with x weighted by aspect, so lengths compare across axes.</summary>
        private Vector2 ScreenToFrame(Vector2 frameUv)
        {
            float aspect = provider.Feed.AspectRatio;
            return new Vector2(frameUv.x * aspect, frameUv.y);
        }

        private Vector2 ViewportToFrame(Vector3 viewport)
        {
            var view = Camera.main;
            float frameAspect = provider.Feed.AspectRatio;
            float scale = frameAspect / view.aspect;

            // Undo the letterbox the feed is displayed with, then weight x by aspect.
            float frameX = scale <= 1f ? 0.5f + (viewport.x - 0.5f) / scale : viewport.x;
            float frameY = scale <= 1f ? viewport.y : 0.5f + (viewport.y - 0.5f) * scale;
            return new Vector2(frameX * frameAspect, frameY);
        }

        /// <summary>
        /// Half-thickness of the person's upper arm in metres: silhouette thickness at the
        /// middle of the upper arm, converted through the shoulder width — a length whose size
        /// is known both on screen and on the rig.
        /// </summary>
        private float MeasureArmRadiusMetres(PoseFrame frame)
        {
            var midUpperArm = frame.Midpoint2D(PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow);
            if (!provider.TryMeasureSilhouetteHeight(midUpperArm, out float thicknessUv)) return 0f;

            float shoulderUv = (ScreenToFrame(frame.ScreenOf(PoseLandmark.LeftShoulder))
                              - ScreenToFrame(frame.ScreenOf(PoseLandmark.RightShoulder))).magnitude;
            if (shoulderUv < 1e-3f) return 0f;

            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);
            if (leftShoulder == null || rightShoulder == null) return 0f;
            float shoulderMetres = Vector3.Distance(leftShoulder.position, rightShoulder.position);

            return thicknessUv * 0.5f * (shoulderMetres / shoulderUv);
        }

        private void ApplyGirth(float personRatio)
        {
            if (personRatio <= 0f || avatarHipGirthRatio <= 0f) return;

            // The person's silhouette includes whatever they are wearing, so it overstates the
            // body — clamp tightly rather than trusting it fully.
            rig.GirthScale = Mathf.Clamp(personRatio / avatarHipGirthRatio, 1f, 1.3f);
        }

        /// <summary>
        /// Whether a frame can be measured at all. The torso is the floor: without shoulders and
        /// hips there is nothing to size the avatar against, whatever else is in view.
        /// </summary>
        private bool IsUsable(PoseFrame frame, BodyCoverage coverage)
        {
            if (coverage == BodyCoverage.None) return false;

            var required = new[]
            {
                PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder,
                PoseLandmark.LeftHip, PoseLandmark.RightHip
            };
            foreach (var landmark in required)
                if (frame.VisibilityOf(landmark) < visibilityThreshold) return false;

            return coverage != BodyCoverage.FullBody || frame.HasVisibleLowerBody(visibilityThreshold);
        }

        /// <summary>
        /// Whether measuring now would tell us anything we do not already know. An upper-body
        /// calibration is worth taking when there is none; a full-body one always supersedes it.
        /// Re-measuring the same scope over and over would only rebind every garment for nothing.
        /// </summary>
        private bool CanImproveOn(BodyCoverage coverage)
        {
            if (coverage == BodyCoverage.None) return false;
            if (Scope == BodyCoverage.None) return true;
            return coverage == BodyCoverage.FullBody && Scope != BodyCoverage.FullBody;
        }
    }
}
