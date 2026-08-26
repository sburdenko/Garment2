using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// Whether the tracker is holding a whole body well enough to dress it.
    ///
    /// Debounced in both directions, and deliberately not symmetric: becoming ready has to be
    /// earned over time, because dressing someone the tracker has not settled on looks worse
    /// than a moment's wait, while coming undressed is quicker, because a garment left on a
    /// body the tracker has lost is the thing that reads as broken.
    /// </summary>
    public sealed class BodyReadinessTracker
    {
        private readonly float readySeconds;
        private readonly float lostSeconds;

        private float goodSince;
        private float lastGood;
        private bool hasStreak;

        public BodyReadinessTracker(float readySeconds, float lostSeconds)
        {
            this.readySeconds = Mathf.Max(0f, readySeconds);
            this.lostSeconds = Mathf.Max(0f, lostSeconds);
        }

        public bool IsReady { get; private set; }

        public void Reset()
        {
            IsReady = false;
            hasStreak = false;
        }

        public bool Update(bool wholeBodyVisible, float time)
        {
            if (wholeBodyVisible)
            {
                lastGood = time;
                if (!hasStreak)
                {
                    goodSince = time;
                    hasStreak = true;
                }
            }
            else
            {
                hasStreak = false;
            }

            if (IsReady) IsReady = time - lastGood < lostSeconds;
            else IsReady = hasStreak && time - goodSince >= readySeconds;

            return IsReady;
        }
    }
}
