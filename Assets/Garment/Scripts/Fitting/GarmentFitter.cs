using Garment.Body;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Places a garment authored on an unknown avatar onto this body: matches the garment's
    /// vertical span to the span between the slot's landmarks, then widens it if the body is
    /// broader than the mesh was cut for.
    /// </summary>
    public static class GarmentFitter
    {
        private const float MinScale = 0.4f;
        private const float MaxScale = 2.5f;

        public static GarmentFit Compute(BodyRig body, GarmentDefinition definition, Mesh garmentMesh, Vector3[] bodyVertices)
        {
            if (body == null || definition == null || garmentMesh == null) return GarmentFit.Identity;
            if (definition.FitMode == GarmentFitMode.Native)
                return new GarmentFit(Vector3.one, definition.PositionOffset);

            if (!TryGetSpan(body, definition.Slot, out float bodyTopY, out float bodyBottomY))
            {
                Debug.LogError($"Garment '{definition.DisplayName}': body is missing landmarks for slot {definition.Slot}.");
                return GarmentFit.Identity;
            }

            var bounds = garmentMesh.bounds;
            float garmentSpan = bounds.size.y;
            if (garmentSpan < 1e-4f) return GarmentFit.Identity;

            float uniform = Mathf.Clamp((bodyTopY - bodyBottomY) / garmentSpan * definition.ScaleMultiplier, MinScale, MaxScale);

            float widthFactor = definition.CompensateWidth
                ? WidthFactor(garmentMesh, bounds, uniform, bodyVertices, bodyBottomY, bodyTopY, definition.SkinOffset)
                : 1f;

            var scale = new Vector3(uniform * widthFactor, uniform, uniform * widthFactor);

            var scaledCenter = Vector3.Scale(bounds.center, scale);
            float scaledTopY = scaledCenter.y + bounds.extents.y * uniform;
            var bodyCenter = HorizontalCenter(bodyVertices, bodyBottomY, bodyTopY);

            var offset = new Vector3(
                bodyCenter.x - scaledCenter.x,
                bodyTopY - scaledTopY,
                bodyCenter.y - scaledCenter.z);

            return new GarmentFit(scale, offset + definition.PositionOffset);
        }

        private static bool TryGetSpan(BodyRig body, GarmentSlot slot, out float topY, out float bottomY)
        {
            topY = 0f;
            bottomY = 0f;
            var root = body.transform;

            switch (slot)
            {
                case GarmentSlot.Bottom:
                {
                    var hips = body.GetBone(BodyLandmark.Hips);
                    var leftAnkle = body.GetBone(BodyLandmark.LeftAnkle);
                    var rightAnkle = body.GetBone(BodyLandmark.RightAnkle);
                    if (hips == null || leftAnkle == null || rightAnkle == null) return false;
                    topY = root.InverseTransformPoint(hips.position).y;
                    bottomY = Mathf.Min(root.InverseTransformPoint(leftAnkle.position).y,
                                        root.InverseTransformPoint(rightAnkle.position).y);
                    return true;
                }
                case GarmentSlot.Top:
                case GarmentSlot.Outer:
                {
                    // The top of an upper-body garment is its collar, and a collar sits at the
                    // neck — anchoring it to the shoulder joint drops the whole garment by the
                    // collar's height: neckline on the chest, hem above the waist.
                    var neck = body.GetBone(BodyLandmark.Neck);
                    var shoulder = body.GetBone(BodyLandmark.LeftShoulder);
                    var hips = body.GetBone(BodyLandmark.Hips);
                    if (hips == null || (neck == null && shoulder == null)) return false;
                    topY = root.InverseTransformPoint((neck != null ? neck : shoulder).position).y;
                    bottomY = root.InverseTransformPoint(hips.position).y;
                    return true;
                }
                case GarmentSlot.Hair:
                {
                    var head = body.GetBone(BodyLandmark.Head);
                    var neck = body.GetBone(BodyLandmark.Neck);
                    if (head == null || neck == null) return false;
                    topY = root.InverseTransformPoint(head.position).y;
                    bottomY = root.InverseTransformPoint(neck.position).y;
                    return true;
                }
                case GarmentSlot.Footwear:
                {
                    var ankle = body.GetBone(BodyLandmark.LeftAnkle);
                    if (ankle == null) return false;
                    topY = root.InverseTransformPoint(ankle.position).y;
                    bottomY = 0f;
                    return true;
                }
                default:
                    return false;
            }
        }

        private static float WidthFactor(
            Mesh garmentMesh, Bounds bounds, float uniform, Vector3[] bodyVertices,
            float bodyBottomY, float bodyTopY, float skinOffset)
        {
            float bodyHalfWidth = MaxHorizontalRadius(bodyVertices, bodyBottomY, bodyTopY);
            if (bodyHalfWidth <= 0f) return 1f;

            float garmentHalfWidth = Mathf.Max(bounds.extents.x, bounds.extents.z) * uniform;
            if (garmentHalfWidth <= 1e-4f) return 1f;

            float required = (bodyHalfWidth + skinOffset) / garmentHalfWidth;
            return Mathf.Clamp(required, 1f, 1.6f);
        }

        private static float MaxHorizontalRadius(Vector3[] vertices, float minY, float maxY)
        {
            float maxRadius = 0f;
            foreach (var v in vertices)
            {
                if (v.y < minY || v.y > maxY) continue;
                float radius = Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.z));
                if (radius > maxRadius) maxRadius = radius;
            }
            return maxRadius;
        }

        private static Vector2 HorizontalCenter(Vector3[] vertices, float minY, float maxY)
        {
            var sum = Vector2.zero;
            int count = 0;
            foreach (var v in vertices)
            {
                if (v.y < minY || v.y > maxY) continue;
                sum += new Vector2(v.x, v.z);
                count++;
            }
            return count == 0 ? Vector2.zero : sum / count;
        }
    }
}
