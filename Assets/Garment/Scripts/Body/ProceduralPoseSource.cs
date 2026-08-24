using System.Collections.Generic;
using UnityEngine;

namespace Garment.Body
{
    /// <summary>
    /// Drives the rig from code so garment behaviour can be exercised without a camera,
    /// a tracking backend, or licensed animation clips.
    /// </summary>
    public sealed class ProceduralPoseSource : MonoBehaviour, IBodyPoseSource
    {
        [SerializeField] private BodyRig rig;
        [SerializeField] private DemoPose pose = DemoPose.Idle;
        [SerializeField, Range(0.1f, 3f)] private float speed = 1f;
        [SerializeField, Range(1f, 30f)] private float blendSharpness = 8f;

        private readonly Dictionary<BodyLandmark, Quaternion> bindRotations = new Dictionary<BodyLandmark, Quaternion>();
        private readonly Dictionary<BodyLandmark, Vector3> targetAngles = new Dictionary<BodyLandmark, Vector3>();
        private readonly Dictionary<BodyLandmark, Vector3> currentAngles = new Dictionary<BodyLandmark, Vector3>();

        private Vector3 bindRootPosition;
        private Quaternion bindRootRotation;
        private Vector3 currentRootOffset;
        private float currentTurn;
        private float phase;
        private bool captured;

        public bool IsPosing => pose != DemoPose.TPose;

        public DemoPose Pose
        {
            get => pose;
            set => pose = value;
        }

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Clamp(value, 0.1f, 3f);
        }

        private void Awake()
        {
            if (rig == null) rig = GetComponent<BodyRig>();

            // This source owns the pose; an Animator writing the same bones would fight it.
            var animator = GetComponent<Animator>();
            if (animator != null) animator.enabled = false;

            CaptureBindPose();
        }

        private void Update()
        {
            if (rig != null) ApplyTo(rig, Time.deltaTime);
        }

        public void ApplyTo(BodyRig target, float deltaTime)
        {
            if (target == null) return;
            if (!captured) CaptureBindPose();

            phase += deltaTime * speed;
            targetAngles.Clear();

            float rootLift = 0f;
            float turnDelta = 0f;

            switch (pose)
            {
                case DemoPose.TPose:
                    break;
                case DemoPose.Idle:
                    ApplyIdle();
                    break;
                case DemoPose.Walk:
                    rootLift = ApplyWalk();
                    break;
                case DemoPose.Turn:
                    ApplyIdle();
                    turnDelta = 45f * deltaTime * speed;
                    break;
                case DemoPose.Squat:
                    rootLift = ApplySquat();
                    break;
                case DemoPose.ArmsUp:
                    ApplyIdle();
                    ApplyArmsUp();
                    break;
            }

            float blend = 1f - Mathf.Exp(-blendSharpness * deltaTime);
            currentTurn += turnDelta;
            currentRootOffset = Vector3.Lerp(currentRootOffset, new Vector3(0f, rootLift, 0f), blend);

            var root = target.GetBone(BodyLandmark.Hips);
            if (root != null)
            {
                root.localPosition = bindRootPosition + currentRootOffset;
                root.localRotation = bindRootRotation * Quaternion.Euler(0f, currentTurn, 0f);
            }

            foreach (var landmark in bindRotations.Keys)
            {
                if (landmark == BodyLandmark.Hips) continue;

                targetAngles.TryGetValue(landmark, out var wanted);
                currentAngles.TryGetValue(landmark, out var current);
                var blended = Vector3.Lerp(current, wanted, blend);
                currentAngles[landmark] = blended;

                var bone = target.GetBone(landmark);
                if (bone != null) bone.localRotation = bindRotations[landmark] * Quaternion.Euler(blended);
            }
        }

        public void ResetToBindPose()
        {
            currentAngles.Clear();
            currentRootOffset = Vector3.zero;
            currentTurn = 0f;
            phase = 0f;
        }

        private void CaptureBindPose()
        {
            if (rig == null) return;

            bindRotations.Clear();
            foreach (BodyLandmark landmark in System.Enum.GetValues(typeof(BodyLandmark)))
            {
                var bone = rig.GetBone(landmark);
                if (bone != null) bindRotations[landmark] = bone.localRotation;
            }

            var hips = rig.GetBone(BodyLandmark.Hips);
            if (hips != null)
            {
                bindRootPosition = hips.localPosition;
                bindRootRotation = hips.localRotation;
            }
            captured = bindRotations.Count > 0;
        }

        private void ApplyIdle()
        {
            float breath = Mathf.Sin(phase * 1.6f);
            float sway = Mathf.Sin(phase * 0.9f);

            targetAngles[BodyLandmark.Spine] = new Vector3(breath * 1.2f, sway * 2f, 0f);
            targetAngles[BodyLandmark.Chest] = new Vector3(breath * 1.5f, sway * 1.5f, 0f);
            targetAngles[BodyLandmark.LeftShoulder] = new Vector3(0f, 0f, -70f + breath * 2f);
            targetAngles[BodyLandmark.RightShoulder] = new Vector3(0f, 0f, 70f - breath * 2f);
            targetAngles[BodyLandmark.LeftElbow] = new Vector3(0f, -12f, 0f);
            targetAngles[BodyLandmark.RightElbow] = new Vector3(0f, 12f, 0f);
        }

        private float ApplyWalk()
        {
            ApplyIdle();

            float stride = Mathf.Sin(phase * 3f);
            float opposite = -stride;

            targetAngles[BodyLandmark.LeftUpperLeg] = new Vector3(stride * 28f, 0f, 0f);
            targetAngles[BodyLandmark.RightUpperLeg] = new Vector3(opposite * 28f, 0f, 0f);
            targetAngles[BodyLandmark.LeftKnee] = new Vector3(Mathf.Max(0f, -stride) * 45f, 0f, 0f);
            targetAngles[BodyLandmark.RightKnee] = new Vector3(Mathf.Max(0f, -opposite) * 45f, 0f, 0f);
            targetAngles[BodyLandmark.LeftAnkle] = new Vector3(stride * 10f, 0f, 0f);
            targetAngles[BodyLandmark.RightAnkle] = new Vector3(opposite * 10f, 0f, 0f);

            targetAngles[BodyLandmark.LeftShoulder] = new Vector3(opposite * 18f, 0f, -72f);
            targetAngles[BodyLandmark.RightShoulder] = new Vector3(stride * 18f, 0f, 72f);

            return Mathf.Abs(Mathf.Cos(phase * 3f)) * -0.03f;
        }

        private float ApplySquat()
        {
            float depth = (Mathf.Sin(phase * 1.4f) + 1f) * 0.5f;

            targetAngles[BodyLandmark.LeftUpperLeg] = new Vector3(depth * 75f, 0f, 4f);
            targetAngles[BodyLandmark.RightUpperLeg] = new Vector3(depth * 75f, 0f, -4f);
            targetAngles[BodyLandmark.LeftKnee] = new Vector3(-depth * 95f, 0f, 0f);
            targetAngles[BodyLandmark.RightKnee] = new Vector3(-depth * 95f, 0f, 0f);
            targetAngles[BodyLandmark.LeftAnkle] = new Vector3(depth * 22f, 0f, 0f);
            targetAngles[BodyLandmark.RightAnkle] = new Vector3(depth * 22f, 0f, 0f);
            targetAngles[BodyLandmark.Spine] = new Vector3(-depth * 18f, 0f, 0f);
            targetAngles[BodyLandmark.LeftShoulder] = new Vector3(-depth * 60f, 0f, -75f);
            targetAngles[BodyLandmark.RightShoulder] = new Vector3(-depth * 60f, 0f, 75f);

            return -depth * 0.32f;
        }

        private void ApplyArmsUp()
        {
            targetAngles[BodyLandmark.LeftShoulder] = new Vector3(0f, 0f, 15f);
            targetAngles[BodyLandmark.RightShoulder] = new Vector3(0f, 0f, -15f);
            targetAngles[BodyLandmark.LeftElbow] = new Vector3(0f, 0f, 0f);
            targetAngles[BodyLandmark.RightElbow] = new Vector3(0f, 0f, 0f);
        }
    }
}
