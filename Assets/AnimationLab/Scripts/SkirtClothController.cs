using UnityEngine;

namespace GarmentDemo.Sandbox
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public sealed class SkirtClothController : MonoBehaviour
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

        private void ConfigureCloth()
        {
            Mesh mesh = GetComponent<SkinnedMeshRenderer>().sharedMesh;
            Vector3[] vertices = cloth.vertices;
            ClothSkinningCoefficient[] coefficients = new ClothSkinningCoefficient[vertices.Length];
            float top = mesh.bounds.max.y;
            float height = mesh.bounds.size.y;
            float minimumMovement = height * 0.008f;
            float maximumMovement = height * 0.08f;

            for (int i = 0; i < vertices.Length; i++)
            {
                float distanceFromWaist = (top - vertices[i].y) / height;
                coefficients[i].maxDistance = distanceFromWaist < 0.10f
                    ? 0f
                    : Mathf.Lerp(minimumMovement, maximumMovement, (distanceFromWaist - 0.10f) / 0.90f);
                coefficients[i].collisionSphereDistance = 0.006f;
            }

            cloth.coefficients = coefficients;
            cloth.useGravity = true;
            cloth.damping = 0.20f;
            cloth.stretchingStiffness = 0.72f;
            cloth.bendingStiffness = 0.15f;
            cloth.useTethers = true;
            cloth.clothSolverFrequency = 90f;
            cloth.worldVelocityScale = 0.8f;
            cloth.worldAccelerationScale = 0.65f;
            cloth.externalAcceleration = Vector3.zero;
        }
    }
}
