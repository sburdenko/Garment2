using System.IO;
using System.Linq;
using Garment.Fitting;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>Creates a GarmentDefinition from a selected model, guessing slot and submesh roles.</summary>
    public static class GarmentDefinitionFactory
    {
        private const string DataFolder = "Assets/Garment/Garments";

        private static readonly string[] RigidKeywords =
            { "zipper", "button", "stopper", "slider", "puller", "rivet", "buckle", "snap", "eyelet", "hook" };

        private static readonly string[] BottomKeywords =
            { "jean", "trouser", "pant", "short", "skirt", "legging", "chino" };

        private static readonly string[] OuterKeywords =
            { "jacket", "coat", "blazer", "hoodie", "cardigan", "parka", "puffer" };

        [MenuItem("Assets/Garment/Create Definition", true)]
        private static bool ValidateCreate() => Selection.activeObject is GameObject;

        [MenuItem("Assets/Garment/Create Definition")]
        public static void Create()
        {
            var definition = CreateFor(Selection.activeObject as GameObject);
            if (definition != null) Selection.activeObject = definition;
        }

        public static GarmentDefinition CreateFor(GameObject model, GarmentSlot? slotOverride = null, GarmentFitMode fitMode = GarmentFitMode.AutoFit)
        {
            if (model == null) return null;

            var renderer = model.GetComponentInChildren<Renderer>();
            var mesh = MeshOf(model);
            if (mesh == null)
            {
                Debug.LogError($"{model.name}: no mesh found.");
                return null;
            }

            var definition = ScriptableObject.CreateInstance<GarmentDefinition>();
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("displayName").stringValue = PrettyName(model.name);
            serialized.FindProperty("sourcePrefab").objectReferenceValue = model;
            serialized.FindProperty("slot").enumValueIndex = (int)(slotOverride ?? GuessSlot(model.name));
            serialized.FindProperty("fitMode").enumValueIndex = (int)fitMode;

            var roles = serialized.FindProperty("submeshRoles");
            roles.arraySize = mesh.subMeshCount;
            var materials = renderer != null ? renderer.sharedMaterials : new Material[0];
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                string materialName = i < materials.Length && materials[i] != null ? materials[i].name : string.Empty;
                roles.GetArrayElementAtIndex(i).enumValueIndex = (int)GuessRole(materialName);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (!AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.CreateFolder("Assets/Garment", "Garments");

            string path = AssetDatabase.GenerateUniqueAssetPath($"{DataFolder}/{SafeName(model.name)}.asset");
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();

            int rigid = Enumerable.Range(0, mesh.subMeshCount).Count(i => definition.RoleOf(i) == SubmeshRole.Rigid);
            Debug.Log($"Garment definition '{definition.DisplayName}' -> {path} (slot {definition.Slot}, {definition.FitMode}, {mesh.subMeshCount} submeshes, {rigid} rigid)");
            return definition;
        }

        private static Mesh MeshOf(GameObject model)
        {
            var filter = model.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) return filter.sharedMesh;
            var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
            return skinned != null ? skinned.sharedMesh : null;
        }

        private static GarmentSlot GuessSlot(string name)
        {
            string lower = name.ToLowerInvariant();
            if (OuterKeywords.Any(lower.Contains)) return GarmentSlot.Outer;
            if (BottomKeywords.Any(lower.Contains)) return GarmentSlot.Bottom;
            return GarmentSlot.Top;
        }

        private static SubmeshRole GuessRole(string materialName)
        {
            string lower = materialName.ToLowerInvariant();
            return RigidKeywords.Any(lower.Contains) ? SubmeshRole.Rigid : SubmeshRole.Fabric;
        }

        private static string PrettyName(string name) => name.Replace("_fbx_thick", string.Empty).Replace('_', ' ').Trim();

        private static string SafeName(string name) => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    }
}
