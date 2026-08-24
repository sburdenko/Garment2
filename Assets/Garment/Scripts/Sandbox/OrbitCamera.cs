using UnityEngine;
using UnityEngine.InputSystem;

namespace Garment.Sandbox
{
    /// <summary>Drag to orbit, wheel to zoom. Lets the garment be inspected from every angle.</summary>
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 0.95f, 0f);
        [SerializeField] private float distance = 3.2f;
        [SerializeField] private float minDistance = 1.2f;
        [SerializeField] private float maxDistance = 6f;
        [SerializeField] private float orbitSensitivity = 0.25f;
        [SerializeField] private float zoomSensitivity = 0.15f;
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private Vector2 startAngles = new Vector2(15f, 20f);

        private float yaw;
        private float pitch;

        private void Awake()
        {
            pitch = startAngles.x;
            yaw = startAngles.y;
        }

        private void LateUpdate()
        {
            ReadInput();

            var pivot = (target != null ? target.position : Vector3.zero) + targetOffset;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
        }

        private void ReadInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * orbitSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, minPitch, maxPitch);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - scroll * zoomSensitivity * 0.05f, minDistance, maxDistance);
        }
    }
}
