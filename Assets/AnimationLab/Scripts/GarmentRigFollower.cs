using System.Collections.Generic;
using UnityEngine;

namespace GarmentDemo.Sandbox
{
    public sealed class GarmentRigFollower : MonoBehaviour
    {
        [SerializeField] private Transform sourceSkeletonRoot;
        [SerializeField] private Transform garmentSkeletonRoot;

        private Transform[] sourceBones;
        private Transform[] garmentBones;
        private float positionScale;

        private void Awake()
        {
            var sourceByName = new Dictionary<string, Transform>();
            foreach (Transform bone in sourceSkeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name.StartsWith("mixamorig:"))
                    sourceByName[bone.name] = bone;
            }

            var matchedSourceBones = new List<Transform>();
            var matchedGarmentBones = new List<Transform>();
            foreach (Transform garmentBone in garmentSkeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (sourceByName.TryGetValue(garmentBone.name, out Transform sourceBone))
                {
                    matchedSourceBones.Add(sourceBone);
                    matchedGarmentBones.Add(garmentBone);
                }
            }

            sourceBones = matchedSourceBones.ToArray();
            garmentBones = matchedGarmentBones.ToArray();
            positionScale = sourceSkeletonRoot.lossyScale.x / garmentSkeletonRoot.lossyScale.x;
        }

        private void LateUpdate()
        {
            for (int i = 0; i < sourceBones.Length; i++)
            {
                garmentBones[i].localPosition = sourceBones[i].localPosition * positionScale;
                garmentBones[i].localRotation = sourceBones[i].localRotation;
            }
        }
    }
}
