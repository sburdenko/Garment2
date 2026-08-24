using System.Collections.Generic;
using UnityEngine;

namespace Garment.EditorTools.Mannequin
{
    public readonly struct BoneSpec
    {
        public readonly string Name;
        public readonly string Parent;
        public readonly HumanBodyBones Human;
        public readonly Vector3 Position;

        public BoneSpec(string name, string parent, HumanBodyBones human, Vector3 position)
        {
            Name = name;
            Parent = parent;
            Human = human;
            Position = position;
        }

        public bool IsHumanBone => Human != HumanBodyBones.LastBone;
    }

    public readonly struct LimbSpec
    {
        public readonly string From;
        public readonly string To;
        public readonly float RadiusFrom;
        public readonly float RadiusTo;
        public readonly float DepthScale;

        public LimbSpec(string from, string to, float radiusFrom, float radiusTo, float depthScale = 1f)
        {
            From = from;
            To = to;
            RadiusFrom = radiusFrom;
            RadiusTo = radiusTo;
            DepthScale = depthScale;
        }
    }

    /// <summary>
    /// A 1.77 m reference body in T-pose, bind-pose positions in root-local space.
    /// Joint heights are measured from the FV2_Yuna CLO avatar the project's garments were
    /// sewn on, so the skeleton sits inside that body and weights transfer to it cleanly.
    /// </summary>
    public static class MannequinProportions
    {
        public const string RootBone = "Root";

        public static readonly BoneSpec[] Bones =
        {
            new BoneSpec(RootBone,        null,            HumanBodyBones.LastBone,      new Vector3(0f,     0f,    0f)),
            new BoneSpec("Hips",          RootBone,        HumanBodyBones.Hips,          new Vector3(0f,     0.900f, 0f)),
            new BoneSpec("Spine",         "Hips",          HumanBodyBones.Spine,         new Vector3(0f,     1.020f, 0f)),
            new BoneSpec("Chest",         "Spine",         HumanBodyBones.Chest,         new Vector3(0f,     1.200f, 0f)),
            new BoneSpec("Neck",          "Chest",         HumanBodyBones.Neck,          new Vector3(0f,     1.500f, 0f)),
            new BoneSpec("Head",          "Neck",          HumanBodyBones.Head,          new Vector3(0f,     1.570f, 0f)),
            new BoneSpec("HeadTop",       "Head",          HumanBodyBones.LastBone,      new Vector3(0f,     1.760f, 0f)),

            new BoneSpec("LeftShoulder",  "Chest",         HumanBodyBones.LeftShoulder,  new Vector3( 0.055f, 1.394f, 0f)),
            new BoneSpec("LeftUpperArm",  "LeftShoulder",  HumanBodyBones.LeftUpperArm,  new Vector3( 0.170f, 1.394f, 0f)),
            new BoneSpec("LeftLowerArm",  "LeftUpperArm",  HumanBodyBones.LeftLowerArm,  new Vector3( 0.430f, 1.394f, 0f)),
            new BoneSpec("LeftHand",      "LeftLowerArm",  HumanBodyBones.LeftHand,      new Vector3( 0.660f, 1.394f, 0f)),
            new BoneSpec("LeftHandEnd",   "LeftHand",      HumanBodyBones.LastBone,      new Vector3( 0.850f, 1.394f, 0f)),

            new BoneSpec("RightShoulder", "Chest",         HumanBodyBones.RightShoulder, new Vector3(-0.055f, 1.394f, 0f)),
            new BoneSpec("RightUpperArm", "RightShoulder", HumanBodyBones.RightUpperArm, new Vector3(-0.170f, 1.394f, 0f)),
            new BoneSpec("RightLowerArm", "RightUpperArm", HumanBodyBones.RightLowerArm, new Vector3(-0.430f, 1.394f, 0f)),
            new BoneSpec("RightHand",     "RightLowerArm", HumanBodyBones.RightHand,     new Vector3(-0.660f, 1.394f, 0f)),
            new BoneSpec("RightHandEnd",  "RightHand",     HumanBodyBones.LastBone,      new Vector3(-0.850f, 1.394f, 0f)),

            new BoneSpec("LeftUpperLeg",  "Hips",          HumanBodyBones.LeftUpperLeg,  new Vector3( 0.095f, 0.850f, 0f)),
            new BoneSpec("LeftLowerLeg",  "LeftUpperLeg",  HumanBodyBones.LeftLowerLeg,  new Vector3( 0.095f, 0.480f, 0f)),
            new BoneSpec("LeftFoot",      "LeftLowerLeg",  HumanBodyBones.LeftFoot,      new Vector3( 0.095f, 0.085f, 0f)),
            new BoneSpec("LeftToes",      "LeftFoot",      HumanBodyBones.LeftToes,      new Vector3( 0.095f, 0.025f, 0.120f)),

            new BoneSpec("RightUpperLeg", "Hips",          HumanBodyBones.RightUpperLeg, new Vector3(-0.095f, 0.850f, 0f)),
            new BoneSpec("RightLowerLeg", "RightUpperLeg", HumanBodyBones.RightLowerLeg, new Vector3(-0.095f, 0.480f, 0f)),
            new BoneSpec("RightFoot",     "RightLowerLeg", HumanBodyBones.RightFoot,     new Vector3(-0.095f, 0.085f, 0f)),
            new BoneSpec("RightToes",     "RightFoot",     HumanBodyBones.RightToes,     new Vector3(-0.095f, 0.025f, 0.120f))
        };

        public static readonly LimbSpec[] Limbs =
        {
            new LimbSpec("Hips",  "Spine", 0.150f, 0.122f, 0.85f),
            new LimbSpec("Spine", "Chest", 0.122f, 0.138f, 0.78f),
            new LimbSpec("Chest", "Neck",  0.138f, 0.052f, 0.85f),
            new LimbSpec("Neck",  "Head",  0.050f, 0.070f),
            new LimbSpec("Head",  "HeadTop", 0.085f, 0.038f, 1.25f),

            new LimbSpec("Hips", "LeftUpperLeg",  0.142f, 0.088f, 0.85f),
            new LimbSpec("Hips", "RightUpperLeg", 0.142f, 0.088f, 0.85f),

            new LimbSpec("LeftShoulder", "LeftUpperArm", 0.068f, 0.050f),
            new LimbSpec("LeftUpperArm", "LeftLowerArm", 0.050f, 0.038f),
            new LimbSpec("LeftLowerArm", "LeftHand",     0.038f, 0.030f),
            new LimbSpec("LeftHand",     "LeftHandEnd",  0.030f, 0.016f),

            new LimbSpec("RightShoulder", "RightUpperArm", 0.068f, 0.050f),
            new LimbSpec("RightUpperArm", "RightLowerArm", 0.050f, 0.038f),
            new LimbSpec("RightLowerArm", "RightHand",     0.038f, 0.030f),
            new LimbSpec("RightHand",     "RightHandEnd",  0.030f, 0.016f),

            new LimbSpec("LeftUpperLeg",  "LeftLowerLeg",  0.086f, 0.052f),
            new LimbSpec("LeftLowerLeg",  "LeftFoot",      0.052f, 0.034f),
            new LimbSpec("LeftFoot",      "LeftToes",      0.042f, 0.030f),

            new LimbSpec("RightUpperLeg", "RightLowerLeg", 0.086f, 0.052f),
            new LimbSpec("RightLowerLeg", "RightFoot",     0.052f, 0.034f),
            new LimbSpec("RightFoot",     "RightToes",     0.042f, 0.030f)
        };

        public static Dictionary<string, BoneSpec> ByName()
        {
            var map = new Dictionary<string, BoneSpec>(Bones.Length);
            foreach (var bone in Bones) map[bone.Name] = bone;
            return map;
        }
    }
}
