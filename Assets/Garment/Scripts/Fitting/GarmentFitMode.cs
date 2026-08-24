namespace Garment.Fitting
{
    /// <summary>
    /// How a garment relates to the body it is put on. A CLO garment exported together with
    /// its avatar already sits exactly where the designer sewed it — rescaling that is damage,
    /// not fitting. A garment authored on some other body has to be measured onto this one.
    /// </summary>
    public enum GarmentFitMode
    {
        Native,
        AutoFit
    }
}
