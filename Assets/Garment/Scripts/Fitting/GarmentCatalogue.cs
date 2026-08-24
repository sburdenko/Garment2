using System.Collections.Generic;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// The set of garments the app offers. Lives as an asset rather than a scene list so new
    /// items survive scene rebuilds and can be swapped per build.
    /// </summary>
    [CreateAssetMenu(menuName = "Garment/Garment Catalogue", fileName = "GarmentCatalogue")]
    public sealed class GarmentCatalogue : ScriptableObject
    {
        [SerializeField] private List<GarmentDefinition> garments = new List<GarmentDefinition>();
        [Tooltip("Worn on start. Anything not listed here falls back to the first garment of that slot.")]
        [SerializeField] private List<GarmentDefinition> defaultOutfit = new List<GarmentDefinition>();

        public IReadOnlyList<GarmentDefinition> Garments => garments;

        public IReadOnlyList<GarmentDefinition> DefaultOutfit => defaultOutfit;

        public GarmentDefinition DefaultFor(GarmentSlot slot)
        {
            foreach (var garment in defaultOutfit)
                if (garment != null && garment.Slot == slot) return garment;

            foreach (var garment in garments)
                if (garment != null && garment.Slot == slot) return garment;
            return null;
        }
    }
}
