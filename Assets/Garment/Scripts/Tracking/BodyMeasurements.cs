using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Limb lengths of a real person, in metres, taken from the tracker's metric landmarks.
    /// A single RGB camera cannot see girth, so this is proportions only — never a size.
    /// </summary>
    public readonly struct BodyMeasurements
    {
        public readonly float ShoulderWidth;
        public readonly float TorsoLength;
        public readonly float UpperArm;
        public readonly float LowerArm;
        public readonly float UpperLeg;
        public readonly float LowerLeg;
        public readonly float HipWidth;

        /// <summary>Silhouette width at the hips divided by the bone width there. 0 = not measured.</summary>
        public readonly float HipGirthRatio;

        /// <summary>
        /// How much of the body these numbers actually come from. The leg lengths are always
        /// populated — the model reports leg landmarks even when the legs are out of frame —
        /// so this is the only thing that says whether they mean anything.
        /// </summary>
        public readonly BodyCoverage Coverage;

        public BodyMeasurements(float shoulderWidth, float torsoLength, float upperArm, float lowerArm,
                                float upperLeg, float lowerLeg, float hipWidth, float hipGirthRatio = 0f,
                                BodyCoverage coverage = BodyCoverage.FullBody)
        {
            Coverage = coverage;
            ShoulderWidth = shoulderWidth;
            TorsoLength = torsoLength;
            UpperArm = upperArm;
            LowerArm = lowerArm;
            UpperLeg = upperLeg;
            LowerLeg = lowerLeg;
            HipWidth = hipWidth;
            HipGirthRatio = hipGirthRatio;
        }

        public BodyMeasurements WithGirth(float ratio) => new BodyMeasurements(
            ShoulderWidth, TorsoLength, UpperArm, LowerArm, UpperLeg, LowerLeg, HipWidth, ratio, Coverage);

        /// <summary>The person's torso length per unit of shoulder width — a proportion that
        /// holds still while the hip landmark, out at the edge of a waist-up frame, does not.</summary>
        public float TorsoPerShoulder => ShoulderWidth > 1e-3f ? TorsoLength / ShoulderWidth : 0f;

        /// <summary>Legs are only judged when they were actually seen; otherwise they are noise.</summary>
        public bool IsPlausible =>
            ShoulderWidth > 0.15f && ShoulderWidth < 0.8f &&
            TorsoLength > 0.2f && TorsoLength < 1f &&
            (Coverage != BodyCoverage.FullBody || (UpperLeg > 0.15f && LowerLeg > 0.15f));

        public static BodyMeasurements FromFrame(PoseFrame frame, BodyCoverage coverage = BodyCoverage.FullBody)
        {
            var hipCentre = frame.Midpoint(PoseLandmark.LeftHip, PoseLandmark.RightHip);
            var shoulderCentre = frame.Midpoint(PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder);

            return new BodyMeasurements(
                shoulderWidth: Distance(frame, PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder),
                torsoLength: Vector3.Distance(hipCentre, shoulderCentre),
                upperArm: Mean(Distance(frame, PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow),
                               Distance(frame, PoseLandmark.RightShoulder, PoseLandmark.RightElbow)),
                lowerArm: Mean(Distance(frame, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist),
                               Distance(frame, PoseLandmark.RightElbow, PoseLandmark.RightWrist)),
                upperLeg: Mean(Distance(frame, PoseLandmark.LeftHip, PoseLandmark.LeftKnee),
                               Distance(frame, PoseLandmark.RightHip, PoseLandmark.RightKnee)),
                lowerLeg: Mean(Distance(frame, PoseLandmark.LeftKnee, PoseLandmark.LeftAnkle),
                               Distance(frame, PoseLandmark.RightKnee, PoseLandmark.RightAnkle)),
                hipWidth: Distance(frame, PoseLandmark.LeftHip, PoseLandmark.RightHip),
                coverage: coverage);
        }

        /// <summary>Running average, so a calibration can settle over several frames.</summary>
        public BodyMeasurements Blend(BodyMeasurements other, float weight) => new BodyMeasurements(
            Mathf.Lerp(ShoulderWidth, other.ShoulderWidth, weight),
            Mathf.Lerp(TorsoLength, other.TorsoLength, weight),
            Mathf.Lerp(UpperArm, other.UpperArm, weight),
            Mathf.Lerp(LowerArm, other.LowerArm, weight),
            Mathf.Lerp(UpperLeg, other.UpperLeg, weight),
            Mathf.Lerp(LowerLeg, other.LowerLeg, weight),
            Mathf.Lerp(HipWidth, other.HipWidth, weight),
            other.HipGirthRatio > 0f && HipGirthRatio > 0f
                ? Mathf.Lerp(HipGirthRatio, other.HipGirthRatio, weight)
                : Mathf.Max(HipGirthRatio, other.HipGirthRatio),
            // Blending an upper-body sample into a full-body one leaves legs that were never
            // seen; the result is only as complete as its least complete part.
            Coverage == BodyCoverage.FullBody && other.Coverage == BodyCoverage.FullBody
                ? BodyCoverage.FullBody
                : BodyCoverage.UpperBody);

        private static float Distance(PoseFrame frame, PoseLandmark a, PoseLandmark b) =>
            Vector3.Distance(frame.WorldOf(a), frame.WorldOf(b));

        private static float Mean(float a, float b) => (a + b) * 0.5f;

        public override string ToString() =>
            $"shoulders={ShoulderWidth:0.000} torso={TorsoLength:0.000} upperArm={UpperArm:0.000} " +
            $"lowerArm={LowerArm:0.000} upperLeg={UpperLeg:0.000} lowerLeg={LowerLeg:0.000} hips={HipWidth:0.000} " +
            $"girthRatio={HipGirthRatio:0.00} from={Coverage}";
    }
}
