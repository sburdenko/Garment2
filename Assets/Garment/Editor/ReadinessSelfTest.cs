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
    public static class ReadinessSelfTest
    {
        [MenuItem("Garment/Test Readiness And Filtering")]
        public static void Run()
        {
            Debug.Log(RunAndReport());
        }

        public static string RunAndReport()
        {
            var report = new StringBuilder("Readiness self-test:\n");
            int failures = 0;

            void Check(string name, bool passed)
            {
                if (!passed) failures++;
                report.AppendLine($"  {(passed ? "PASS" : "FAIL")} {name}");
            }

            TestReadiness(Check);
            TestGestures(Check);
            TestOneEuroFilter(Check);
            TestRoiFraming(Check);
            TestCapsulePushout(Check);

            report.AppendLine(failures == 0 ? "All passed." : $"{failures} FAILURES.");
            return report.ToString();
        }

        private static void TestReadiness(System.Action<string, bool> check)
        {
            var tracker = new BodyReadinessTracker(readySeconds: 0.6f, lostSeconds: 0.25f);

            check("starts undressed", !tracker.IsReady);

            check("a single good frame is not enough to dress",
                !tracker.Update(true, 0f));

            check("half a second of a good body is still not enough",
                !Feed(tracker, good: true, from: 0.05f, to: 0.5f));

            check("0.6s of a steadily seen whole body dresses",
                Feed(tracker, good: true, from: 0.55f, to: 0.7f));

            check("a single bad frame does not undress",
                tracker.Update(false, 0.75f));

            check("recovering keeps the clothes on",
                Feed(tracker, good: true, from: 0.8f, to: 1f));

            check("a quarter second of bad tracking undresses",
                !Feed(tracker, good: false, from: 1.05f, to: 1.35f));

            check("dressing again has to be earned from scratch",
                !Feed(tracker, good: true, from: 1.4f, to: 1.8f) &&
                Feed(tracker, good: true, from: 1.85f, to: 2.1f));

            tracker.Reset();
            check("reset undresses", !tracker.IsReady);
        }

        private static bool Feed(BodyReadinessTracker tracker, bool good, float from, float to)
        {
            bool ready = tracker.IsReady;
            for (float t = from; t <= to + 1e-4f; t += 0.05f) ready = tracker.Update(good, t);
            return ready;
        }

        private static void TestGestures(System.Action<string, bool> check)
        {
            var recognizer = new GestureRecognizer(holdSeconds: 0.5f, repeatSeconds: 1f);

            var resting = Pose(rightWristY: 0.45f, leftWristY: 0.45f);
            var rightUp = Pose(rightWristY: 0.95f, leftWristY: 0.45f);
            var leftUp = Pose(rightWristY: 0.45f, leftWristY: 0.95f);
            var bothUp = Pose(rightWristY: 0.95f, leftWristY: 0.95f);

            check("a resting pose fires nothing",
                Fire(recognizer, resting, 0f, 1f) == PoseGesture.None);

            check("a raised hand does not fire before it is held",
                Fire(recognizer, rightUp, 1.05f, 1.4f) == PoseGesture.None);

            check("held for half a second, the right hand fires",
                Fire(recognizer, rightUp, 1.45f, 1.7f) == PoseGesture.RightHandRaised);

            check("holding on does not fire again",
                Fire(recognizer, rightUp, 1.75f, 3.5f) == PoseGesture.None);

            check("lowering and raising again fires again",
                Fire(recognizer, resting, 3.55f, 3.8f) == PoseGesture.None &&
                Fire(recognizer, rightUp, 3.85f, 4.5f) == PoseGesture.RightHandRaised);

            check("the left hand fires its own gesture",
                Fire(recognizer, resting, 4.55f, 4.8f) == PoseGesture.None &&
                Fire(recognizer, leftUp, 4.85f, 5.5f) == PoseGesture.LeftHandRaised);

            check("both hands up is a stretch, not a request",
                Fire(recognizer, resting, 5.55f, 5.8f) == PoseGesture.None &&
                Fire(recognizer, bothUp, 5.85f, 7f) == PoseGesture.None);

            var invisible = Pose(rightWristY: 0.95f, leftWristY: 0.45f, visibility: 0.1f);
            var strict = new GestureRecognizer(holdSeconds: 0.5f, repeatSeconds: 1f);
            check("landmarks the model is unsure of are ignored",
                Fire(strict, invisible, 0f, 2f) == PoseGesture.None);
        }

        private static PoseGesture Fire(GestureRecognizer recognizer, PoseFrame frame, float from, float to)
        {
            var fired = PoseGesture.None;
            for (float t = from; t <= to + 1e-4f; t += 0.05f)
            {
                var now = recognizer.Update(frame, 0.6f, t);
                if (now != PoseGesture.None) fired = now;
            }
            return fired;
        }

        private static void TestCapsulePushout(System.Action<string, bool> check)
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(0f, 1f, 0f);

            var outside = new Vector3(0.3f, 0.5f, 0f);
            check("a point outside the capsule is untouched",
                Garment.Fitting.LegCollisionPushout.PushOutOfCapsule(outside, a, b, 0.2f) == outside);

            var inner = Garment.Fitting.LegCollisionPushout.PushOutOfCapsule(
                new Vector3(0.05f, 0.5f, 0f), a, b, 0.2f);
            var outer = Garment.Fitting.LegCollisionPushout.PushOutOfCapsule(
                new Vector3(0.10f, 0.5f, 0f), a, b, 0.2f);
            check("a point inside lands just past the surface, radially",
                inner.x >= 0.2f && inner.x < 0.21f && Mathf.Abs(inner.y - 0.5f) < 1e-5f);
            check("shell ordering survives: deeper stays deeper",
                inner.x < outer.x);

            var below = Garment.Fitting.LegCollisionPushout.PushOutOfCapsule(
                new Vector3(0f, -0.1f, 0.05f), a, b, 0.2f);
            check("the end caps are round",
                (below - a).magnitude >= 0.2f && (below - a).magnitude < 0.21f);

            var axial = Garment.Fitting.LegCollisionPushout.PushOutOfCapsule(
                new Vector3(0f, 0.5f, 0f), a, b, 0.2f);
            check("a point on the axis still leaves",
                (axial - new Vector3(0f, 0.5f, 0f)).magnitude >= 0.2f);
        }

        /// <summary>An upright person with the nose at 0.80.</summary>
        private static PoseFrame Pose(float rightWristY, float leftWristY, float visibility = 0.95f)
        {
            var screen = new Vector2[PoseFrame.LandmarkCount];
            var world = new Vector3[PoseFrame.LandmarkCount];
            var visible = new float[PoseFrame.LandmarkCount];
            for (int i = 0; i < visible.Length; i++)
            {
                visible[i] = visibility;
                screen[i] = new Vector2(0.5f, 0.5f);
            }

            screen[(int)PoseLandmark.Nose] = new Vector2(0.5f, 0.80f);
            screen[(int)PoseLandmark.RightWrist] = new Vector2(0.35f, rightWristY);
            screen[(int)PoseLandmark.LeftWrist] = new Vector2(0.65f, leftWristY);

            return new PoseFrame(world, screen, visible, 1f, Vector2.zero, Vector2.one);
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

            // With legs trusted the crop hugs the real body; untrusted it reaches further
            // down on purpose, far enough that a standing person's ankles fall inside it.
            var trusted = PoseRoi.FromLandmarks(screen, visibility, 0.5f, aspect, padding, includeLegs: true);
            check("trusted crop frames the actual body",
                trusted.HalfExtent.y <= roiA.HalfExtent.y + 1e-3f);

            check("untrusted crop reaches the ankles of a standing person",
                roiA.Centre.y - roiA.HalfExtent.y <= 0.10f + 1e-3f);
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
