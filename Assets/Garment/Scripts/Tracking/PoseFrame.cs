using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>One inference result: where the body is, in metres and on screen.</summary>
    public readonly struct PoseFrame
    {
        public const int LandmarkCount = 33;

        /// <summary>Metric 3D positions, origin roughly at the hips, Y up.</summary>
        public readonly Vector3[] World;

        /// <summary>Normalised image positions, 0..1 with origin bottom-left.</summary>
        public readonly Vector2[] Screen;

        /// <summary>Per-landmark visibility, 0..1.</summary>
        public readonly float[] Visibility;

        /// <summary>Model's own confidence that a body is present at all.</summary>
        public readonly float Presence;

        /// <summary>
        /// The two auxiliary points the model emits after the 33 body landmarks: the hip centre
        /// and a scale/rotation reference. MediaPipe uses them to place the next frame's crop,
        /// which is what lets tracking continue without re-running the detector.
        /// </summary>
        public readonly Vector2 AuxCentre;
        public readonly Vector2 AuxScale;

        public PoseFrame(Vector3[] world, Vector2[] screen, float[] visibility, float presence,
                         Vector2 auxCentre, Vector2 auxScale)
        {
            World = world;
            Screen = screen;
            Visibility = visibility;
            Presence = presence;
            AuxCentre = auxCentre;
            AuxScale = auxScale;
        }

        public bool IsValid => World != null && World.Length == LandmarkCount;

        public Vector3 WorldOf(PoseLandmark landmark) => World[(int)landmark];

        public Vector2 ScreenOf(PoseLandmark landmark) => Screen[(int)landmark];

        public float VisibilityOf(PoseLandmark landmark) => Visibility[(int)landmark];

        public Vector3 Midpoint(PoseLandmark a, PoseLandmark b) => (WorldOf(a) + WorldOf(b)) * 0.5f;

        public Vector2 Midpoint2D(PoseLandmark a, PoseLandmark b) => (ScreenOf(a) + ScreenOf(b)) * 0.5f;
    }
}
