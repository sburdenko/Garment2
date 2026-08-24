using System.IO;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// CLO ships opacity as a separate greyscale file. Unity needs it in the base map's alpha
    /// channel before alpha clipping can cut hair cards, mesh or lace out of their backing quads.
    /// </summary>
    public static class TextureAlphaMerger
    {
        public static Texture2D Merge(Texture2D colour, Texture2D alpha)
        {
            if (colour == null || alpha == null)
            {
                Debug.LogError("Alpha merge: both a colour and an alpha texture are required.");
                return null;
            }

            string colourPath = AssetDatabase.GetAssetPath(colour);
            string outputPath = $"{Path.GetDirectoryName(colourPath)}/{Path.GetFileNameWithoutExtension(colourPath)}_rgba.png";

            var colourPixels = ReadPixels(colourPath, out int width, out int height);
            var alphaPixels = ReadPixels(AssetDatabase.GetAssetPath(alpha), out int alphaWidth, out int alphaHeight);
            if (colourPixels == null || alphaPixels == null) return null;

            var merged = new Color[colourPixels.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                int alphaIndex = Mathf.Clamp(y * alphaHeight / height, 0, alphaHeight - 1) * alphaWidth
                               + Mathf.Clamp(x * alphaWidth / width, 0, alphaWidth - 1);

                var pixel = colourPixels[index];
                pixel.a = alphaPixels[alphaIndex].r;
                merged[index] = pixel;
            }

            var output = new Texture2D(width, height, TextureFormat.RGBA32, true);
            output.SetPixels(merged);
            output.Apply();
            File.WriteAllBytes(outputPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(outputPath) as TextureImporter;
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            Debug.Log($"Merged alpha of '{alpha.name}' into '{colour.name}' -> {outputPath}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }

        private static Color[] ReadPixels(string path, out int width, out int height)
        {
            width = height = 0;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (importer == null || texture == null)
            {
                Debug.LogError($"{path}: not a readable texture.");
                return null;
            }

            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            var pixels = texture.GetPixels();
            width = texture.width;
            height = texture.height;

            if (!wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            return pixels;
        }
    }
}
