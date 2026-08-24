using System.Collections.Generic;
using UnityEngine;

namespace Garment.Body
{
    /// <summary>Bones resolved from a Unity Humanoid avatar (Mixamo, Ready Player Me, ...).</summary>
    [RequireComponent(typeof(Animator))]
    public sealed class HumanoidBodyRig : BodyRig
    {
        [SerializeField] private SkinnedMeshRenderer bodyMesh;

        private static readonly Dictionary<BodyLandmark, HumanBodyBones> BoneOf =
            new Dictionary<BodyLandmark, HumanBodyBones>
            {
                { BodyLandmark.Hips, HumanBodyBones.Hips },
                { BodyLandmark.Spine, HumanBodyBones.Spine },
                { BodyLandmark.Chest, HumanBodyBones.Chest },
                { BodyLandmark.Neck, HumanBodyBones.Neck },
                { BodyLandmark.Head, HumanBodyBones.Head },
                { BodyLandmark.LeftShoulder, HumanBodyBones.LeftUpperArm },
                { BodyLandmark.RightShoulder, HumanBodyBones.RightUpperArm },
                { BodyLandmark.LeftElbow, HumanBodyBones.LeftLowerArm },
                { BodyLandmark.RightElbow, HumanBodyBones.RightLowerArm },
                { BodyLandmark.LeftWrist, HumanBodyBones.LeftHand },
                { BodyLandmark.RightWrist, HumanBodyBones.RightHand },
                { BodyLandmark.LeftUpperLeg, HumanBodyBones.LeftUpperLeg },
                { BodyLandmark.RightUpperLeg, HumanBodyBones.RightUpperLeg },
                { BodyLandmark.LeftKnee, HumanBodyBones.LeftLowerLeg },
                { BodyLandmark.RightKnee, HumanBodyBones.RightLowerLeg },
                { BodyLandmark.LeftAnkle, HumanBodyBones.LeftFoot },
                { BodyLandmark.RightAnkle, HumanBodyBones.RightFoot }
            };

        private Animator animator;

        private Animator Animator
        {
            get
            {
                if (animator == null) animator = GetComponent<Animator>();
                return animator;
            }
        }

        public override SkinnedMeshRenderer BodyMesh
        {
            get
            {
                if (bodyMesh == null) bodyMesh = GetComponentInChildren<SkinnedMeshRenderer>();
                return bodyMesh;
            }
        }

        public override Transform GetBone(BodyLandmark landmark)
        {
            if (!Animator.isHuman)
            {
                Debug.LogError($"{name}: Animator has no Humanoid avatar; cannot resolve landmarks.", this);
                return null;
            }
            return BoneOf.TryGetValue(landmark, out var bone) ? Animator.GetBoneTransform(bone) : null;
        }
    }
}
