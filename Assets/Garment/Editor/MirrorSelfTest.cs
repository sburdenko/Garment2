using System.IO;
using System.Text;
using Garment.Body;
using Garment.Fitting;
using Garment.Tracking;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// Runs the full mirror pipeline against the still photo in the open scene and renders the
    /// result to a file. A fixed photo makes every run comparable, which live video never is.
    /// </summary>
    public static class MirrorSelfTest
    {
        private const string LandmarkerPath = "Assets/Garment/Tracking/pose_landmarks_detector_full.onnx";
        private static string OutputPath => DiagnosticsOutput.PathFor("MirrorSelfTest.png");
        private const int TrackingPasses = 60;

        [MenuItem("Garment/Test Mirror On Photo")]
        public static void Run()
        {
            Debug.Log(RunAndReport());
        }

        public static string RunAndReport()
        {
            var report = new StringBuilder();

            var photo = Object.FindFirstObjectByType<PhotoFrameSource>(FindObjectsInactive.Include);
            if (photo == null || !photo.IsReady) return "Mirror self-test: no PhotoFrameSource with a photo in the open scene.";

            var rig = Object.FindFirstObjectByType<BodyRig>();
            var poseSource = Object.FindFirstObjectByType<TrackedPoseSource>();
            var alignment = Object.FindFirstObjectByType<PoseAlignment>();
            var view = Camera.main;
            if (rig == null || poseSource == null || alignment == null || view == null)
                return "Mirror self-test: open FittingRoom_Mirror — rig, pose source, alignment or camera missing.";

            var landmarker = AssetDatabase.LoadAssetAtPath<ModelAsset>(LandmarkerPath);
            var cropShader = Shader.Find("Garment/RoiCrop");
            if (landmarker == null || cropShader == null) return "Mirror self-test: model or crop shader missing.";

            // Alignment reads the frame's aspect from the provider, so it must be on the photo too.
            var provider = Object.FindFirstObjectByType<WebcamPoseProvider>();
            if (provider != null) provider.UseSource(photo);

            report.AppendLine($"photo={photo.Photo.name} {photo.Photo.width}x{photo.Photo.height} aspect={photo.AspectRatio:0.000}");
            report.AppendLine($"provider source={(provider != null && provider.Feed != null ? provider.Feed.DisplayName : "none")}");

            PoseFrame frame = default;
            float personGirthRatio = 0f;
            using (var tracker = new PoseTracker(landmarker, cropShader) { Mirrored = true })
            {
                bool found = false;
                for (int pass = 0; pass < TrackingPasses; pass++)
                {
                    found = tracker.TryTrack(photo.Texture, out frame);
                    if (!found) break;
                    // The same photo every pass, so the ROI must converge and then hold still.
                    if (pass == 0 || pass == 4 || pass == 20 || pass == TrackingPasses - 1)
                        report.AppendLine($"  pass {pass,2}: roi={tracker.CurrentRoi}");
                }
                if (!found) return report + "Mirror self-test: no body found in the photo.";

                // Girth must be read while the tracker (and its last inference) is still alive.
                var hipCentre = frame.Midpoint2D(PoseLandmark.LeftHip, PoseLandmark.RightHip);
                float boneWidthUv = Mathf.Abs(frame.ScreenOf(PoseLandmark.LeftHip).x - frame.ScreenOf(PoseLandmark.RightHip).x);
                if (boneWidthUv > 1e-3f && tracker.TryMeasureSilhouetteWidth(hipCentre, out float silhouetteUv))
                    personGirthRatio = silhouetteUv / boneWidthUv;
                report.AppendLine($"silhouette girth ratio={personGirthRatio:0.00}");
            }

            // Snapshot before ANY mutation — calibration moves bones too, and whatever is not
            // restored here gets saved into the scene as a poisoned bind pose.
            var snapshot = CaptureSkeleton(rig);

            // Reproduce what the user sees: they press Measure me before judging the fit.
            var calibrator = Object.FindFirstObjectByType<BodyCalibrator>();
            if (calibrator != null)
            {
                // Awake has not run in edit mode; it captures the avatar's own girth baseline.
                calibrator.GetType().GetMethod("Awake",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(calibrator, null);

                var measurements = BodyMeasurements.FromFrame(frame).WithGirth(personGirthRatio);
                calibrator.ApplyMeasurements(measurements);
                report.AppendLine($"calibrated: {measurements}");
                report.AppendLine($"rig scale after calibration={rig.transform.localScale.ToString("0.000")}");
            }

            WearDefaults(rig, report);

            try
            {
                // Several steps so the smoothing filters settle, as they would over live frames.
                for (int step = 0; step < 30; step++)
                {
                    poseSource.ApplyFrame(rig, frame, 1f / 30f);
                    alignment.AlignTo(frame, 1f / 30f);
                }

                report.Append(Describe(rig, frame, view));
                Render(view, photo);
                report.AppendLine($"render -> {OutputPath}");
            }
            finally
            {
                RestoreSkeleton(snapshot);
                rig.GirthScale = 1f;
                foreach (var worn in rig.GetComponentsInChildren<Transform>(true))
                    if (worn != null && worn.name.StartsWith("Garment_")) Object.DestroyImmediate(worn.gameObject);
            }
            return report.ToString();
        }

        private readonly struct BoneState
        {
            public readonly Transform Bone;
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;
            public readonly Vector3 LocalScale;

            public BoneState(Transform bone)
            {
                Bone = bone;
                LocalPosition = bone.localPosition;
                LocalRotation = bone.localRotation;
                LocalScale = bone.localScale;
            }

            public void Restore()
            {
                if (Bone == null) return;
                Bone.localPosition = LocalPosition;
                Bone.localRotation = LocalRotation;
                Bone.localScale = LocalScale;
            }
        }

        private static System.Collections.Generic.List<BoneState> CaptureSkeleton(BodyRig rig)
        {
            var states = new System.Collections.Generic.List<BoneState> { new BoneState(rig.transform) };
            foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
                if (bone != null && !bone.name.StartsWith("Garment_")) states.Add(new BoneState(bone));
            return states;
        }

        private static void RestoreSkeleton(System.Collections.Generic.List<BoneState> snapshot)
        {
            foreach (var state in snapshot) state.Restore();
        }

        private static void WearDefaults(BodyRig rig, StringBuilder report)
        {
            foreach (var existing in rig.GetComponentsInChildren<Transform>(true))
                if (existing != null && existing.name.StartsWith("Garment_")) Object.DestroyImmediate(existing.gameObject);

            var wardrobe = Object.FindFirstObjectByType<Wardrobe.Wardrobe>();
            if (wardrobe == null) return;

            var index = BodySkinIndex.From(rig);
            if (index == null) return;

            foreach (GarmentSlot slot in System.Enum.GetValues(typeof(GarmentSlot)))
            {
                GarmentDefinition wanted = null;
                foreach (var definition in wardrobe.Catalogue)
                    if (definition != null && definition.Slot == slot) { wanted = definition; break; }

                var catalogue = AssetDatabase.LoadAssetAtPath<GarmentCatalogue>("Assets/Garment/Garments/GarmentCatalogue.asset");
                if (catalogue != null && catalogue.DefaultFor(slot) != null) wanted = catalogue.DefaultFor(slot);
                if (wanted == null) continue;

                GarmentBinder.Bind(rig, index, wanted);
                report.AppendLine($"worn [{slot}] {wanted.DisplayName} ({wanted.FitMode})");
            }
        }

        private static string Describe(BodyRig rig, PoseFrame frame, Camera view)
        {
            var report = new StringBuilder();

            var hips = rig.GetBone(BodyLandmark.Hips);
            var leftAnkle = rig.GetBone(BodyLandmark.LeftAnkle);
            var leftShoulder = rig.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = rig.GetBone(BodyLandmark.RightShoulder);

            report.AppendLine($"avatar root={rig.transform.position.ToString("0.00")} " +
                              $"hips={hips.position.ToString("0.00")} height={rig.StandingHeight:0.000}");
            report.AppendLine($"avatar shoulder span={Vector3.Distance(leftShoulder.position, rightShoulder.position):0.000} " +
                              $"hips->ankle={Mathf.Abs(hips.position.y - leftAnkle.position.y):0.000}");

            var measured = BodyMeasurements.FromFrame(frame);
            report.AppendLine($"tracked {measured}");

            // Facing: the avatar's forward should point back at the camera.
            var hipsForward = hips.forward;
            var toCamera = (view.transform.position - hips.position).normalized;
            report.AppendLine($"  facing dot(toCamera)={Vector3.Dot(hipsForward, toCamera):+0.00;-0.00} " +
                              $"({(Vector3.Dot(hipsForward, toCamera) > 0f ? "towards camera" : "AWAY — back to front")})");

            // Where the avatar's joints land on screen versus where the person's are.
            report.AppendLine(Projected("hips", view, hips.position, frame.Midpoint2D(PoseLandmark.LeftHip, PoseLandmark.RightHip)));
            report.AppendLine(Projected("shoulders", view, (leftShoulder.position + rightShoulder.position) * 0.5f,
                frame.Midpoint2D(PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder)));
            report.AppendLine(Projected("left ankle", view, leftAnkle.position, frame.ScreenOf(PoseLandmark.LeftAnkle)));
            return report.ToString();
        }

        private static string Projected(string label, Camera view, Vector3 world, Vector2 tracked)
        {
            var viewport = view.WorldToViewportPoint(world);
            // The feed is mirrored on screen, so the tracked point must be flipped to compare.
            float trackedX = 1f - tracked.x;
            return $"  {label,-11} avatar=({viewport.x:0.000}, {viewport.y:0.000})  tracked=({trackedX:0.000}, {tracked.y:0.000})" +
                   $"  dx={viewport.x - trackedX:+0.000;-0.000}  dy={viewport.y - tracked.y:+0.000;-0.000}";
        }

        /// <summary>
        /// The feed canvas is driven at runtime, so in the editor it has to be pointed at the
        /// photo by hand — otherwise the render shows clothes floating on an empty background.
        /// </summary>
        private static void PrepareBackground(Camera view, PhotoFrameSource photo)
        {
            var display = Object.FindFirstObjectByType<WebcamDisplay>(FindObjectsInactive.Include);
            if (display == null) return;

            var image = display.GetComponent<UnityEngine.UI.RawImage>();
            var fitter = display.GetComponent<UnityEngine.UI.AspectRatioFitter>();
            if (image == null || fitter == null) return;

            image.texture = photo.Texture;
            fitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = photo.AspectRatio;
            image.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            var canvas = display.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = view;
            }
            Canvas.ForceUpdateCanvases();
        }

        private static void Render(Camera view, PhotoFrameSource photo)
        {
            PrepareBackground(view, photo);

            int width = 720;
            int height = Mathf.RoundToInt(width / Mathf.Max(view.aspect, 0.1f));
            var render = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = view.targetTexture;

            view.targetTexture = render;
            view.Render();
            view.targetTexture = previousTarget;

            var previousActive = RenderTexture.active;
            RenderTexture.active = render;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(OutputPath, texture.EncodeToPNG());
            RenderTexture.active = previousActive;

            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(render);
        }
    }
}
