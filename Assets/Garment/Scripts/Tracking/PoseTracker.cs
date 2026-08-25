using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Keeps a body tracked across frames: crops the region the body was last seen in, runs the
    /// landmark model on it, and moves the crop to follow. Landmarks come back in crop space,
    /// so they are mapped out to the full frame before anyone else sees them.
    /// </summary>
    public sealed class PoseTracker : IDisposable
    {
        private const int CropSize = 256;
        private const float RoiPadding = 1.35f;
        private const float RoiVisibility = 0.5f;
        private const float MinimumShoulderWidth = 0.05f;
        private const float MinimumTorsoHeight = 0.08f;
        private const float SteadyScreenBlend = 0.2f;
        private const float FastScreenBlend = 0.85f;
        private const float SteadyMotionDistance = 0.01f;
        private const float FastMotionDistance = 0.08f;

        private readonly BlazePoseEstimator estimator;
        private readonly Material cropMaterial;
        private readonly RenderTexture crop;
        private readonly Vector2[] rawScreen = new Vector2[PoseFrame.LandmarkCount];
        private readonly Vector2[] filteredScreen = new Vector2[PoseFrame.LandmarkCount];
        private readonly Vector2[] screen = new Vector2[PoseFrame.LandmarkCount];

        private PoseRoi roi;
        private bool hasRoi;
        private bool hasFilteredScreen;
        private float[] segmentation;

        public PoseTracker(ModelAsset landmarker, Shader cropShader, BackendType backend = BackendType.GPUCompute)
        {
            if (cropShader == null) throw new ArgumentNullException(nameof(cropShader));

            estimator = new BlazePoseEstimator(landmarker, backend);
            cropMaterial = new Material(cropShader);
            crop = new RenderTexture(CropSize, CropSize, 0, RenderTextureFormat.ARGB32);
        }

        /// <summary>Mirror the crop so the user sees themselves as in a mirror.</summary>
        public bool Mirrored { get; set; } = true;

        public PoseRoi CurrentRoi => roi;

        public RenderTexture LastCrop => crop;

        public bool TryTrack(Texture source, out PoseFrame frame)
        {
            frame = default;
            if (source == null) return false;

            float aspect = (float)source.width / source.height;
            if (!hasRoi) roi = PoseRoi.FullFrame(aspect);

            Blit(source);

            if (!estimator.TryEstimate(crop, out var cropFrame))
            {
                // Lost the body — widen back out so the next frame can find it again.
                hasRoi = false;
                return false;
            }

            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
                rawScreen[i] = roi.ToSource(cropFrame.Screen[i], Mirrored);

            int leftShoulder = (int)PoseLandmark.LeftShoulder;
            int rightShoulder = (int)PoseLandmark.RightShoulder;
            int leftHip = (int)PoseLandmark.LeftHip;
            int rightHip = (int)PoseLandmark.RightHip;
            if (cropFrame.Visibility[leftShoulder] < RoiVisibility ||
                cropFrame.Visibility[rightShoulder] < RoiVisibility ||
                cropFrame.Visibility[leftHip] < RoiVisibility ||
                cropFrame.Visibility[rightHip] < RoiVisibility ||
                Vector2.Distance(rawScreen[leftShoulder], rawScreen[rightShoulder]) < MinimumShoulderWidth ||
                Vector2.Distance((rawScreen[leftShoulder] + rawScreen[rightShoulder]) * 0.5f,
                    (rawScreen[leftHip] + rawScreen[rightHip]) * 0.5f) < MinimumTorsoHeight)
            {
                hasRoi = false;
                return false;
            }

            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
            {
                if (!hasFilteredScreen)
                {
                    filteredScreen[i] = rawScreen[i];
                }
                else
                {
                    float response = Mathf.InverseLerp(SteadyMotionDistance, FastMotionDistance,
                        Vector2.Distance(filteredScreen[i], rawScreen[i]));
                    float blend = Mathf.Lerp(SteadyScreenBlend, FastScreenBlend, response);
                    filteredScreen[i] = Vector2.Lerp(filteredScreen[i], rawScreen[i], blend);
                }

                screen[i] = filteredScreen[i];
            }
            hasFilteredScreen = true;

            var auxCentre = roi.ToSource(cropFrame.AuxCentre, Mirrored);
            var auxScale = roi.ToSource(cropFrame.AuxScale, Mirrored);
            roi = PoseRoi.FromLandmarks(screen, cropFrame.Visibility, RoiVisibility, aspect, RoiPadding);
            hasRoi = true;

            var world = cropFrame.World;
            var visibility = cropFrame.Visibility;

            // Mirroring the view is exactly a left/right swap. Negating the coordinates as well
            // applies the flip twice and puts every limb back on the wrong side.
            if (Mirrored) SwapSides(world, screen, visibility);

            frame = new PoseFrame(world, screen, visibility, cropFrame.Presence, auxCentre, auxScale);
            return true;
        }

        /// <summary>
        /// Width of the person's silhouette at a source-space point, in source UV along X,
        /// read from the model's segmentation mask. This is what a bone skeleton cannot see:
        /// how broad the body actually is.
        /// </summary>
        public bool TryMeasureSilhouetteWidth(Vector2 sourceUv, out float widthSourceUv)
        {
            widthSourceUv = 0f;
            if (!hasRoi) return false;

            segmentation ??= new float[BlazePoseEstimator.SegmentationSize * BlazePoseEstimator.SegmentationSize];
            if (!estimator.TryReadSegmentation(segmentation)) return false;

            int size = BlazePoseEstimator.SegmentationSize;
            var cropUv = roi.FromSource(sourceUv, Mirrored);
            if (cropUv.x < 0f || cropUv.x > 1f || cropUv.y < 0f || cropUv.y > 1f) return false;

            // Mask rows run top-down; crop UV has its origin bottom-left.
            int row = Mathf.Clamp(Mathf.RoundToInt((1f - cropUv.y) * (size - 1)), 0, size - 1);
            int seed = Mathf.Clamp(Mathf.RoundToInt(cropUv.x * (size - 1)), 0, size - 1);
            if (segmentation[row * size + seed] <= 0f) return false;

            int left = seed;
            while (left > 0 && segmentation[row * size + left - 1] > 0f) left--;
            int right = seed;
            while (right < size - 1 && segmentation[row * size + right + 1] > 0f) right++;

            float widthInCrop = (right - left + 1) / (float)size;
            widthSourceUv = widthInCrop * roi.HalfExtent.x * 2f;
            return true;
        }

        /// <summary>
        /// Height of the person's silhouette at a source-space point, in source UV along Y —
        /// the thickness of a horizontal limb, where a row scan would measure its length.
        /// </summary>
        public bool TryMeasureSilhouetteHeight(Vector2 sourceUv, out float heightSourceUv)
        {
            heightSourceUv = 0f;
            if (!hasRoi) return false;

            segmentation ??= new float[BlazePoseEstimator.SegmentationSize * BlazePoseEstimator.SegmentationSize];
            if (!estimator.TryReadSegmentation(segmentation)) return false;

            int size = BlazePoseEstimator.SegmentationSize;
            var cropUv = roi.FromSource(sourceUv, Mirrored);
            if (cropUv.x < 0f || cropUv.x > 1f || cropUv.y < 0f || cropUv.y > 1f) return false;

            int col = Mathf.Clamp(Mathf.RoundToInt(cropUv.x * (size - 1)), 0, size - 1);
            int seed = Mathf.Clamp(Mathf.RoundToInt((1f - cropUv.y) * (size - 1)), 0, size - 1);
            if (segmentation[seed * size + col] <= 0f) return false;

            int top = seed;
            while (top > 0 && segmentation[(top - 1) * size + col] > 0f) top--;
            int bottom = seed;
            while (bottom < size - 1 && segmentation[(bottom + 1) * size + col] > 0f) bottom++;

            float heightInCrop = (bottom - top + 1) / (float)size;
            heightSourceUv = heightInCrop * roi.HalfExtent.y * 2f;
            return true;
        }

        /// <summary>Restores real left and right after the image has been flipped.</summary>
        private static void SwapSides(Vector3[] world, Vector2[] screen, float[] visibility)
        {
            foreach (var (left, right) in PoseLandmarks.SymmetricPairs)
            {
                (world[left], world[right]) = (world[right], world[left]);
                (screen[left], screen[right]) = (screen[right], screen[left]);
                (visibility[left], visibility[right]) = (visibility[right], visibility[left]);
            }
        }

        public void Reset()
        {
            hasRoi = false;
            hasFilteredScreen = false;
        }

        private void Blit(Texture source)
        {
            cropMaterial.SetTexture("_MainTex", source);
            cropMaterial.SetVector("_Center", roi.Centre);
            cropMaterial.SetVector("_Size", roi.HalfExtent);
            cropMaterial.SetFloat("_Angle", roi.Angle);
            cropMaterial.SetFloat("_Mirror", Mirrored ? 1f : 0f);
            Graphics.Blit(source, crop, cropMaterial);
        }

        public void Dispose()
        {
            estimator?.Dispose();
            if (cropMaterial != null) UnityEngine.Object.DestroyImmediate(cropMaterial);
            if (crop != null)
            {
                crop.Release();
                UnityEngine.Object.DestroyImmediate(crop);
            }
        }
    }
}
