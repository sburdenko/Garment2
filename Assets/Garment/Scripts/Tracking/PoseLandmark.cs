namespace Garment.Tracking
{
    /// <summary>
    /// BlazePose's 33 keypoints, in the order the model outputs them.
    /// Left and right are the subject's own, not the viewer's.
    /// </summary>
    public enum PoseLandmark
    {
        Nose = 0,
        LeftEyeInner = 1,
        LeftEye = 2,
        LeftEyeOuter = 3,
        RightEyeInner = 4,
        RightEye = 5,
        RightEyeOuter = 6,
        LeftEar = 7,
        RightEar = 8,
        MouthLeft = 9,
        MouthRight = 10,
        LeftShoulder = 11,
        RightShoulder = 12,
        LeftElbow = 13,
        RightElbow = 14,
        LeftWrist = 15,
        RightWrist = 16,
        LeftPinky = 17,
        LeftIndex = 18,
        LeftThumb = 19,
        RightPinky = 20,
        RightIndex = 21,
        RightThumb = 22,
        LeftHip = 23,
        RightHip = 24,
        LeftKnee = 25,
        RightKnee = 26,
        LeftAnkle = 27,
        RightAnkle = 28,
        LeftHeel = 29,
        RightHeel = 30,
        LeftFootIndex = 31,
        RightFootIndex = 32
    }

    public static class PoseLandmarks
    {
        /// <summary>
        /// Landmarks that swap identity when the image is mirrored. Looking at a mirrored frame
        /// the model calls a person's right arm their left one, so the labels have to be swapped
        /// back or the avatar mirrors the pose limb for limb.
        /// </summary>
        public static readonly (int left, int right)[] SymmetricPairs =
        {
            ((int)PoseLandmark.LeftEyeInner, (int)PoseLandmark.RightEyeInner),
            ((int)PoseLandmark.LeftEye, (int)PoseLandmark.RightEye),
            ((int)PoseLandmark.LeftEyeOuter, (int)PoseLandmark.RightEyeOuter),
            ((int)PoseLandmark.LeftEar, (int)PoseLandmark.RightEar),
            ((int)PoseLandmark.MouthLeft, (int)PoseLandmark.MouthRight),
            ((int)PoseLandmark.LeftShoulder, (int)PoseLandmark.RightShoulder),
            ((int)PoseLandmark.LeftElbow, (int)PoseLandmark.RightElbow),
            ((int)PoseLandmark.LeftWrist, (int)PoseLandmark.RightWrist),
            ((int)PoseLandmark.LeftPinky, (int)PoseLandmark.RightPinky),
            ((int)PoseLandmark.LeftIndex, (int)PoseLandmark.RightIndex),
            ((int)PoseLandmark.LeftThumb, (int)PoseLandmark.RightThumb),
            ((int)PoseLandmark.LeftHip, (int)PoseLandmark.RightHip),
            ((int)PoseLandmark.LeftKnee, (int)PoseLandmark.RightKnee),
            ((int)PoseLandmark.LeftAnkle, (int)PoseLandmark.RightAnkle),
            ((int)PoseLandmark.LeftHeel, (int)PoseLandmark.RightHeel),
            ((int)PoseLandmark.LeftFootIndex, (int)PoseLandmark.RightFootIndex)
        };
    }
}
