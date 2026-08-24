using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>Prints a model's input and output signature — needed to wire tensors correctly.</summary>
    public static class ModelInspector
    {
        [MenuItem("Assets/Garment/Inspect Model", true)]
        private static bool Validate() => Selection.activeObject is ModelAsset;

        [MenuItem("Assets/Garment/Inspect Model")]
        public static void Inspect()
        {
            Describe(Selection.activeObject as ModelAsset);
        }

        public static string Describe(ModelAsset asset)
        {
            if (asset == null) return string.Empty;

            var model = ModelLoader.Load(asset);
            var report = new StringBuilder();
            report.AppendLine($"=== {asset.name}");

            foreach (var input in model.inputs)
                report.AppendLine($"  IN  {input.name}  shape={input.shape}  dataType={input.dataType}");

            for (int i = 0; i < model.outputs.Count; i++)
                report.AppendLine($"  OUT [{i}] {model.outputs[i].name}");

            report.AppendLine($"  layers={model.layers.Count}");
            Debug.Log(report.ToString());
            return report.ToString();
        }
    }
}
