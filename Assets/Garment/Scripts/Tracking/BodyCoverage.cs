using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>How much of the tracked person the camera actually sees.</summary>
    public enum BodyCoverage
    {
        None,
        UpperBody,
        FullBody
    }

    /// <summary>
    /// Debounces the per-frame lower-body visibility into a stable state. The raw flag flips
    /// many times a second when knees hover at the frame edge; garments and the skeleton
    /// overlay must switch rarely and deliberately, not with every inference.
    /// </summary>
    public sealed class BodyCoverageTracker
    {
        private readonly float promoteSeconds;
        private readonly float demoteSeconds;

        private float fullBodyStreakStart;
        private float fullBodyLastSeen;
        private bool hasStreak;

        public BodyCoverageTracker(float promoteSeconds, float demoteSeconds)
        {
            this.promoteSeconds = Mathf.Max(0f, promoteSeconds);
            this.demoteSeconds = Mathf.Max(0f, demoteSeconds);
        }

        public BodyCoverage Coverage { get; private set; } = BodyCoverage.None;

        public void Reset()
        {
            Coverage = BodyCoverage.None;
            hasStreak = false;
        }

        public BodyCoverage Update(bool hasPose, bool rawFullBody, float time)
        {
            if (!hasPose)
            {
                Reset();
                return Coverage;
            }

            if (rawFullBody)
            {
                fullBodyLastSeen = time;
                if (!hasStreak)
                {
                    fullBodyStreakStart = time;
                    hasStreak = true;
                }
            }
            else
            {
                hasStreak = false;
            }

            switch (Coverage)
            {
                // The first detection is the detector's best frame — trust it as-is; the
                // hysteresis exists to guard transitions afterwards, not acquisition.
                case BodyCoverage.None:
                    Coverage = rawFullBody ? BodyCoverage.FullBody : BodyCoverage.UpperBody;
                    break;
                case BodyCoverage.UpperBody:
                    if (hasStreak && time - fullBodyStreakStart >= promoteSeconds)
                        Coverage = BodyCoverage.FullBody;
                    break;
                case BodyCoverage.FullBody:
                    if (time - fullBodyLastSeen >= demoteSeconds)
                        Coverage = BodyCoverage.UpperBody;
                    break;
            }

            return Coverage;
        }
    }
}
