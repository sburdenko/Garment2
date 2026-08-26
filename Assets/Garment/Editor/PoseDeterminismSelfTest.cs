using System.Collections.Generic;
using System.Text;
using Garment.Body;
using Garment.Tracking;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// The rig's pose must depend on the tracked landmarks alone, never on how it got there.
    /// Aiming a bone by the short way round from its current rotation leaves the roll about its
    /// own axis free to accumulate — which is what wound the sleeves around the arms every time
    /// the photo changed. Returning to a pose must return the bones to the same rotations.
    /// </summary>
    public static class PoseDeterminismSelfTest
    {
        private const float DeltaTime = 1f / 30f;
        private const int SettleFrames = 60;

        [MenuItem("Garment/Test Pose Determinism")]
        public static void Run() => Debug.Log(RunAndReport());

        public static string RunAndReport()
        {
            var rig = Object.FindFirstObjectByType<BodyRig>();
            if (rig == null) return "Pose determinism self-test: no BodyRig in the open scene.";
            var source = Object.FindFirstObjectByType<TrackedPoseSource>();
            if (source == null) return "Pose determinism self-test: no TrackedPoseSource in the open scene.";

            var report = new StringBuilder("Pose determinism self-test:\n");
            int failures = 0;
            var restore = Snapshot(rig);

            try
            {
                var armsOut = StandingPose(leftArmUp: false);
                var armUp = StandingPose(leftArmUp: true);

                Settle(source, rig, armsOut);
                var first = Snapshot(rig);

                // Go somewhere else and come back — the classic photo switch.
                Settle(source, rig, armUp);
                Settle(source, rig, armsOut);
                var returned = Snapshot(rig);

                float drift = WorstAngle(first, returned);
                bool passed = drift < 0.5f;
                if (!passed) failures++;
                report.AppendLine($"  {(passed ? "PASS" : "FAIL")} returning to a pose reproduces it"
                                + $" (worst bone drift {drift:F2}°)");

                // Switching back and forth must not creep, however many times it happens.
                for (int i = 0; i < 8; i++)
                {
                    Settle(source, rig, armUp);
                    Settle(source, rig, armsOut);
                }
                float creep = WorstAngle(first, Snapshot(rig));
                bool noCreep = creep < 0.5f;
                if (!noCreep) failures++;
                report.AppendLine($"  {(noCreep ? "PASS" : "FAIL")} eight switches accumulate nothing"
                                + $" (worst bone drift {creep:F2}°)");

                // The upper arm is where roll shows: it must sit at its natural roll, not a
                // wound-up one, no matter which pose preceded it.
                var upperArm = rig.GetBone(BodyLandmark.LeftShoulder);
                if (upperArm != null)
                {
                    Settle(source, rig, armUp);
                    var viaUp = upperArm.rotation;
                    Settle(source, rig, armsOut);
                    Settle(source, rig, armUp);
                    float armDrift = Quaternion.Angle(viaUp, upperArm.rotation);
                    bool armStable = armDrift < 0.5f;
                    if (!armStable) failures++;
                    report.AppendLine($"  {(armStable ? "PASS" : "FAIL")} upper arm keeps its roll"
                                    + $" (drift {armDrift:F2}°)");
                }
            }
            finally
            {
                Restore(restore);
            }

            report.AppendLine(failures == 0 ? "All passed." : $"{failures} FAILURES.");
            return report.ToString();
        }

        /// <summary>Feeds one frame until the landmark smoothing has converged on it.</summary>
        private static void Settle(TrackedPoseSource source, BodyRig rig, PoseFrame frame)
        {
            for (int i = 0; i < SettleFrames; i++)
                source.ApplyFrame(rig, frame, DeltaTime, BodyCoverage.FullBody);
        }

        /// <summary>A plain standing figure in metres, hips at the origin, optionally reaching up.</summary>
        private static PoseFrame StandingPose(bool leftArmUp)
        {
            var world = new Vector3[PoseFrame.LandmarkCount];
            var screen = new Vector2[PoseFrame.LandmarkCount];
            var visibility = new float[PoseFrame.LandmarkCount];
            for (int i = 0; i < visibility.Length; i++) visibility[i] = 1f;

            void Set(PoseLandmark landmark, float x, float y, float z = 0f) =>
                world[(int)landmark] = new Vector3(x, y, z);

            Set(PoseLandmark.Nose, 0f, 0.72f);
            Set(PoseLandmark.LeftShoulder, 0.19f, 0.52f);
            Set(PoseLandmark.RightShoulder, -0.19f, 0.52f);
            Set(PoseLandmark.RightElbow, -0.46f, 0.50f);
            Set(PoseLandmark.RightWrist, -0.71f, 0.48f);
            Set(PoseLandmark.LeftHip, 0.10f, 0f);
            Set(PoseLandmark.RightHip, -0.10f, 0f);
            Set(PoseLandmark.LeftKnee, 0.11f, -0.44f);
            Set(PoseLandmark.RightKnee, -0.11f, -0.44f);
            Set(PoseLandmark.LeftAnkle, 0.11f, -0.86f);
            Set(PoseLandmark.RightAnkle, -0.11f, -0.86f);

            if (leftArmUp)
            {
                Set(PoseLandmark.LeftElbow, 0.30f, 0.76f, 0.06f);
                Set(PoseLandmark.LeftWrist, 0.38f, 1.01f, 0.12f);
            }
            else
            {
                Set(PoseLandmark.LeftElbow, 0.46f, 0.50f);
                Set(PoseLandmark.LeftWrist, 0.71f, 0.48f);
            }

            return new PoseFrame(world, screen, visibility, 1f, Vector2.zero, Vector2.zero);
        }

        private static List<(Transform bone, Quaternion rotation)> Snapshot(BodyRig rig)
        {
            var state = new List<(Transform, Quaternion)>();
            var bones = rig.BodyMesh != null ? rig.BodyMesh.bones : new Transform[0];
            foreach (var bone in bones)
                if (bone != null) state.Add((bone, bone.rotation));
            return state;
        }

        private static void Restore(List<(Transform bone, Quaternion rotation)> state)
        {
            foreach (var (bone, rotation) in state)
                if (bone != null) bone.rotation = rotation;
        }

        private static float WorstAngle(
            List<(Transform bone, Quaternion rotation)> a, List<(Transform bone, Quaternion rotation)> b)
        {
            float worst = 0f;
            for (int i = 0; i < a.Count && i < b.Count; i++)
                worst = Mathf.Max(worst, Quaternion.Angle(a[i].rotation, b[i].rotation));
            return worst;
        }
    }
}
