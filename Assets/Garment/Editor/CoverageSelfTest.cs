using System.Text;
using Garment.Tracking;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// Exercises the coverage debounce and the One Euro filter on synthetic sequences.
    /// Both are pure state machines, so their timing contract can be checked exactly.
    /// </summary>
    public static class CoverageSelfTest
    {
        [MenuItem("Garment/Test Coverage And Filtering")]
        public static void Run()
        {
            Debug.Log(RunAndReport());
        }

        public static string RunAndReport()
        {
            var report = new StringBuilder("Coverage self-test:\n");
            int failures = 0;

            void Check(string name, bool passed)
            {
                if (!passed) failures++;
                report.AppendLine($"  {(passed ? "PASS" : "FAIL")} {name}");
            }

            TestCoverageTracker(Check);
            TestOneEuroFilter(Check);
            TestRoiFraming(Check);

            report.AppendLine(failures == 0 ? "All passed." : $"{failures} FAILURES.");
            return report.ToString();
        }

        private static void TestCoverageTracker(System.Action<string, bool> check)
        {
            var tracker = new BodyCoverageTracker(promoteSeconds: 0.5f, demoteSeconds: 0.3f);

            check("starts at None", tracker.Coverage == BodyCoverage.None);

            check("first full-body detection is trusted immediately",
                tracker.Update(hasPose: true, rawFullBody: true, time: 0f) == BodyCoverage.FullBody);

            check("a 0.1s lower-body dropout does not demote",
                Feed(tracker, raw: false, from: 0.05f, to: 0.15f) == BodyCoverage.FullBody);

            check("recovery cancels the dropout",
                Feed(tracker, raw: true, from: 0.2f, to: 0.4f) == BodyCoverage.FullBody);

            check("a sustained dropout demotes to UpperBody",
                Feed(tracker, raw: false, from: 0.45f, to: 0.9f) == BodyCoverage.UpperBody);

            check("a 0.2s glimpse of the legs does not promote",
                Feed(tracker, raw: true, from: 1f, to: 1.2f) == BodyCoverage.UpperBody &&
                tracker.Update(true, false, 1.25f) == BodyCoverage.UpperBody);

            check("legs steadily visible for 0.5s promote to FullBody",
                Feed(tracker, raw: true, from: 1.3f, to: 1.85f) == BodyCoverage.FullBody);

            check("losing the pose resets to None",
                tracker.Update(hasPose: false, rawFullBody: false, time: 2f) == BodyCoverage.None);

            check("reacquiring an upper-body pose starts at UpperBody",
                tracker.Update(true, false, 2.1f) == BodyCoverage.UpperBody);
        }

        private static BodyCoverage Feed(BodyCoverageTracker tracker, bool raw, float from, float to)
        {
            var coverage = tracker.Coverage;
            for (float t = from; t <= to + 1e-4f; t += 0.05f)
                coverage = tracker.Update(hasPose: true, rawFullBody: raw, time: t);
            return coverage;
        }

        private static void TestOneEuroFilter(System.Action<string, bool> check)
        {
            const float dt = 1f / 30f;

            var filter = new OneEuroFilter(minCutoff: 1.5f, beta: 10f);
            check("first sample passes through unchanged",
                Mathf.Approximately(filter.Filter(3.7f, dt), 3.7f));

            for (int i = 0; i < 90; i++) filter.Filter(3.7f, dt);
            check("constant input converges to the input",
                Mathf.Abs(filter.Filter(3.7f, dt) - 3.7f) < 1e-3f);

            // Standing still: millimetre sensor noise must shrink, not pass through.
            var jitterFilter = new OneEuroFilter(minCutoff: 1.5f, beta: 10f);
            float rawRange = 0f, filteredMin = float.MaxValue, filteredMax = float.MinValue;
            for (int i = 0; i < 120; i++)
            {
                float raw = 1f + 0.004f * Mathf.Sin(i * 2.1f);
                rawRange = 0.008f;
                float smoothed = jitterFilter.Filter(raw, dt);
                if (i > 30)
                {
                    filteredMin = Mathf.Min(filteredMin, smoothed);
                    filteredMax = Mathf.Max(filteredMax, smoothed);
                }
            }
            check("jitter at rest is attenuated below half",
                filteredMax - filteredMin < rawRange * 0.5f);

            // A fast reach: the filter must open up and follow with little lag.
            var motionFilter = new OneEuroFilter(minCutoff: 1.5f, beta: 10f);
            motionFilter.Filter(0f, dt);
            float value = 0f;
            for (int i = 1; i <= 15; i++) value = motionFilter.Filter(i * 2f * dt, dt);
            check("fast motion is followed with under 20% lag",
                Mathf.Abs(value - 15f * 2f * dt) < 15f * 2f * dt * 0.2f);

            var vector = new OneEuroFilterVector3(minCutoff: 1.5f, beta: 10f);
            var first = vector.Filter(new Vector3(1f, 2f, 3f), dt);
            check("vector filter primes on the first sample",
                (first - new Vector3(1f, 2f, 3f)).sqrMagnitude < 1e-8f);
        }

        private static void TestRoiFraming(System.Action<string, bool> check)
        {
            const float aspect = 0.5f;
            const float padding = 1.35f;

            var screen = UpperBodyPose();
            var visibility = new float[PoseFrame.LandmarkCount];
            for (int i = 0; i < visibility.Length; i++) visibility[i] = 0.9f;

            // Hallucinated legs land somewhere plausible inside the frame...
            SetLegs(screen, kneeY: 0.30f, ankleY: 0.10f, x: 0.5f);
            var roiA = PoseRoi.FromLandmarks(screen, visibility, 0.5f, aspect, padding, includeLegs: false);

            // ...and somewhere else entirely on the next inference.
            SetLegs(screen, kneeY: 0.45f, ankleY: 0.25f, x: 0.85f);
            var roiB = PoseRoi.FromLandmarks(screen, visibility, 0.5f, aspect, padding, includeLegs: false);

            check("untrusted legs cannot move the crop",
                (roiA.Centre - roiB.Centre).sqrMagnitude < 1e-10f &&
                (roiA.HalfExtent - roiB.HalfExtent).sqrMagnitude < 1e-10f);

            var trusted = PoseRoi.FromLandmarks(screen, visibility, 0.5f, aspect, padding, includeLegs: true);
            check("trusted legs still grow the crop downwards",
                trusted.Centre.y < roiB.Centre.y - 1e-3f);

            check("upper-body crop reserves a band below the hips",
                roiA.Centre.y - roiA.HalfExtent.y < 0.50f - 0.20f);
        }

        /// <summary>Upright person: head at 0.92, shoulders at 0.75, hips at 0.50 (frame UV, y up).</summary>
        private static Vector2[] UpperBodyPose()
        {
            var screen = new Vector2[PoseFrame.LandmarkCount];
            for (int i = 0; i <= (int)PoseLandmark.MouthRight; i++) screen[i] = new Vector2(0.5f, 0.92f);
            screen[(int)PoseLandmark.LeftShoulder] = new Vector2(0.62f, 0.75f);
            screen[(int)PoseLandmark.RightShoulder] = new Vector2(0.38f, 0.75f);
            screen[(int)PoseLandmark.LeftElbow] = new Vector2(0.72f, 0.65f);
            screen[(int)PoseLandmark.RightElbow] = new Vector2(0.28f, 0.65f);
            for (int i = (int)PoseLandmark.LeftWrist; i <= (int)PoseLandmark.RightThumb; i++)
                screen[i] = new Vector2(i % 2 == 0 ? 0.24f : 0.76f, 0.58f);
            screen[(int)PoseLandmark.LeftHip] = new Vector2(0.56f, 0.50f);
            screen[(int)PoseLandmark.RightHip] = new Vector2(0.44f, 0.50f);
            return screen;
        }

        private static void SetLegs(Vector2[] screen, float kneeY, float ankleY, float x)
        {
            screen[(int)PoseLandmark.LeftKnee] = new Vector2(x, kneeY);
            screen[(int)PoseLandmark.RightKnee] = new Vector2(1f - x, kneeY);
            screen[(int)PoseLandmark.LeftAnkle] = new Vector2(x, ankleY);
            screen[(int)PoseLandmark.RightAnkle] = new Vector2(1f - x, ankleY);
            for (int i = (int)PoseLandmark.LeftHeel; i <= (int)PoseLandmark.RightFootIndex; i++)
                screen[i] = new Vector2(i % 2 == 0 ? 1f - x : x, ankleY - 0.03f);
        }
    }
}
