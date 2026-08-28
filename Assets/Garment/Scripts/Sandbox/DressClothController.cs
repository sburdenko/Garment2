using UnityEngine;

namespace GarmentDemo.Sandbox
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public sealed class DressClothController : MonoBehaviour
    {
        private Cloth cloth;

        public bool IsClothEnabled
        {
            get
            {
                EnsureInitialized();
                return cloth.enabled;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (cloth != null)
                return;

            cloth = GetComponent<Cloth>();
            if (cloth == null)
                cloth = gameObject.AddComponent<Cloth>();

            ConfigureCloth();
        }

        public void SetClothEnabled(bool enabled)
        {
            EnsureInitialized();
            cloth.enabled = enabled;
            if (enabled)
                cloth.ClearTransformMotion();
        }

        private void LateUpdate()
        {
            if (!cloth.enabled)
                return;

            float sway = Mathf.Sin(Time.time * 2.4f);
            float flutter = Mathf.Sin(Time.time * 4.1f + 0.8f);
            cloth.externalAcceleration = new Vector3(sway * 6f, 0f, flutter * 2.5f);
        }

        private void ConfigureCloth()
        {
            Vector3[] vertices = cloth.vertices;
            ClothSkinningCoefficient[] coefficients = new ClothSkinningCoefficient[vertices.Length];
            float minimumZ = float.MaxValue;
            float maximumZ = float.MinValue;
            float maximumX = 0f;

            foreach (Vector3 vertex in vertices)
            {
                minimumZ = Mathf.Min(minimumZ, vertex.z);
                maximumZ = Mathf.Max(maximumZ, vertex.z);
                maximumX = Mathf.Max(maximumX, Mathf.Abs(vertex.x));
            }

            float simulationTop = Mathf.Lerp(minimumZ, maximumZ, 0.40f);
            float bodyLimit = maximumX * 0.52f;
            float hemFreedom = (maximumZ - minimumZ) * 0.10f;
            var bandRadii = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<float>>();
            var bandMedians = new System.Collections.Generic.Dictionary<int, float>();
            var radii = new float[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                radii[i] = Mathf.Sqrt(vertex.x * vertex.x + vertex.y * vertex.y);
                if (vertex.z >= simulationTop || Mathf.Abs(vertex.x) >= bodyLimit)
                    continue;

                int band = Mathf.RoundToInt(vertex.z * 50f);
                if (!bandRadii.TryGetValue(band, out var values))
                    bandRadii[band] = values = new System.Collections.Generic.List<float>();
                values.Add(radii[i]);
            }

            foreach (var band in bandRadii)
            {
                band.Value.Sort();
                bandMedians[band.Key] = band.Value[band.Value.Count / 2];
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                int band = Mathf.RoundToInt(vertex.z * 50f);
                bool skirtVertex = vertex.z < simulationTop && Mathf.Abs(vertex.x) < bodyLimit;
                bool innerShell = skirtVertex && radii[i] < bandMedians[band];
                float skirtMovement = hemFreedom * Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(simulationTop, minimumZ, vertex.z));

                coefficients[i].maxDistance = skirtVertex && !innerShell ? skirtMovement : 0f;
                coefficients[i].collisionSphereDistance = 0.012f;
            }

            cloth.coefficients = coefficients;
            cloth.useGravity = true;
            cloth.damping = 0.25f;
            cloth.stretchingStiffness = 0.75f;
            cloth.bendingStiffness = 0.20f;
            cloth.friction = 0.4f;
            cloth.useTethers = true;
            cloth.clothSolverFrequency = 90f;
            cloth.worldVelocityScale = 0.9f;
            cloth.worldAccelerationScale = 0.6f;
            cloth.randomAcceleration = new Vector3(0.5f, 0.2f, 0.5f);
        }
    }
}
