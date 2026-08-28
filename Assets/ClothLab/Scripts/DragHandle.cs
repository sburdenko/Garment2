using UnityEngine;
using UnityEngine.InputSystem;

namespace ClothLab
{
    /// <summary>
    /// Drags the object with the left mouse button across a plane facing the camera, and with the
    /// right button towards and away from it, so the fabric can be shaken by hand. Space returns
    /// the object to where it started.
    /// </summary>
    public sealed class DragHandle : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [Tooltip("Metres travelled per pixel of vertical mouse movement while dragging in depth.")]
        [SerializeField] private float depthSpeed = 0.01f;

        private Vector3 origin;
        private Vector3 grabOffset;
        private Plane plane;
        private bool draggingInPlane;
        private bool draggingInDepth;
        private Vector2 lastPointer;

        private void Awake()
        {
            if (view == null) view = Camera.main;
            origin = transform.position;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || view == null) return;

            Vector2 pointer = mouse.position.ReadValue();

            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                transform.position = origin;

            if (mouse.leftButton.wasPressedThisFrame) BeginPlaneDrag(pointer);
            if (mouse.leftButton.wasReleasedThisFrame) draggingInPlane = false;
            if (mouse.rightButton.wasPressedThisFrame) { draggingInDepth = true; lastPointer = pointer; }
            if (mouse.rightButton.wasReleasedThisFrame) draggingInDepth = false;

            if (draggingInPlane && TryPointOnPlane(pointer, out Vector3 point))
                transform.position = point + grabOffset;

            if (draggingInDepth)
            {
                transform.position += view.transform.forward * ((pointer.y - lastPointer.y) * depthSpeed);
                lastPointer = pointer;
            }
        }

        private void BeginPlaneDrag(Vector2 pointer)
        {
            plane = new Plane(-view.transform.forward, transform.position);
            if (!TryPointOnPlane(pointer, out Vector3 point)) return;
            grabOffset = transform.position - point;
            draggingInPlane = true;
        }

        private bool TryPointOnPlane(Vector2 pointer, out Vector3 point)
        {
            Ray ray = view.ScreenPointToRay(pointer);
            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }
            point = Vector3.zero;
            return false;
        }
    }
}
