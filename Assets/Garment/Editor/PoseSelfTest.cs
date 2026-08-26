using System.Text;
using Garment.Body;
using Garment.Tracking;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// Runs the pose model against a render of the avatar itself. The bone positions are known,
    /// so this checks the tracking maths — coordinate conventions, ordering, scale — without
    /// needing a webcam or a person in front of it.
    /// </summary>
    public static class PoseSelfTest
    {
        private const string LandmarkerPath = "Assets/Garment/Tracking/pose_landmarks_detector_full.onnx";
        private const int RenderSize = 512;

        [MenuItem("Garment/Test Pose On Avatar")]
        public static void Run()
        {
            Debug.Log(RunAndReport());
        }

        public static string RunAndReport()
        {
            var rig = Object.FindFirstObjectByType<BodyRig>();
            if (rig == null) return "Pose self-test: no BodyRig in the open scene.";

            var landmarker = AssetDatabase.LoadAssetAtPath<ModelAsset>(LandmarkerPath);
            if (landmarker == null) return $"Pose self-test: {LandmarkerPath} not found.";

            var cropShader = Shader.Find("Garment/RoiCrop");
            if (cropShader == null) return "Pose self-test: Garment/RoiCrop shader not found.";

            var render = RenderAvatar(rig);
            try
            {
                using var tracker = new PoseTracker(landmarker, cropShader, BackendType.GPUCompute) { Mirrored = false };

                // The first pass sees the whole frame; each following pass re-crops around the body.
                PoseFrame frame = default;
                bool found = false;
                var report = new StringBuilder();
                for (int pass = 0; pass < 3; pass++)
                {
                    found = tracker.TryTrack(render, out frame);
                    report.AppendLine($"pass {pass}: found={found} roi={tracker.CurrentRoi}");
                    if (!found) break;
                }

                if (!found) return report + "Pose self-test: model found no body in the render.";
                return report + Compare(rig, frame);
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(render);
            }
        }

        private static RenderTexture RenderAvatar(BodyRig rig)
        {
            var render = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("PoseSelfTestCamera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.5f, 0.5f, 0.55f);
                camera.fieldOfView = 50f;
                camera.targetTexture = render;

                float height = Mathf.Max(rig.StandingHeight, 1.2f);
                var pivot = rig.transform.position + Vector3.up * height * 0.55f;
                float distance = height / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.25f;
                camera.transform.SetPositionAndRotation(pivot + rig.transform.forward * -distance, Quaternion.identity);
                camera.transform.LookAt(pivot);

                camera.Render();
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
            return render;
        }

        private static string Compare(BodyRig rig, PoseFrame frame)
        {
            var report = new StringBuilder();
            report.AppendLine($"Pose self-test: presence={frame.Presence:0.00}");

            var pairs = new (PoseLandmark landmark, BodyLandmark bone)[]
            {
                (PoseLandmark.LeftShoulder, BodyLandmark.LeftShoulder),
                (PoseLandmark.RightShoulder, BodyLandmark.RightShoulder),
                (PoseLandmark.LeftElbow, BodyLandmark.LeftElbow),
                (PoseLandmark.LeftWrist, BodyLandmark.LeftWrist),
                (PoseLandmark.LeftHip, BodyLandmark.LeftUpperLeg),
                (PoseLandmark.RightHip, BodyLandmark.RightUpperLeg),
                (PoseLandmark.LeftKnee, BodyLandmark.LeftKnee),
                (PoseLandmark.LeftAnkle, BodyLandmark.LeftAnkle)
            };

            foreach (var (landmark, bone) in pairs)
            {
                var boneTransform = rig.GetBone(bone);
                report.AppendLine(
                    $"  {landmark,-14} world={frame.WorldOf(landmark).ToString("0.00")} " +
                    $"screen={frame.ScreenOf(landmark).ToString("0.00")} vis={frame.VisibilityOf(landmark):0.00} " +
                    $"| bone {bone} at {(boneTransform == null ? "none" : boneTransform.position.ToString("0.00"))}");
            }

            float measuredShoulderSpan = Vector3.Distance(
                frame.WorldOf(PoseLandmark.LeftShoulder), frame.WorldOf(PoseLandmark.RightShoulder));
            float measuredHipSpan = Vector3.Distance(
                frame.WorldOf(PoseLandmark.LeftHip), frame.WorldOf(PoseLandmark.RightHip));

            report.AppendLine($"  measured shoulder span={measuredShoulderSpan:0.000} m (rig {rig.ShoulderWidth:0.000})");
            report.AppendLine($"  measured hip span={measuredHipSpan:0.000} m (rig {rig.HipWidth:0.000})");
            return report.ToString();
        }
    }
}
