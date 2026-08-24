using System.IO;
using System.Linq;
using Garment.Fitting;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// Builds URP materials from the texture set a CLO3D export ships with and wires them into
    /// a GarmentDefinition, so the FBX's own embedded materials never need editing.
    /// </summary>
    public static class GarmentMaterialSetup
    {
        private const string MaterialFolder = "Assets/Garment/Materials";

        [MenuItem("Assets/Garment/Build Materials", true)]
        private static bool Validate() => Selection.activeObject is GarmentDefinition;

        [MenuItem("Assets/Garment/Build Materials")]
        public static void Build()
        {
            BuildFor(Selection.activeObject as GarmentDefinition);
        }

        public static void BuildFor(GarmentDefinition definition)
        {
            if (definition == null || definition.SourcePrefab == null) return;

            if (AlreadyTextured(definition.SourcePrefab))
            {
                Debug.Log($"Materials for '{definition.DisplayName}': model ships its own textured materials, keeping them.");
                return;
            }

            string modelPath = AssetDatabase.GetAssetPath(definition.SourcePrefab);
            // Downloaded packages put textures in a sibling folder ("source" + "textures"), so
            // search from the package root, one level above the model.
            string folder = Path.GetDirectoryName(modelPath);
            string parent = Path.GetDirectoryName(folder);
            if (!string.IsNullOrEmpty(parent) && parent != "Assets") folder = parent;
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Garment", "Materials");

            var albedo = FindTexture(folder, "diffuse", "albedo", "basecolor");
            var normal = FindTexture(folder, "normal", "bump");
            ConfigureNormalMap(normal);

            var fabric = CreateFabric(definition.DisplayName, albedo, normal);
            var hardware = CreateHardware();

            var mesh = MeshOf(definition.SourcePrefab);
            if (mesh == null) return;

            var serialized = new SerializedObject(definition);
            var overrides = serialized.FindProperty("materialOverrides");
            overrides.arraySize = mesh.subMeshCount;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var material = definition.RoleOf(i) == SubmeshRole.Rigid ? hardware : fabric;
                overrides.GetArrayElementAtIndex(i).objectReferenceValue = material;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Debug.Log($"Materials for '{definition.DisplayName}': albedo={(albedo != null ? albedo.name : "none")}, " +
                      $"normal={(normal != null ? normal.name : "none")}, {mesh.subMeshCount} submeshes assigned.");
        }

        /// <summary>
        /// CLO exports carry a usable .mtl; rebuilding materials from loose texture files would
        /// pick the wrong map when one atlas serves several garments.
        /// </summary>
        private static bool AlreadyTextured(GameObject model)
        {
            var renderer = model.GetComponentInChildren<Renderer>();
            if (renderer == null) return false;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) return false;
                if (!material.HasProperty("_BaseMap") || material.GetTexture("_BaseMap") == null) return false;
            }
            return true;
        }

        private static Material CreateFabric(string displayName, Texture2D albedo, Texture2D normal)
        {
            string path = $"{MaterialFolder}/{Sanitize(displayName)}_Fabric.mat";
            var material = LoadOrCreate(path);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.22f);
            if (albedo != null) material.SetTexture("_BaseMap", albedo);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateHardware()
        {
            string path = $"{MaterialFolder}/GarmentHardware.mat";
            var material = LoadOrCreate(path);
            material.SetColor("_BaseColor", new Color(0.62f, 0.63f, 0.66f));
            material.SetFloat("_Metallic", 0.9f);
            material.SetFloat("_Smoothness", 0.65f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreate(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = Path.GetFileNameWithoutExtension(path)
            };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Texture2D FindTexture(string folder, params string[] keywords)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (keywords.Any(lower.Contains))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        private static void ConfigureNormalMap(Texture2D normal)
        {
            if (normal == null) return;
            string path = AssetDatabase.GetAssetPath(normal);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        private static Mesh MeshOf(GameObject model)
        {
            var filter = model.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) return filter.sharedMesh;
            var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
            return skinned != null ? skinned.sharedMesh : null;
        }

        private static string Sanitize(string name) =>
            string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
    }
}
