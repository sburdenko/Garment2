using System.Collections.Generic;
using Garment.Body;
using UnityEditor;
using UnityEngine;

namespace Garment.EditorTools.Mannequin
{
    /// <summary>
    /// Generates a Humanoid stand-in body so fitting work is not blocked on a licensed
    /// character. Swapping in a Mixamo rig later means replacing the prefab, nothing else.
    /// </summary>
    public static class MannequinGenerator
    {
        private const string PrefabPath = "Assets/Garment/Prefabs/Mannequin.prefab";
        private const string MeshPath = "Assets/Garment/Prefabs/MannequinBody.asset";
        private const string AvatarPath = "Assets/Garment/Prefabs/MannequinAvatar.asset";
        private const string MaterialPath = "Assets/Garment/Prefabs/MannequinSkin.mat";

        [MenuItem("Garment/Generate Mannequin")]
        public static void Generate()
        {
            var root = new GameObject("Mannequin");
            try
            {
                var bones = BuildSkeleton(root.transform);
                var boneArray = OrderedBones(bones);

                var mesh = MannequinMeshBuilder.Build(root.transform, boneArray, MannequinProportions.Limbs);
                var avatar = BuildAvatar(root, bones);
                if (!avatar.isValid)
                {
                    Debug.LogError("Mannequin: generated avatar is invalid; check bone naming and T-pose.");
                    Object.DestroyImmediate(root);
                    return;
                }

                SaveAsset(mesh, MeshPath);
                SaveAsset(avatar, AvatarPath);
                var material = LoadOrCreateSkinMaterial();

                var meshHolder = new GameObject("Body");
                meshHolder.transform.SetParent(root.transform, false);
                var renderer = meshHolder.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                renderer.bones = boneArray;
                renderer.rootBone = bones[MannequinProportions.RootBone];
                renderer.sharedMaterial = material;
                renderer.updateWhenOffscreen = true;

                var animator = root.AddComponent<Animator>();
                animator.avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
                animator.applyRootMotion = false;

                var rig = root.AddComponent<HumanoidBodyRig>();
                var serialized = new SerializedObject(rig);
                serialized.FindProperty("bodyMesh").objectReferenceValue = renderer;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Mannequin: generated {boneArray.Length} bones, {renderer.sharedMesh.vertexCount} verts -> {PrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Dictionary<string, Transform> BuildSkeleton(Transform root)
        {
            var created = new Dictionary<string, Transform>(MannequinProportions.Bones.Length);
            foreach (var spec in MannequinProportions.Bones)
            {
                var bone = new GameObject(spec.Name).transform;
                bone.SetParent(spec.Parent == null ? root : created[spec.Parent], false);
                bone.position = root.TransformPoint(spec.Position);
                bone.rotation = root.rotation;
                created[spec.Name] = bone;
            }
            return created;
        }

        private static Transform[] OrderedBones(Dictionary<string, Transform> bones)
        {
            var ordered = new Transform[MannequinProportions.Bones.Length];
            for (int i = 0; i < MannequinProportions.Bones.Length; i++)
                ordered[i] = bones[MannequinProportions.Bones[i].Name];
            return ordered;
        }

        private static Avatar BuildAvatar(GameObject root, Dictionary<string, Transform> bones)
        {
            var human = new List<HumanBone>();
            foreach (var spec in MannequinProportions.Bones)
            {
                if (!spec.IsHumanBone) continue;
                human.Add(new HumanBone
                {
                    boneName = spec.Name,
                    humanName = HumanTrait.BoneName[(int)spec.Human],
                    limit = new HumanLimit { useDefaultValues = true }
                });
            }

            var skeleton = new List<SkeletonBone>
            {
                new SkeletonBone
                {
                    name = root.name,
                    position = Vector3.zero,
                    rotation = Quaternion.identity,
                    scale = Vector3.one
                }
            };
            foreach (var spec in MannequinProportions.Bones)
            {
                var bone = bones[spec.Name];
                skeleton.Add(new SkeletonBone
                {
                    name = spec.Name,
                    position = bone.localPosition,
                    rotation = bone.localRotation,
                    scale = bone.localScale
                });
            }

            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };

            var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            avatar.name = "MannequinAvatar";
            return avatar;
        }

        private static void SaveAsset(Object asset, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        private static Material LoadOrCreateSkinMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("Mannequin: URP Lit shader not found.");
                return null;
            }
            var material = new Material(shader) { name = "MannequinSkin" };
            material.SetColor("_BaseColor", new Color(0.72f, 0.70f, 0.68f));
            material.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
