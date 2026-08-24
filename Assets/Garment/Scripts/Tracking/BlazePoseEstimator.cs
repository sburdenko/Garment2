using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Runs the BlazePose landmark model over an image and returns a body pose.
    /// The model reports 39 points; the last six are auxiliary and are dropped.
    /// </summary>
    public sealed class BlazePoseEstimator : IDisposable
    {
        private const int InputSize = 256;
        private const int ModelLandmarkCount = 39;
        private const int ValuesPerLandmark = 5;

        private const int LandmarksOutput = 0;
        private const int PresenceOutput = 1;
        private const int SegmentationOutput = 2;
        private const int WorldLandmarksOutput = 4;

        public const int SegmentationSize = 256;

        private readonly Worker worker;
        private readonly Vector3[] world = new Vector3[PoseFrame.LandmarkCount];
        private readonly Vector2[] screen = new Vector2[PoseFrame.LandmarkCount];
        private readonly float[] visibility = new float[PoseFrame.LandmarkCount];
        private Vector2 auxCentre;
        private Vector2 auxScale;
        private readonly TextureTransform transform;

        public BlazePoseEstimator(ModelAsset landmarker, BackendType backend = BackendType.GPUCompute)
        {
            if (landmarker == null) throw new ArgumentNullException(nameof(landmarker));

            worker = new Worker(ModelLoader.Load(landmarker), backend);
            transform = new TextureTransform()
                .SetDimensions(InputSize, InputSize, 3)
                .SetTensorLayout(TensorLayout.NHWC);
        }

        /// <summary>Minimum model confidence before a pose is accepted.</summary>
        public float PresenceThreshold { get; set; } = 0.3f;

        public bool TryEstimate(Texture source, out PoseFrame frame)
        {
            frame = default;
            if (source == null) return false;

            using (var input = TextureConverter.ToTensor(source, transform))
            {
                worker.Schedule(input);
            }

            float presence = ReadPresence();
            if (presence < PresenceThreshold) return false;

            if (!ReadLandmarks() || !ReadWorldLandmarks()) return false;

            frame = new PoseFrame(world, screen, visibility, presence, auxCentre, auxScale);
            return true;
        }

        private float ReadPresence()
        {
            using var tensor = (worker.PeekOutput(PresenceOutput) as Tensor<float>)?.ReadbackAndClone();
            if (tensor == null) return 0f;

            var values = tensor.DownloadToArray();
            // The flag comes out as a raw logit, not a probability.
            return values.Length > 0 ? Sigmoid(values[0]) : 0f;
        }

        private bool ReadLandmarks()
        {
            using var tensor = (worker.PeekOutput(LandmarksOutput) as Tensor<float>)?.ReadbackAndClone();
            if (tensor == null) return false;

            var values = tensor.DownloadToArray();
            if (values.Length < ModelLandmarkCount * ValuesPerLandmark) return false;

            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
            {
                int offset = i * ValuesPerLandmark;
                // Model space has its origin top-left; Unity's viewport origin is bottom-left.
                screen[i] = new Vector2(
                    values[offset] / InputSize,
                    1f - values[offset + 1] / InputSize);
                visibility[i] = Sigmoid(values[offset + 3]);
            }

            auxCentre = ReadPoint(values, PoseFrame.LandmarkCount);
            auxScale = ReadPoint(values, PoseFrame.LandmarkCount + 1);
            return true;
        }

        private static Vector2 ReadPoint(float[] values, int landmarkIndex)
        {
            int offset = landmarkIndex * ValuesPerLandmark;
            return new Vector2(values[offset] / InputSize, 1f - values[offset + 1] / InputSize);
        }

        private bool ReadWorldLandmarks()
        {
            using var tensor = (worker.PeekOutput(WorldLandmarksOutput) as Tensor<float>)?.ReadbackAndClone();
            if (tensor == null) return false;

            var values = tensor.DownloadToArray();
            if (values.Length < ModelLandmarkCount * 3) return false;

            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
            {
                int offset = i * 3;
                // MediaPipe world space is X right, Y down, Z towards the camera.
                world[i] = new Vector3(values[offset], -values[offset + 1], values[offset + 2]);
            }
            return true;
        }

        /// <summary>
        /// Copies the person-segmentation logits of the last inference into <paramref name="mask"/>
        /// (256x256, row 0 at the top). Positive logit = person. This is the model's own mask —
        /// no extra inference runs.
        /// </summary>
        public bool TryReadSegmentation(float[] mask)
        {
            if (mask == null || mask.Length < SegmentationSize * SegmentationSize) return false;

            using var tensor = (worker.PeekOutput(SegmentationOutput) as Tensor<float>)?.ReadbackAndClone();
            if (tensor == null) return false;

            var values = tensor.DownloadToArray();
            if (values.Length < SegmentationSize * SegmentationSize) return false;

            System.Array.Copy(values, mask, SegmentationSize * SegmentationSize);
            return true;
        }

        private static float Sigmoid(float value) => 1f / (1f + Mathf.Exp(-value));

        public void Dispose()
        {
            worker?.Dispose();
        }
    }
}
