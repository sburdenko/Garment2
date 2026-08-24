using Garment.Body;
using Garment.Fitting;
using Garment.EditorTools.Mannequin;
using Garment.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>Rebuilds the camera-free test scene from scratch so it stays reproducible.</summary>
    public static class SandboxSceneBuilder
    {
        private const string ScenePath = "Assets/Garment/Scenes/FittingRoom_Sandbox.unity";
        private const string MannequinPath = "Assets/Garment/Prefabs/Mannequin.prefab";
        private const string FloorMaterialPath = "Assets/Garment/Prefabs/SandboxFloor.mat";

        [MenuItem("Garment/Build Sandbox Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            var floor = CreateFloor();
            var mannequin = CreateMannequin();
            if (mannequin == null) return;

            var rig = mannequin.GetComponent<BodyRig>();
            var poseSource = mannequin.AddComponent<ProceduralPoseSource>();
            Assign(poseSource, "rig", rig);

            var wardrobe = CreateWardrobe(rig);
            var camera = CreateCamera(mannequin.transform);
            var sandbox = CreateSandbox(rig, wardrobe, poseSource);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"Sandbox scene built -> {ScenePath} (floor={floor.name}, camera={camera.name}, sandbox={sandbox.name})");
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(45f, -30f, 0f));
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.45f, 0.5f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.3f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.16f, 0.17f);
        }

        private static GameObject CreateFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());

            var material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "SandboxFloor" };
                material.SetColor("_BaseColor", new Color(0.22f, 0.23f, 0.25f));
                material.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(material, FloorMaterialPath);
            }
            floor.GetComponent<MeshRenderer>().sharedMaterial = material;
            return floor;
        }

        private static GameObject CreateMannequin()
        {
            // A real scanned body is preferred whenever one has been skinned onto the skeleton.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodySkinBaker.SkinnedBodyPath)
                      ?? AssetDatabase.LoadAssetAtPath<GameObject>(MannequinPath);
            if (prefab == null)
            {
                Debug.LogError($"Sandbox: no body prefab found. Run Garment/Generate Mannequin first.");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "Mannequin";
            return instance;
        }

        private static Wardrobe.Wardrobe CreateWardrobe(BodyRig rig)
        {
            var holder = new GameObject("Wardrobe");
            var wardrobe = holder.AddComponent<Wardrobe.Wardrobe>();
            Assign(wardrobe, "body", rig);

            Assign(wardrobe, "catalogue", GarmentImporter.RefreshCatalogue());
            return wardrobe;
        }

        private static GameObject CreateCamera(Transform target)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.05f;

            var orbit = cameraObject.AddComponent<OrbitCamera>();
            Assign(orbit, "target", target);
            return cameraObject;
        }

        private static GameObject CreateSandbox(BodyRig rig, Wardrobe.Wardrobe wardrobe, ProceduralPoseSource poseSource)
        {
            var sandbox = new GameObject("Sandbox");

            var probe = sandbox.AddComponent<ClippingProbe>();
            Assign(probe, "body", rig);

            var ui = sandbox.AddComponent<SandboxUI>();
            Assign(ui, "wardrobe", wardrobe);
            Assign(ui, "poseSource", poseSource);
            Assign(ui, "clippingProbe", probe);
            return sandbox;
        }

        private static void Assign(Object component, string field, Object value)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"{component.GetType().Name}: no serialized field '{field}'.");
                return;
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
