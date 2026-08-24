namespace Garment.Fitting
{
    /// <summary>
    /// How a submesh reacts to the body. Zippers, buttons and buckles must stay rigid:
    /// per-vertex skinning would shear them apart as the body moves.
    /// </summary>
    public enum SubmeshRole
    {
        Fabric,
        Rigid
    }
}
