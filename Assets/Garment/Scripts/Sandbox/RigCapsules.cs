using System.Collections.Generic;
using UnityEngine;

namespace GarmentDemo.Sandbox
{
    /// <summary>
    /// Trigger capsules that ride a skeleton, authored in world metres, for Unity Cloth to
    /// collide with. Cloth only ever pushes fabric out of these, so a garment can be given a
    /// generous freedom budget without the drape sinking into the limb it hangs from.
    /// </summary>
    public static class RigCapsules
    {
        public readonly struct Segment
        {
            public readonly string FromBone;
            public readonly string ToBone;
            public readonly float Radius;

            public Segment(string fromBone, string toBone, float radius)
            {
                FromBone = fromBone;
                ToBone = toBone;
                Radius = radius;
            }
        }

        /// <summary>Builds one capsule per segment whose bones both exist; others are skipped.</summary>
        public static CapsuleCollider[] Build(Transform skeletonRoot, IReadOnlyList<Segment> segments)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform bone in skeletonRoot.GetComponentsInChildren<Transform>(true))
                bones[bone.name] = bone;

            var capsules = new List<CapsuleCollider>(segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                if (!bones.TryGetValue(segment.FromBone, out Transform top)) continue;
                if (!bones.TryGetValue(segment.ToBone, out Transform bottom)) continue;
                if (segment.Radius <= 0f) continue;
                capsules.Add(BuildOne(segment, top, bottom));
            }

            return capsules.ToArray();
        }

        public static void Destroy(CapsuleCollider[] capsules)
        {
            if (capsules == null) return;
            foreach (CapsuleCollider capsule in capsules)
                if (capsule != null) Object.Destroy(capsule.gameObject);
        }

        private static CapsuleCollider BuildOne(Segment segment, Transform top, Transform bottom)
        {
            var holder = new GameObject($"ClothCapsule_{segment.FromBone}");
            holder.transform.SetParent(top, false);

            // A Mixamo rig carries a 0.01 bone scale; without cancelling it the capsule shrinks
            // to a speck and the cloth falls straight through the limb.
            Vector3 inherited = top.lossyScale;
            holder.transform.localScale = new Vector3(
                SafeInverse(inherited.x), SafeInverse(inherited.y), SafeInverse(inherited.z));
            holder.transform.localPosition = top.InverseTransformPoint((top.position + bottom.position) * 0.5f);

            Vector3 axis = bottom.position - top.position;
            if (axis.sqrMagnitude > 0f)
                holder.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);

            var capsule = holder.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = segment.Radius;
            capsule.height = axis.magnitude + segment.Radius * 2f;
            return capsule;
        }

        private static float SafeInverse(float value)
        {
            return Mathf.Abs(value) < 1e-6f ? 1f : 1f / value;
        }
    }
}
