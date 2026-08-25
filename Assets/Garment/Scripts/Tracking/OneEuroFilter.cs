using UnityEngine;

namespace Garment.Tracking
{
    /// <summary>
    /// One Euro filter: heavy smoothing at rest, light smoothing in motion. A fixed exponential
    /// filter has to pick one — either the idle pose shakes or fast moves lag behind.
    /// </summary>
    public sealed class OneEuroFilter
    {
        private readonly float minCutoff;
        private readonly float beta;
        private readonly float derivativeCutoff;

        private bool primed;
        private float value;
        private float derivative;

        public OneEuroFilter(float minCutoff, float beta, float derivativeCutoff = 1f)
        {
            this.minCutoff = Mathf.Max(1e-3f, minCutoff);
            this.beta = Mathf.Max(0f, beta);
            this.derivativeCutoff = Mathf.Max(1e-3f, derivativeCutoff);
        }

        public void Reset() => primed = false;

        public float Filter(float raw, float deltaTime)
        {
            deltaTime = Mathf.Max(deltaTime, 1e-4f);
            if (!primed)
            {
                primed = true;
                value = raw;
                derivative = 0f;
                return value;
            }

            float rawDerivative = (raw - value) / deltaTime;
            derivative = Blend(derivative, rawDerivative, derivativeCutoff, deltaTime);

            float cutoff = minCutoff + beta * Mathf.Abs(derivative);
            value = Blend(value, raw, cutoff, deltaTime);
            return value;
        }

        private static float Blend(float current, float target, float cutoff, float deltaTime)
        {
            float alpha = 1f / (1f + 1f / (2f * Mathf.PI * cutoff * deltaTime));
            return Mathf.Lerp(current, target, alpha);
        }
    }

    /// <summary>Per-axis One Euro filter for positions.</summary>
    public sealed class OneEuroFilterVector3
    {
        private readonly OneEuroFilter x;
        private readonly OneEuroFilter y;
        private readonly OneEuroFilter z;

        public OneEuroFilterVector3(float minCutoff, float beta)
        {
            x = new OneEuroFilter(minCutoff, beta);
            y = new OneEuroFilter(minCutoff, beta);
            z = new OneEuroFilter(minCutoff, beta);
        }

        public void Reset()
        {
            x.Reset();
            y.Reset();
            z.Reset();
        }

        public Vector3 Filter(Vector3 raw, float deltaTime) =>
            new Vector3(x.Filter(raw.x, deltaTime), y.Filter(raw.y, deltaTime), z.Filter(raw.z, deltaTime));
    }
}
