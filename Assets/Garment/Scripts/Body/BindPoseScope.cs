using System;
using UnityEngine;

namespace Garment.Body
{
    /// <summary>
    /// Puts a skinned skeleton back into the pose its bindposes were recorded in for the
    /// duration of an action, then returns it to the pose it was in.
    ///
    /// A garment is skinned with the body's bindposes but FITTED by measuring live bone
    /// transforms. Measure those while the tracker holds the body posed and that pose is baked
    /// into the garment mesh, then applied a second time by the skinning — the sleeves wind
    /// around the arms. Binding has to happen inside this scope.
    ///
    /// Target rotations are derived from the bindposes rather than snapshotted at startup: a
    /// scene saved with a posed skeleton would poison a snapshot, and has done before.
    /// </summary>
    public static class BindPoseScope
    {
        public static void Run(SkinnedMeshRenderer skin, Action action)
        {
            if (action == null) return;

            var bones = skin != null ? skin.bones : null;
            var bindposes = skin != null && skin.sharedMesh != null ? skin.sharedMesh.bindposes : null;
            if (bones == null || bindposes == null || bones.Length != bindposes.Length)
            {
                action();
                return;
            }

            // Only rotations are ever written: the tracker moves nothing else, and bone scale
            // carries the calibrated girth, which the garment is meant to be fitted to.
            var order = ParentsFirst(bones);
            var live = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) live[i] = bones[i].rotation;

            var skinToWorld = skin.localToWorldMatrix;
            try
            {
                foreach (int i in order)
                    if (bones[i] != null)
                        bones[i].rotation = (skinToWorld * bindposes[i].inverse).rotation;

                action();
            }
            finally
            {
                foreach (int i in order)
                    if (bones[i] != null) bones[i].rotation = live[i];
            }
        }

        /// <summary>
        /// Bone indices ordered shallowest first. Writing a world rotation re-places every
        /// descendant, so a child written before its parent is undone by the parent's write.
        /// </summary>
        private static int[] ParentsFirst(Transform[] bones)
        {
            var depth = new int[bones.Length];
            var order = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                order[i] = i;
                for (var t = bones[i]; t != null; t = t.parent) depth[i]++;
            }
            Array.Sort(depth, order);
            return order;
        }
    }
}
