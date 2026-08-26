using UnityEngine;

namespace Garment.Body
{
    /// <summary>
    /// The body a garment is fitted to: a skinned mesh, its bones, and the landmarks
    /// used for measurement. Implementations differ only in where the bones come from.
    /// </summary>
    public abstract class BodyRig : MonoBehaviour
    {
        public abstract SkinnedMeshRenderer BodyMesh { get; }

        public abstract Transform GetBone(BodyLandmark landmark);

        /// <summary>
        /// How much broader the tracked person is than this body's cross-section, measured by
        /// calibration. Garments are widened by this when bound; the skeleton itself is never
        /// scaled — non-uniform scales inside a bone hierarchy shear the mesh as soon as any
        /// joint between them rotates.
        /// </summary>
        public float GirthScale { get; set; } = 1f;

        /// <summary>
        /// How much longer the tracked person's arms are than this body's, measured on screen.
        /// Sleeves are stretched by this when a garment is bound.
        /// </summary>
        public float ArmStretch { get; set; } = 1f;

        /// <summary>
        /// Half-thickness of the tracked person's upper arm, in metres. The sleeve's upper edge
        /// is lifted so it sits on top of an arm this thick — the tracked bone runs through the
        /// arm's centre, and a sleeve authored for a slimmer avatar otherwise cuts through it.
        /// </summary>
        public float ArmRadius { get; set; }

        /// <summary>
        /// Runs an action with the skeleton returned to the pose its bindposes were recorded
        /// in. Anything that measures bones in order to fit or bind a garment must run here,
        /// or the live pose is baked into the garment and then applied again by the skinning.
        /// </summary>
        public void WhileInBindPose(System.Action action) => BindPoseScope.Run(BodyMesh, action);

        public Transform Root => GetBone(BodyLandmark.Hips);

        /// <summary>Full height of the body mesh, crown to sole.</summary>
        public float StandingHeight
        {
            get
            {
                var mesh = BodyMesh;
                return mesh != null ? mesh.bounds.size.y : 0f;
            }
        }

        /// <summary>Horizontal distance between the two upper-leg joints.</summary>
        public float HipWidth
        {
            get
            {
                var left = GetBone(BodyLandmark.LeftUpperLeg);
                var right = GetBone(BodyLandmark.RightUpperLeg);
                if (left == null || right == null) return 0f;
                return Vector3.Distance(left.position, right.position);
            }
        }

        /// <summary>Hip joint down to ankle, measured along the actual limb chain.</summary>
        public float InsideLegLength
        {
            get
            {
                var hip = GetBone(BodyLandmark.LeftUpperLeg);
                var knee = GetBone(BodyLandmark.LeftKnee);
                var ankle = GetBone(BodyLandmark.LeftAnkle);
                if (hip == null || knee == null || ankle == null) return 0f;
                return Vector3.Distance(hip.position, knee.position)
                     + Vector3.Distance(knee.position, ankle.position);
            }
        }

        public float ShoulderWidth
        {
            get
            {
                var left = GetBone(BodyLandmark.LeftShoulder);
                var right = GetBone(BodyLandmark.RightShoulder);
                if (left == null || right == null) return 0f;
                return Vector3.Distance(left.position, right.position);
            }
        }
    }
}
