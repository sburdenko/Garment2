using System.IO;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// CLO texture atlases leave the space between pattern pieces empty. Any UV that lands
    /// there — a seam pixel, or geometry whose UDIM tile was never exported — renders as a
    /// black hole. Bleeding the fabric outwards over the empty space removes both.
    /// </summary>
    public static class TextureDilator
    {
        private const float EmptyThreshold = 0.02f;
        private const int BleedIterations = 24;

        [MenuItem("Assets/Garment/Dilate Texture", true)]
        private static bool Validate() => Selection.activeObject is Texture2D;

        [MenuItem("Assets/Garment/Dilate Texture")]
        public static void DilateSelection()
        {
            var dilated = Dilate(Selection.activeObject as Texture2D);
            if (dilated != null) Selection.activeObject = dilated;
        }

        public static Texture2D Dilate(Texture2D source)
        {
            if (source == null) return null;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string outputPath = $"{Path.GetDirectoryName(sourcePath)}/{Path.GetFileNameWithoutExtension(sourcePath)}_filled.png";

            var pixels = ReadPixels(source, sourcePath, out int width, out int height);
            if (pixels == null) return null;

            var filled = new bool[pixels.Length];
            int filledCount = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                filled[i] = pixels[i].a > 0.5f && Luminance(pixels[i]) > EmptyThreshold;
                if (filled[i]) filledCount++;
            }

            if (filledCount == 0)
            {
                Debug.LogWarning($"{source.name}: texture is entirely empty, nothing to bleed.");
                return null;
            }

            Bleed(pixels, filled, width, height);
            FillRemainder(pixels, filled, AverageOf(pixels, filled));

            var output = new Texture2D(width, height, TextureFormat.RGBA32, true);
            output.SetPixels(pixels);
            output.Apply();
            File.WriteAllBytes(outputPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"Dilated '{source.name}' ({filledCount * 100 / pixels.Length}% covered) -> {outputPath}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }

        private static Color[] ReadPixels(Texture2D source, string path, out int width, out int height)
        {
            width = height = 0;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"{path}: not a texture asset.");
                return null;
            }

            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            var pixels = source.GetPixels();
            width = source.width;
            height = source.height;

            if (!wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            return pixels;
        }

        private static void Bleed(Color[] pixels, bool[] filled, int width, int height)
        {
            var next = (bool[])filled.Clone();
            for (int iteration = 0; iteration < BleedIterations; iteration++)
            {
                bool changed = false;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (filled[index]) continue;

                    var sum = Color.clear;
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int neighbour = ny * width + nx;
                        if (!filled[neighbour]) continue;
                        sum += pixels[neighbour];
                        count++;
                    }
                    if (count == 0) continue;

                    pixels[index] = sum / count;
                    next[index] = true;
                    changed = true;
                }
                if (!changed) break;
                System.Array.Copy(next, filled, filled.Length);
            }
        }

        private static void FillRemainder(Color[] pixels, bool[] filled, Color average)
        {
            for (int i = 0; i < pixels.Length; i++)
                if (!filled[i]) pixels[i] = average;
        }

        private static Color AverageOf(Color[] pixels, bool[] filled)
        {
            var sum = Color.clear;
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (!filled[i]) continue;
                sum += pixels[i];
                count++;
            }
            return count == 0 ? Color.grey : sum / count;
        }

        private static float Luminance(Color color) => 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
    }
}
