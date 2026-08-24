using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Garment.Fitting;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools
{
    public readonly struct SplitRule
    {
        public readonly string PartName;
        public readonly GarmentSlot? Slot;
        public readonly string[] Keywords;

        public SplitRule(string partName, GarmentSlot? slot, params string[] keywords)
        {
            PartName = partName;
            Slot = slot;
            Keywords = keywords;
        }

        public bool Matches(string materialName)
        {
            string lower = materialName.ToLowerInvariant();
            return Keywords.Any(lower.Contains);
        }
    }

    /// <summary>
    /// CLO3D exports a whole styled look — avatar, hair, every garment, shoes — as one mesh
    /// split only by material. This carves it back into separate wearables and a body.
    /// </summary>
    public static class ModelSplitter
    {
        /// <summary>Order matters: a submesh joins the first rule that claims it.</summary>
        private static readonly SplitRule[] DefaultRules =
        {
            new SplitRule("Body", null, "face", "body", "arm", "leg", "eye", "tooth", "eyelash", "skin", "nail"),
            new SplitRule("Hair", GarmentSlot.Hair, "hair"),
            new SplitRule("Footwear", GarmentSlot.Footwear, "shoe", "sneaker", "boot"),
            new SplitRule("Top", GarmentSlot.Top, "tmain", "trib", "tshirt", "shirt", "top"),
            new SplitRule("Bottom", GarmentSlot.Bottom, "denim", "jean", "pant", "trouser", "short", "skirt")
        };

        [MenuItem("Assets/Garment/Split By Material", true)]
        private static bool Validate() => Selection.activeObject is GameObject;

        [MenuItem("Assets/Garment/Split By Material")]
        public static void SplitSelection()
        {
            var model = Selection.activeObject as GameObject;
            if (model == null) return;

            var parts = Split(model, DefaultRules);
            if (parts.Count == 0) return;

            Debug.Log($"Split '{model.name}' into {parts.Count} part(s): {string.Join(", ", parts.Keys)}");
        }

        public static Dictionary<string, GameObject> Split(GameObject model, SplitRule[] rules)
        {
            var results = new Dictionary<string, GameObject>();

            var filter = model.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogError($"{model.name}: no mesh to split.");
                return results;
            }

            var source = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            var materials = renderer != null ? renderer.sharedMaterials : new Material[0];

            string modelPath = AssetDatabase.GetAssetPath(model);
            string folder = Path.Combine(Path.GetDirectoryName(modelPath), "Split").Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(modelPath), "Split");

            var claimed = new HashSet<int>();
            foreach (var rule in rules)
            {
                var submeshes = new List<int>();
                for (int i = 0; i < source.subMeshCount; i++)
                {
                    if (claimed.Contains(i)) continue;
                    string materialName = i < materials.Length && materials[i] != null ? materials[i].name : string.Empty;
                    if (!rule.Matches(materialName)) continue;
                    submeshes.Add(i);
                    claimed.Add(i);
                }
                if (submeshes.Count == 0) continue;

                var part = BuildPart(source, materials, submeshes, $"{model.name}_{rule.PartName}", folder);
                if (part == null) continue;

                results[rule.PartName] = part;
                if (rule.Slot.HasValue)
                {
                    // These garments were exported alongside the very avatar they were sewn on.
                    var definition = GarmentDefinitionFactory.CreateFor(part, rule.Slot.Value, GarmentFitMode.Native);
                    if (definition != null) GarmentMaterialSetup.BuildFor(definition);
                }
            }

            var unclaimed = Enumerable.Range(0, source.subMeshCount).Where(i => !claimed.Contains(i)).ToArray();
            if (unclaimed.Length > 0)
            {
                var names = unclaimed.Select(i => i < materials.Length && materials[i] != null ? materials[i].name : $"#{i}");
                Debug.LogWarning($"{model.name}: {unclaimed.Length} submesh(es) matched no rule and were dropped: {string.Join(", ", names)}");
            }

            GarmentImporter.RefreshCatalogue();
            AssetDatabase.SaveAssets();
            return results;
        }

        /// <summary>
        /// CLO writes UDIM coordinates: each texture tile lives at its own integer offset in UV
        /// space. Unity has no UDIM support and just repeats the texture, which reads as stripes.
        /// The tile number in the texture filename is authoritative — averaging the coordinates
        /// picks the wrong tile whenever a piece straddles a tile boundary.
        /// </summary>
        private static void NormaliseUdimTiles(List<Vector2> uv, List<int[]> triangleSets, Material[] submeshMaterials)
        {
            for (int set = 0; set < triangleSets.Count; set++)
            {
                var unique = new HashSet<int>(triangleSets[set]);
                if (unique.Count == 0) continue;

                var material = set < submeshMaterials.Length ? submeshMaterials[set] : null;
                if (!TryGetUdimTile(material, out var shift))
                    shift = MedianTile(uv, unique);

                if (shift == Vector2.zero) continue;
                foreach (int index in unique) uv[index] -= shift;
            }
        }

        private static bool TryGetUdimTile(Material material, out Vector2 shift)
        {
            shift = Vector2.zero;
            var texture = material != null && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            if (texture == null) return false;

            var match = Regex.Match(texture.name, @"_(\d{4})$");
            if (!match.Success) return false;

            int udim = int.Parse(match.Groups[1].Value);
            if (udim < 1001 || udim > 1999) return false;

            int offset = udim - 1001;
            shift = new Vector2(offset % 10, offset / 10);
            return true;
        }

        private static Vector2 MedianTile(List<Vector2> uv, HashSet<int> indices)
        {
            var tilesU = new List<float>(indices.Count);
            var tilesV = new List<float>(indices.Count);
            foreach (int index in indices)
            {
                tilesU.Add(Mathf.Floor(uv[index].x));
                tilesV.Add(Mathf.Floor(uv[index].y));
            }
            tilesU.Sort();
            tilesV.Sort();
            return new Vector2(tilesU[tilesU.Count / 2], tilesV[tilesV.Count / 2]);
        }

        private static GameObject BuildPart(Mesh source, Material[] materials, List<int> submeshes, string partName, string folder)
        {
            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            var sourceUv = source.uv;
            bool hasNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
            bool hasUv = sourceUv != null && sourceUv.Length == sourceVertices.Length;

            var remap = new Dictionary<int, int>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangleSets = new List<int[]>();

            foreach (int submesh in submeshes)
            {
                var indices = source.GetTriangles(submesh);
                var remapped = new int[indices.Length];
                for (int i = 0; i < indices.Length; i++)
                {
                    int original = indices[i];
                    if (!remap.TryGetValue(original, out int mapped))
                    {
                        mapped = vertices.Count;
                        remap[original] = mapped;
                        vertices.Add(sourceVertices[original]);
                        if (hasNormals) normals.Add(sourceNormals[original]);
                        if (hasUv) uv.Add(sourceUv[original]);
                    }
                    remapped[i] = mapped;
                }
                triangleSets.Add(remapped);
            }

            if (hasUv) NormaliseUdimTiles(uv, triangleSets, submeshes.Select(i => i < materials.Length ? materials[i] : null).ToArray());

            var mesh = new Mesh { name = partName };
            if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            if (hasNormals) mesh.SetNormals(normals);
            if (hasUv) mesh.SetUVs(0, uv);
            mesh.subMeshCount = triangleSets.Count;
            for (int i = 0; i < triangleSets.Count; i++) mesh.SetTriangles(triangleSets[i], i);
            if (!hasNormals) mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            string meshPath = $"{folder}/{partName}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var holder = new GameObject(partName);
            holder.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            holder.AddComponent<MeshRenderer>().sharedMaterials =
                submeshes.Select(i => i < materials.Length ? materials[i] : null).ToArray();

            string prefabPath = $"{folder}/{partName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(holder, prefabPath);
            Object.DestroyImmediate(holder);

            Debug.Log($"  {partName}: {vertices.Count} verts, {triangleSets.Count} submesh(es) -> {prefabPath}");
            return prefab;
        }
    }
}
