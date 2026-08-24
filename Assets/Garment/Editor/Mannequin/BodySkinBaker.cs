using Garment.Body;
using Garment.Fitting;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools.Mannequin
{
    /// <summary>
    /// Gives a static body scan the mannequin's skeleton. A CLO avatar exports as a bare mesh,
    /// but it is the exact body the garments were sewn on — worth far more as the demo body
    /// than the procedural stand-in, which only has to be good enough to carry the bones.
    /// </summary>
    public static class BodySkinBaker
    {
        private const string DonorPath = "Assets/Garment/Prefabs/Mannequin.prefab";
        private const string OutputFolder = "Assets/Garment/Prefabs";
        public const string SkinnedBodyPath = OutputFolder + "/Mannequin_Skinned.prefab";

        [MenuItem("Assets/Garment/Skin As Body", true)]
        private static bool Validate() => Selection.activeObject is GameObject;

        [MenuItem("Assets/Garment/Skin As Body")]
        public static void SkinSelection()
        {
            Bake(Selection.activeObject as GameObject);
        }

        public static GameObject Bake(GameObject bodyModel)
        {
            if (bodyModel == null) return null;

            var donorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPath);
            if (donorPrefab == null)
            {
                Debug.LogError($"Skin As Body: donor skeleton {DonorPath} not found. Run Garment/Generate Mannequin first.");
                return null;
            }

            if (!TryGetMesh(bodyModel, out var sourceMesh, out var materials))
            {
                Debug.LogError($"{bodyModel.name}: no mesh to skin.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(donorPrefab);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = $"Mannequin_{bodyModel.name}";

                var rig = instance.GetComponent<BodyRig>();
                var index = BodySkinIndex.From(rig);
                if (index == null) return null;

                var mesh = Object.Instantiate(sourceMesh);
                mesh.name = $"{bodyModel.name}_Skinned";
                mesh.boneWeights = SkinWeightTransfer.Transfer(
                    mesh.vertices, mesh, null, index.Vertices, index.Weights, index.Grid);
                mesh.bindposes = index.Bindposes;
                mesh.RecalculateBounds();

                string meshPath = $"{OutputFolder}/{mesh.name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);

                var renderer = rig.BodyMesh;
                renderer.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                renderer.sharedMaterials = materials;
                renderer.name = "Body";

                var saved = PrefabUtility.SaveAsPrefabAsset(instance, SkinnedBodyPath);
                AssetDatabase.SaveAssets();

                Debug.Log($"Skinned '{bodyModel.name}' ({mesh.vertexCount} verts, {mesh.subMeshCount} submeshes) " +
                          $"onto {index.Bones.Length} bones -> {SkinnedBodyPath}");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static bool TryGetMesh(GameObject model, out Mesh mesh, out Material[] materials)
        {
            mesh = null;
            materials = null;

            var filter = model.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                materials = renderer != null ? renderer.sharedMaterials : new Material[mesh.subMeshCount];
                return true;
            }

            var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned == null || skinned.sharedMesh == null) return false;

            mesh = skinned.sharedMesh;
            materials = skinned.sharedMaterials;
            return true;
        }
    }
}
