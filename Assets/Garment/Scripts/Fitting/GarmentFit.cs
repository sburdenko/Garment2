using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>An affine placement of a garment mesh into body-root space.</summary>
    public readonly struct GarmentFit
    {
        public readonly Vector3 Scale;
        public readonly Vector3 Offset;

        public GarmentFit(Vector3 scale, Vector3 offset)
        {
            Scale = scale;
            Offset = offset;
        }

        public static GarmentFit Identity => new GarmentFit(Vector3.one, Vector3.zero);

        public Vector3 Apply(Vector3 localVertex) => Vector3.Scale(localVertex, Scale) + Offset;

        public GarmentFit WithOffset(Vector3 extraOffset) => new GarmentFit(Scale, Offset + extraOffset);

        public override string ToString() => $"scale={Scale} offset={Offset}";
    }
}
