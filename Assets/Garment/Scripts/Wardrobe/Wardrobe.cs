using System;
using System.Collections.Generic;
using Garment.Body;
using Garment.Fitting;
using UnityEngine;

namespace Garment.Wardrobe
{
    /// <summary>Owns what the body is currently wearing: one garment per slot.</summary>
    public sealed class Wardrobe : MonoBehaviour
    {
        [SerializeField] private BodyRig body;
        [SerializeField] private GarmentCatalogue catalogue;
        [Tooltip("Put on the first garment of each slot when the scene starts.")]
        [SerializeField] private bool wearOnStart = true;
        [SerializeField, Range(0.85f, 1.15f)] private float topFitScale = 1f;

        // Binding a garment costs hundreds of milliseconds, so a garment is bound once and
        // then only shown or hidden. Switching outfits must not stall the frame.
        private readonly Dictionary<GarmentDefinition, GameObject> bound = new Dictionary<GarmentDefinition, GameObject>();
        private readonly Dictionary<GarmentSlot, GarmentDefinition> worn = new Dictionary<GarmentSlot, GarmentDefinition>();
        private BodySkinIndex bodyIndex;

        public event Action<GarmentSlot, GarmentDefinition> Changed;

        private static readonly IReadOnlyList<GarmentDefinition> Empty = new GarmentDefinition[0];

        public IReadOnlyList<GarmentDefinition> Catalogue => catalogue != null ? catalogue.Garments : Empty;

        public BodyRig Body => body;

        public float TopFitScale
        {
            get => topFitScale;
            set
            {
                topFitScale = Mathf.Clamp(value, 0.85f, 1.15f);
                if (worn.TryGetValue(GarmentSlot.Top, out var definition) &&
                    bound.TryGetValue(definition, out var instance) && instance != null)
                    instance.transform.localScale = new Vector3(topFitScale, 1f, topFitScale);
            }
        }

        public GarmentDefinition WornIn(GarmentSlot slot) => worn.TryGetValue(slot, out var definition) ? definition : null;

        private void Awake()
        {
            if (body == null) body = FindFirstObjectByType<BodyRig>();
            bodyIndex = BodySkinIndex.From(body);
            if (catalogue == null) Debug.LogWarning($"{name}: no garment catalogue assigned.", this);
        }

        private void Start()
        {
            if (!wearOnStart || catalogue == null) return;

            foreach (GarmentSlot slot in System.Enum.GetValues(typeof(GarmentSlot)))
            {
                var definition = catalogue.DefaultFor(slot);
                if (definition != null) Wear(definition);
            }
        }

        public void Wear(GarmentDefinition definition)
        {
            if (definition == null) return;
            if (bodyIndex == null)
            {
                Debug.LogError("Wardrobe: no usable body; cannot bind garments.", this);
                return;
            }

            if (definition.Slot == GarmentSlot.Top || definition.Slot == GarmentSlot.Outer)
            {
                Remove(GarmentSlot.Top);
                Remove(GarmentSlot.Outer);
            }
            else
            {
                Remove(definition.Slot);
            }

            if (!bound.TryGetValue(definition, out var instance) || instance == null)
            {
                instance = GarmentBinder.Bind(body, bodyIndex, definition);
                if (instance == null) return;
                bound[definition] = instance;
            }

            instance.SetActive(true);
            if (definition.Slot == GarmentSlot.Top)
                instance.transform.localScale = new Vector3(topFitScale, 1f, topFitScale);
            worn[definition.Slot] = definition;
            Changed?.Invoke(definition.Slot, definition);
        }

        public void Remove(GarmentSlot slot)
        {
            if (!worn.TryGetValue(slot, out var definition)) return;

            if (bound.TryGetValue(definition, out var instance) && instance != null)
                instance.SetActive(false);

            worn.Remove(slot);
            Changed?.Invoke(slot, null);
        }

        /// <summary>
        /// Throws away every bound garment and puts the worn ones back on. Needed whenever the
        /// skeleton itself changes — bind poses are measured against the bones as they were.
        /// </summary>
        public void Rebuild()
        {
            var wornNow = new List<GarmentDefinition>(worn.Values);

            foreach (var instance in bound.Values)
                if (instance != null) Destroy(instance);
            bound.Clear();
            worn.Clear();

            bodyIndex = BodySkinIndex.From(body);
            foreach (var definition in wornNow) Wear(definition);
        }

        public void Toggle(GarmentDefinition definition)
        {
            if (definition == null) return;
            if (WornIn(definition.Slot) == definition) Remove(definition.Slot);
            else Wear(definition);
        }
    }
}
