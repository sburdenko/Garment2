using System.Collections.Generic;
using System.IO;
using System.Linq;
using Garment.Fitting;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// One step from "a model file landed in the project" to "the garment is in the app":
    /// creates its definition, builds URP materials, and adds it to the catalogue.
    /// </summary>
    public static class GarmentImporter
    {
        private const string CataloguePath = "Assets/Garment/Data/GarmentCatalogue.asset";
        private const string DataFolder = "Assets/Garment/Data";
        private static readonly string[] ModelExtensions = { ".fbx", ".obj", ".glb", ".gltf", ".dae" };

        [MenuItem("Garment/Import Garments")]
        public static void ImportAll()
        {
            var existing = ExistingSources();
            var imported = new List<GarmentDefinition>();

            foreach (var model in FindUnimportedModels(existing))
            {
                EnsureReadable(model);
                var definition = GarmentDefinitionFactory.CreateFor(model);
                if (definition == null) continue;
                GarmentMaterialSetup.BuildFor(definition);
                imported.Add(definition);
            }

            var catalogue = RefreshCatalogue();
            AssetDatabase.SaveAssets();

            if (imported.Count == 0)
            {
                Debug.Log($"Garment import: nothing new. Catalogue holds {catalogue.Garments.Count} garment(s).");
                return;
            }

            var names = string.Join(", ", imported.Select(d => $"{d.DisplayName} [{d.Slot}]"));
            Debug.Log($"Garment import: added {imported.Count} -> {names}. Catalogue holds {catalogue.Garments.Count}.");
        }

        /// <summary>
        /// Garment meshes are rebuilt at runtime — fitted, widened, reskinned — and writing to
        /// a mesh imported without Read/Write silently does nothing in play mode: the garment
        /// stays unfitted with no bone weights and renders collapsed at the rig root.
        /// </summary>
        private static void EnsureReadable(GameObject model)
        {
            string path = AssetDatabase.GetAssetPath(model);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.isReadable) return;

            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log($"Garment import: enabled Read/Write on {Path.GetFileName(path)}.");
        }

        public static GarmentCatalogue RefreshCatalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<GarmentCatalogue>(CataloguePath);
            if (catalogue == null)
            {
                catalogue = ScriptableObject.CreateInstance<GarmentCatalogue>();
                if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/Garment", "Data");
                AssetDatabase.CreateAsset(catalogue, CataloguePath);
            }

            var all = AssetDatabase.FindAssets("t:GarmentDefinition", new[] { DataFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GarmentDefinition>)
                .Where(d => d != null)
                .OrderBy(d => d.Slot)
                .ThenBy(d => d.DisplayName)
                .ToList();

            var serialized = new SerializedObject(catalogue);
            var list = serialized.FindProperty("garments");
            list.arraySize = all.Count;
            for (int i = 0; i < all.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return catalogue;
        }

        private static HashSet<GameObject> ExistingSources()
        {
            var sources = new HashSet<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:GarmentDefinition", new[] { DataFolder }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<GarmentDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null && definition.SourcePrefab != null) sources.Add(definition.SourcePrefab);
            }
            return sources;
        }

        private static IEnumerable<GameObject> FindUnimportedModels(HashSet<GameObject> existing)
        {
            // Folders holding prefabs the catalogue already references. A model whose folder
            // contains such a subfolder has been split into parts — the raw export is not a
            // wearable itself.
            var splitFolders = new HashSet<string>();
            foreach (var source in existing)
            {
                string sourcePath = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrEmpty(sourcePath))
                    splitFolders.Add(Path.GetDirectoryName(sourcePath).Replace('\\', '/'));
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // The mannequin lives in the Garment folder and is a body, not a wearable.
                if (path.StartsWith("Assets/Garment/")) continue;
                if (!ModelExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;

                string folder = Path.GetDirectoryName(path).Replace('\\', '/');
                bool alreadySplit = false;
                foreach (var splitFolder in splitFolders)
                    if (splitFolder.StartsWith(folder + "/")) { alreadySplit = true; break; }
                if (alreadySplit) continue;

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null || existing.Contains(model)) continue;
                yield return model;
            }
        }
    }
}
