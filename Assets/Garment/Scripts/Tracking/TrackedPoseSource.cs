using System.Collections.Generic;
using Garment.Body;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Drives the rig from tracked landmarks. Each bone is aimed at the next joint the model
    /// reports, so only directions matter — the tracker's absolute scale and position are ignored,
    /// which keeps the avatar steady even when the person moves towards or away from the camera.
    /// </summary>
    public sealed class TrackedPoseSource : MonoBehaviour, IBodyPoseSource
    {
        [SerializeField] private BodyRig rig;
        [SerializeField] private WebcamPoseProvider provider;

        [Tooltip("MediaPipe's depth axis points at the camera; Unity's points away from it.")]
        [SerializeField] private bool flipDepth = true;
        [Tooltip("How much of the model's depth estimate to trust. It is the least reliable axis " +
                 "from a single camera — at 1 a standing person comes out leaning badly.")]
        [SerializeField, Range(0f, 1f)] private float depthInfluence = 0.25f;
        [Tooltip("At or above this visibility a landmark is followed outright.")]
        [SerializeField, Range(0f, 1f)] private float visibilityThreshold = 0.5f;
        [Tooltip("Below this visibility a landmark is ignored. Between the two the limb eases " +
                 "in, so a point hovering around the threshold cannot snap the limb about.")]
        [SerializeField, Range(0f, 1f)] private float visibilityFloor = 0.2f;
        [Tooltip("Movement slower than this (Hz) is treated as sensor jitter and smoothed away.")]
        [SerializeField, Range(0.1f, 10f)] private float jitterCutoff = 1.5f;
        [Tooltip("How much a moving landmark relaxes the smoothing. Higher follows faster.")]
        [SerializeField, Range(0f, 50f)] private float speedResponse = 10f;
        [Tooltip("Turn the body around. Which way the torso ends up facing depends on the mirror " +
                 "and on the rig's own bind orientation, so it is settled by measurement.")]
        [SerializeField] private bool faceCamera;

        private readonly Dictionary<BodyLandmark, Vector3> bindDirections = new Dictionary<BodyLandmark, Vector3>();
        private readonly Dictionary<BodyLandmark, Quaternion> bindLocalRotations = new Dictionary<BodyLandmark, Quaternion>();
        private readonly Vector3[] filtered = new Vector3[PoseFrame.LandmarkCount];
        private OneEuroFilterVector3[] filters;

        private Quaternion bindRootRotation;
        private bool captured;

        public bool IsPosing => provider != null && provider.HasPose;

        /// <summary>Chains of (bone, joint the bone points at, joint the bone starts from).</summary>
        private static readonly (BodyLandmark bone, PoseLandmark from, PoseLandmark to)[] Aims =
        {
            (BodyLandmark.LeftUpperLeg, PoseLandmark.LeftHip, PoseLandmark.LeftKnee),
            (BodyLandmark.LeftKnee, PoseLandmark.LeftKnee, PoseLandmark.LeftAnkle),
            (BodyLandmark.RightUpperLeg, PoseLandmark.RightHip, PoseLandmark.RightKnee),
            (BodyLandmark.RightKnee, PoseLandmark.RightKnee, PoseLandmark.RightAnkle),
            (BodyLandmark.LeftShoulder, PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow),
            (BodyLandmark.LeftElbow, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist),
            (BodyLandmark.RightShoulder, PoseLandmark.RightShoulder, PoseLandmark.RightElbow),
            (BodyLandmark.RightElbow, PoseLandmark.RightElbow, PoseLandmark.RightWrist)
        };

        private void Awake()
        {
            if (rig == null) rig = GetComponent<BodyRig>();
            if (provider == null) provider = FindFirstObjectByType<WebcamPoseProvider>();

            var animator = GetComponent<Animator>();
            if (animator != null) animator.enabled = false;

            CaptureBindPose();
        }

        private void LateUpdate()
        {
            if (rig != null) ApplyTo(rig, Time.deltaTime);
        }

        public void ApplyTo(BodyRig target, float deltaTime)
        {
            if (target == null || provider == null || !provider.HasPose) return;
            ApplyFrame(target, provider.LatestFrame, deltaTime, provider.Coverage);
        }

        /// <summary>
        /// Pose the rig from a frame supplied directly, bypassing the live tracker. A lone frame
        /// has no history to debounce, so its own visibility decides the coverage.
        /// </summary>
        public void ApplyFrame(BodyRig target, PoseFrame frame, float deltaTime)
        {
            if (!frame.IsValid) return;
            ApplyFrame(target, frame, deltaTime,
                frame.HasVisibleLowerBody(visibilityThreshold) ? BodyCoverage.FullBody : BodyCoverage.UpperBody);
        }

        public void ApplyFrame(BodyRig target, PoseFrame frame, float deltaTime, BodyCoverage coverage)
        {
            if (target == null || !frame.IsValid) return;
            if (!captured) CaptureBindPose();

            Filter(frame, deltaTime);

            var hipCentre = Midpoint(PoseLandmark.LeftHip, PoseLandmark.RightHip);
            var shoulderCentre = Midpoint(PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder);

            AimRoot(target, hipCentre, shoulderCentre);
            AimBone(target, BodyLandmark.Spine, hipCentre, shoulderCentre, 1f);

            bool lowerBodyVisible = coverage == BodyCoverage.FullBody;
            for (int i = 0; i < Aims.Length; i++)
            {
                if (!lowerBodyVisible && i < 4) continue;
                var (bone, from, to) = Aims[i];
                AimBone(target, bone, Position(from), Position(to),
                    Mathf.Min(frame.VisibilityOf(from), frame.VisibilityOf(to)));
            }
        }

        /// <summary>Turns the pelvis to face the way the tracked shoulders and hips do.</summary>
        private void AimRoot(BodyRig target, Vector3 hipCentre, Vector3 shoulderCentre)
        {
            var hips = target.GetBone(BodyLandmark.Hips);
            if (hips == null) return;

            var right = Position(PoseLandmark.LeftHip) - Position(PoseLandmark.RightHip);
            var up = shoulderCentre - hipCentre;
            if (right.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f) return;

            var forward = Vector3.Cross(right.normalized, up.normalized);
            if (forward.sqrMagnitude < 1e-6f) return;
            if (faceCamera) forward = -forward;

            hips.rotation = Quaternion.LookRotation(forward.normalized, up.normalized) * bindRootRotation;
        }

        /// <summary>
        /// Turns a bone to point along a tracked segment, starting from its REST orientation
        /// rather than from wherever it is now.
        ///
        /// A direction pins down only two of the bone's three degrees of freedom; the roll about
        /// its own axis is left over. Turning the bone by the short way round from its current
        /// rotation therefore keeps whatever roll it had, and every frame adds a little more —
        /// worst when the target jumps, as it does when the photo changes. The sleeve is skinned
        /// to that bone and visibly winds around the arm. Aiming from rest makes the result a
        /// function of the tracked pose alone, so nothing can accumulate.
        ///
        /// Rest means rest *this frame*: the chain is aimed parents first, so a bone's rest
        /// orientation is its parent's current rotation carrying the bone's bind-pose local one.
        /// </summary>
        private void AimBone(BodyRig target, BodyLandmark landmark, Vector3 from, Vector3 to, float confidence)
        {
            var bone = target.GetBone(landmark);
            if (bone == null ||
                !bindDirections.TryGetValue(landmark, out var bindDirection) ||
                !bindLocalRotations.TryGetValue(landmark, out var bindLocalRotation)) return;

            // Confidence is a continuous quantity, so the response to it has to be continuous
            // too. A hard cutoff makes a landmark hovering around the threshold flip the whole
            // limb between tracked and rest several times a second — a raised arm snapping back
            // to a horizontal T-pose and out again, dragging its sleeve across the chest.
            float trust = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(visibilityFloor, visibilityThreshold, confidence));
            if (trust <= 0f) return;

            var wanted = to - from;
            if (wanted.sqrMagnitude < 1e-6f) return;

            var restRotation = bone.parent != null
                ? bone.parent.rotation * bindLocalRotation
                : bindLocalRotation;

            var aimed = Quaternion.FromToRotation(restRotation * bindDirection, wanted.normalized) * restRotation;
            bone.rotation = trust >= 1f ? aimed : Quaternion.Slerp(restRotation, aimed, trust);
        }

        /// <summary>Landmarks jitter frame to frame; the avatar must not.</summary>
        private void Filter(PoseFrame frame, float deltaTime)
        {
            if (filters == null)
            {
                filters = new OneEuroFilterVector3[PoseFrame.LandmarkCount];
                for (int i = 0; i < filters.Length; i++)
                    filters[i] = new OneEuroFilterVector3(jitterCutoff, speedResponse);
            }

            for (int i = 0; i < filtered.Length; i++)
                filtered[i] = filters[i].Filter(Convert(frame.World[i]), deltaTime);
        }

        private Vector3 Convert(Vector3 modelSpace)
        {
            float depth = modelSpace.z * depthInfluence;
            return new Vector3(modelSpace.x, modelSpace.y, flipDepth ? -depth : depth);
        }

        private Vector3 Position(PoseLandmark landmark) => filtered[(int)landmark];

        private Vector3 Midpoint(PoseLandmark a, PoseLandmark b) => (Position(a) + Position(b)) * 0.5f;

        /// <summary>
        /// Records where each bone points while the rig is in its rest pose. Aiming later is
        /// then just the rotation that takes the rest direction onto the tracked one.
        /// </summary>
        private void CaptureBindPose()
        {
            if (rig == null) return;
            bindDirections.Clear();
            bindLocalRotations.Clear();

            // Measured with the skeleton put back into bind pose: a scene saved while posed
            // would otherwise be recorded as rest, and every bone would aim from a lie.
            rig.WhileInBindPose(RecordBindPose);
            captured = bindDirections.Count > 0;
        }

        private void RecordBindPose()
        {
            Record(BodyLandmark.Spine, BodyLandmark.Chest);
            Record(BodyLandmark.LeftUpperLeg, BodyLandmark.LeftKnee);
            Record(BodyLandmark.LeftKnee, BodyLandmark.LeftAnkle);
            Record(BodyLandmark.RightUpperLeg, BodyLandmark.RightKnee);
            Record(BodyLandmark.RightKnee, BodyLandmark.RightAnkle);
            Record(BodyLandmark.LeftShoulder, BodyLandmark.LeftElbow);
            Record(BodyLandmark.LeftElbow, BodyLandmark.LeftWrist);
            Record(BodyLandmark.RightShoulder, BodyLandmark.RightElbow);
            Record(BodyLandmark.RightElbow, BodyLandmark.RightWrist);

            var hips = rig.GetBone(BodyLandmark.Hips);
            if (hips != null) bindRootRotation = hips.rotation;
        }

        private void Record(BodyLandmark bone, BodyLandmark child)
        {
            var boneTransform = rig.GetBone(bone);
            var childTransform = rig.GetBone(child);
            if (boneTransform == null || childTransform == null) return;

            var direction = childTransform.position - boneTransform.position;
            if (direction.sqrMagnitude < 1e-8f) return;

            bindDirections[bone] = boneTransform.InverseTransformDirection(direction.normalized);
            bindLocalRotations[bone] = boneTransform.localRotation;
        }
    }
}
