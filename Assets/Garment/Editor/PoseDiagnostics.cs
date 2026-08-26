using System.IO;
using System.Text;
using Garment.Body;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>Dumps every model output and the image fed to it, for wiring up the tensors.</summary>
    public static class PoseDiagnostics
    {
        private const string LandmarkerPath = "Assets/Garment/Tracking/pose_landmarks_detector_full.onnx";
        private static string DumpPath => DiagnosticsOutput.PathFor("PoseSelfTestInput.png");
        private const int InputSize = 256;

        public static string Run()
        {
            var rig = Object.FindFirstObjectByType<BodyRig>();
            if (rig == null) return "no BodyRig in scene";

            var landmarker = AssetDatabase.LoadAssetAtPath<ModelAsset>(LandmarkerPath);
            if (landmarker == null) return "landmarker missing";

            var render = Render(rig);
            SaveDump(render);

            var report = new StringBuilder();
            var model = ModelLoader.Load(landmarker);
            using var worker = new Worker(model, BackendType.GPUCompute);

            var transform = new TextureTransform()
                .SetDimensions(InputSize, InputSize, 3)
                .SetTensorLayout(TensorLayout.NHWC);

            using (var input = TextureConverter.ToTensor(render, transform))
            {
                report.AppendLine($"input shape={input.shape}");
                worker.Schedule(input);
            }

            for (int i = 0; i < model.outputs.Count; i++)
            {
                var raw = worker.PeekOutput(i) as Tensor<float>;
                if (raw == null)
                {
                    report.AppendLine($"OUT[{i}] {model.outputs[i].name}: not a float tensor");
                    continue;
                }

                using var cpu = raw.ReadbackAndClone();
                var values = cpu.DownloadToArray();
                report.Append($"OUT[{i}] {model.outputs[i].name} shape={cpu.shape} count={values.Length} first=");
                for (int v = 0; v < Mathf.Min(6, values.Length); v++) report.Append($"{values[v]:0.###} ");
                report.AppendLine();

                if (i == 0 && values.Length >= 39 * 5)
                {
                    for (int lm = 32; lm < 39; lm++)
                    {
                        int o = lm * 5;
                        report.AppendLine($"    landmark[{lm}] x={values[o]:0.##} y={values[o + 1]:0.##} " +
                                          $"z={values[o + 2]:0.##} vis={values[o + 3]:0.##} pres={values[o + 4]:0.##}");
                    }
                }
            }

            RenderTexture.active = null;
            Object.DestroyImmediate(render);
            return report.ToString();
        }

        private static RenderTexture Render(BodyRig rig)
        {
            var render = new RenderTexture(InputSize, InputSize, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("PoseDiagnosticsCamera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.55f, 0.55f, 0.6f);
                camera.fieldOfView = 50f;
                camera.targetTexture = render;

                float height = Mathf.Max(rig.StandingHeight, 1.2f);
                var pivot = rig.transform.position + Vector3.up * height * 0.55f;
                float distance = height / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.2f;
                camera.transform.position = pivot + new Vector3(0f, 0f, -distance);
                camera.transform.LookAt(pivot);
                camera.Render();
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
            return render;
        }

        private static void SaveDump(RenderTexture render)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = render;
            var texture = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(DumpPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            RenderTexture.active = previous;
        }
    }
}
