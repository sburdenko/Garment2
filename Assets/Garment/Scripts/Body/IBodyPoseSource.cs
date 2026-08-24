namespace Garment.Body
{
    /// <summary>
    /// Whatever drives the body's pose. The demo uses a procedural source; camera-based
    /// tracking replaces this implementation and nothing downstream changes.
    /// </summary>
    public interface IBodyPoseSource
    {
        bool IsPosing { get; }

        void ApplyTo(BodyRig rig, float deltaTime);
    }
}
