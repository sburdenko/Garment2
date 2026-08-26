using Garment.Body;
using Garment.EditorTools.Mannequin;
using Garment.Fitting;
using Garment.Tracking;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Garment.EditorTools
{
    /// <summary>Builds the live-camera scene: your own feed with the tracked skeleton over it.</summary>
    public static class MirrorSceneBuilder
    {
        private const string ScenePath = "Assets/Garment/Scenes/FittingRoom_Mirror.unity";
        private const string LandmarkerPath = "Assets/Garment/Tracking/pose_landmarks_detector_full.onnx";
        private const string CataloguePath = "Assets/Garment/Garments/GarmentCatalogue.asset";

        [MenuItem("Garment/Build Mirror Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Load after the scene swap: creating a scene unloads assets nothing references yet,
            // which quietly turns a ModelAsset held from before into a null reference.
            var landmarker = AssetDatabase.LoadAssetAtPath<ModelAsset>(LandmarkerPath);
            if (landmarker == null)
            {
                Debug.LogError($"Mirror scene: {LandmarkerPath} not found.");
                return;
            }

            var cropShader = Shader.Find("Garment/RoiCrop");
            if (cropShader == null)
            {
                Debug.LogError("Mirror scene: Garment/RoiCrop shader not found.");
                return;
            }

            var camera = CreateCamera();
            var feed = new GameObject("Webcam").AddComponent<WebcamFeed>();

            // Test photos live together and are numbered to define their gallery order.
            var photo = new GameObject("Photo Source").AddComponent<PhotoFrameSource>();
            var photoSerialized = new SerializedObject(photo);
            var photosProperty = photoSerialized.FindProperty("photos");
            var found = FindPhotos();
            photosProperty.arraySize = found.Length;
            for (int i = 0; i < found.Length; i++)
                photosProperty.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            photoSerialized.ApplyModifiedPropertiesWithoutUndo();

            var display = CreateDisplay(camera, feed);

            var trackingObject = new GameObject("Tracking");
            var provider = trackingObject.AddComponent<WebcamPoseProvider>();
            Assign(provider, "feed", feed);
            Assign(provider, "landmarker", landmarker);
            Assign(provider, "cropShader", cropShader);

            var overlay = trackingObject.AddComponent<PoseDebugOverlay>();
            Assign(overlay, "provider", provider);

            // The display is built before the tracker exists, so it gets wired up here.
            Assign(display, "provider", provider);

            CreateAvatar(provider, camera);
            CreateLighting();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"Mirror scene built -> {ScenePath} (display={display.name}, camera={camera.name})");
        }

        private static void CreateAvatar(WebcamPoseProvider provider, Camera view)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodySkinBaker.SkinnedBodyPath);
            if (prefab == null)
            {
                Debug.LogWarning($"Mirror scene: {BodySkinBaker.SkinnedBodyPath} not found; scene has no avatar.");
                return;
            }

            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            avatar.name = "Avatar";

            var rig = avatar.GetComponent<BodyRig>();
            var source = avatar.AddComponent<TrackedPoseSource>();
            Assign(source, "rig", rig);
            Assign(source, "provider", provider);

            var alignment = avatar.AddComponent<PoseAlignment>();
            Assign(alignment, "rig", rig);
            Assign(alignment, "provider", provider);
            Assign(alignment, "view", view);

            var calibrator = avatar.AddComponent<BodyCalibrator>();
            Assign(calibrator, "rig", rig);
            Assign(calibrator, "provider", provider);

            // The point is to see yourself wearing the clothes, so the body itself stays hidden.
            if (rig.BodyMesh != null) rig.BodyMesh.enabled = false;

            var wardrobeObject = new GameObject("Wardrobe");
            var wardrobe = wardrobeObject.AddComponent<Wardrobe.Wardrobe>();
            Assign(wardrobe, "body", rig);
            Assign(wardrobe, "catalogue", AssetDatabase.LoadAssetAtPath<GarmentCatalogue>(CataloguePath));

            var gate = wardrobeObject.AddComponent<DressWhenTracked>();
            Assign(gate, "provider", provider);
            Assign(gate, "wardrobe", wardrobe);

            var ui = wardrobeObject.AddComponent<MirrorUI>();
            Assign(ui, "overlay", Object.FindFirstObjectByType<PoseDebugOverlay>());
            Assign(ui, "wardrobe", wardrobe);
            Assign(ui, "calibrator", calibrator);
            Assign(ui, "provider", provider);
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetPositionAndRotation(new Vector3(0f, 3f, -2f), Quaternion.Euler(35f, 160f, 0f));
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.52f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.38f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.22f);
        }

        private static Texture2D[] FindPhotos()
        {
            var photos = new System.Collections.Generic.List<Texture2D>();
            foreach (var guid in AssetDatabase.FindAssets("t:texture2D", new[] { "Assets/Garment/Tracking/ReferencePhotos" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) photos.Add(texture);
            }
            photos.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return photos.ToArray();
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
            camera.transform.position = new Vector3(0f, 1f, -3f);
            return camera;
        }

        private static WebcamDisplay CreateDisplay(Camera camera, FrameSource feed)
        {
            var canvasObject = new GameObject("Feed Canvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            // Sits far behind anything 3D, so the avatar always draws in front of the feed.
            canvas.planeDistance = 90f;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            var imageObject = new GameObject("Feed");
            imageObject.transform.SetParent(canvasObject.transform, false);

            var rect = imageObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            imageObject.AddComponent<RawImage>();
            imageObject.AddComponent<AspectRatioFitter>();

            var display = imageObject.AddComponent<WebcamDisplay>();
            Assign(display, "feed", feed);
            return display;
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

            if (value != null && property.objectReferenceValue == null)
                Debug.LogError($"{component.GetType().Name}: '{field}' did not take the assigned value.");
        }
    }
}
