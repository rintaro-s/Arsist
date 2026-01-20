// ==============================================
// Arsist Engine - Unity Build Pipeline
// UnityProject/Assets/Arsist/Editor/ArsistBuildPipeline.cs
// ==============================================

using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Arsist.Runtime.RemoteInput;

namespace Arsist.Builder
{
    /// <summary>
    /// Arsistエンジンからのビルドコマンドを処理するメインパイプライン
    /// </summary>
    public static class ArsistBuildPipeline
    {
        private static string _outputPath;
        private static string _targetDevice;
        private static bool _developmentBuild;
        private static JObject _manifest;

        /// <summary>
        /// CLI経由でビルドを実行（Arsistエンジンから呼び出される）
        /// </summary>
        public static void BuildFromCLI()
        {
            Debug.Log("[Arsist] Build pipeline started");

            // コマンドライン引数を解析
            ParseCommandLineArgs();

            // マニフェストを読み込み
            var manifestPath = Path.Combine(Application.dataPath, "ArsistGenerated", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError("[Arsist] manifest.json not found!");
                EditorApplication.Exit(1);
                return;
            }

            _manifest = JObject.Parse(File.ReadAllText(manifestPath));
            Debug.Log($"[Arsist] Building project: {_manifest["projectName"]}");

            // ランタイムでも参照できるよう Resources にコピー
            EnsureRuntimeManifestResource(_manifest);

            try
            {
                // Phase 1: シーン生成
                Debug.Log("[Arsist] Phase 1: Generating scenes...");
                GenerateScenes();

                // Phase 2: UI生成
                Debug.Log("[Arsist] Phase 2: Generating UI...");
                GenerateUI();

                // Phase 3: ビルド設定適用
                Debug.Log("[Arsist] Phase 3: Applying build settings...");
                ApplyBuildSettings(_manifest);

                // Phase 4: ビルド実行
                Debug.Log("[Arsist] Phase 4: Building APK...");
                ExecuteBuild(_manifest);

                Debug.Log("[Arsist] Build completed successfully!");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Arsist] Build failed: {e.Message}\n{e.StackTrace}");
                EditorApplication.Exit(1);
            }
        }

        private static void ParseCommandLineArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-outputPath":
                        _outputPath = args[++i];
                        break;
                    case "-targetDevice":
                        _targetDevice = args[++i];
                        break;
                    case "-developmentBuild":
                        _developmentBuild = args[++i].ToLower() == "true";
                        break;
                }
            }

            Debug.Log($"[Arsist] Output: {_outputPath}, Device: {_targetDevice}, Dev: {_developmentBuild}");
        }

        private static void GenerateScenes()
        {
            var scenesPath = Path.Combine(Application.dataPath, "ArsistGenerated", "scenes.json");
            if (!File.Exists(scenesPath)) return;

            var scenesJson = File.ReadAllText(scenesPath);
            var scenes = JArray.Parse(scenesJson);

            foreach (JObject scene in scenes)
            {
                var sceneName = scene["name"]?.ToString() ?? "MainScene";
                Debug.Log($"[Arsist] Processing scene: {sceneName}");

                // 新しいシーンを作成
                var newScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single
                );

                // オブジェクトを生成
                var objects = scene["objects"] as JArray;
                if (objects != null)
                {
                    foreach (JObject obj in objects)
                    {
                        CreateGameObject(obj);
                    }
                }

                // XR Origin を追加（デバイスに応じたプレハブを使用）
                CreateXROrigin();

                // Remote Input（UDP/TCP）を追加
                EnsureRemoteInputInScene(_manifest);

                // ランタイム基盤コンポーネントを追加
                CreateRuntimeSystems(_manifest);

                // シーンを保存
                var scenePath = $"Assets/Scenes/{sceneName}.unity";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", scenePath)));
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(newScene, scenePath);
                AssetDatabase.Refresh();
            }
        }

        private static void CreateGameObject(JObject objData)
        {
            var name = objData["name"]?.ToString() ?? "GameObject";
            var type = objData["type"]?.ToString() ?? "empty";
            var modelPath = objData["modelPath"]?.ToString();

            GameObject go = null;

            // モデル読み込み（GLB/GLTF）
            if (type == "model" && !string.IsNullOrEmpty(modelPath))
            {
                go = CreateModelGameObject(name, modelPath);
            }
            // プリミティブ作成
            else if (type == "primitive")
            {
                var primitiveType = objData["primitiveType"]?.ToString() ?? "cube";
                PrimitiveType pType = primitiveType switch
                {
                    "cube" => PrimitiveType.Cube,
                    "sphere" => PrimitiveType.Sphere,
                    "plane" => PrimitiveType.Plane,
                    "cylinder" => PrimitiveType.Cylinder,
                    "capsule" => PrimitiveType.Capsule,
                    _ => PrimitiveType.Cube
                };
                go = GameObject.CreatePrimitive(pType);
            }
            else if (type == "light")
            {
                go = new GameObject(name);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
            }
            else
            {
                go = new GameObject(name);
            }

            go.name = name;

            // Transform適用
            var transform = objData["transform"] as JObject;
            if (transform != null)
            {
                var pos = transform["position"] as JObject;
                var rot = transform["rotation"] as JObject;
                var scale = transform["scale"] as JObject;

                if (pos != null)
                    go.transform.position = new Vector3(
                        pos["x"]?.Value<float>() ?? 0,
                        pos["y"]?.Value<float>() ?? 0,
                        pos["z"]?.Value<float>() ?? 0
                    );

                if (rot != null)
                    go.transform.eulerAngles = new Vector3(
                        rot["x"]?.Value<float>() ?? 0,
                        rot["y"]?.Value<float>() ?? 0,
                        rot["z"]?.Value<float>() ?? 0
                    );

                if (scale != null)
                    go.transform.localScale = new Vector3(
                        scale["x"]?.Value<float>() ?? 1,
                        scale["y"]?.Value<float>() ?? 1,
                        scale["z"]?.Value<float>() ?? 1
                    );
            }

            // マテリアル適用
            var material = objData["material"] as JObject;
            if (material != null && go.TryGetComponent<Renderer>(out var renderer))
            {
                var mat = new Material(Shader.Find("Standard"));
                
                var colorHex = material["color"]?.ToString() ?? "#FFFFFF";
                if (ColorUtility.TryParseHtmlString(colorHex, out Color color))
                {
                    mat.color = color;
                }
                
                mat.SetFloat("_Metallic", material["metallic"]?.Value<float>() ?? 0);
                mat.SetFloat("_Glossiness", 1 - (material["roughness"]?.Value<float>() ?? 0.5f));
                
                renderer.material = mat;
            }
        }

        private static void EnsureRuntimeManifestResource(JObject manifest)
        {
            try
            {
                var resourcesDir = Path.Combine(Application.dataPath, "Resources");
                Directory.CreateDirectory(resourcesDir);
                var outPath = Path.Combine(resourcesDir, "ArsistManifest.json");
                File.WriteAllText(outPath, manifest.ToString(Formatting.Indented));
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to write runtime manifest resource: {e.Message}");
            }
        }

        private static void EnsureRemoteInputInScene(JObject manifest)
        {
            try
            {
                var remoteInput = manifest["remoteInput"] as JObject;
                if (remoteInput == null) return;

                var udpEnabled = remoteInput.SelectToken("udp.enabled")?.Value<bool>() ?? false;
                var tcpEnabled = remoteInput.SelectToken("tcp.enabled")?.Value<bool>() ?? false;
                if (!udpEnabled && !tcpEnabled) return;

                var go = GameObject.Find("ArsistRemoteInput");
                if (go == null) go = new GameObject("ArsistRemoteInput");

                if (go.GetComponent<ArsistRemoteInputBehaviour>() == null)
                {
                    go.AddComponent<ArsistRemoteInputBehaviour>();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to ensure remote input in scene: {e.Message}");
            }
        }

        private static void CreateXROrigin()
        {
            // ===== XREAL Rig (required for XREAL One) =====
            // Desired hierarchy:
            // XREAL_Rig
            //  ├── XR Origin
            //  │    └── Camera Offset
            //  │         └── Main Camera
            //  ├── AR Session
            //  └── XREAL Session Config

            bool isXreal = !string.IsNullOrEmpty(_targetDevice) && _targetDevice.ToLower().Contains("xreal");

            GameObject rigRoot = null;
            if (isXreal)
            {
                rigRoot = new GameObject("XREAL_Rig");
            }

            // XR Origin プレハブを探してインスタンス化（将来: アダプター側prefabに差し替え）
            GameObject xrOrigin = null;
            var xrOriginPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Arsist/Prefabs/XROrigin.prefab");
            if (xrOriginPrefab != null)
            {
                xrOrigin = (GameObject)PrefabUtility.InstantiatePrefab(xrOriginPrefab);
                xrOrigin.name = "XR Origin";
                xrOrigin.transform.position = Vector3.zero;
            }
            else
            {
                xrOrigin = new GameObject("XR Origin");
                xrOrigin.transform.position = Vector3.zero;

                var cameraOffset = new GameObject("Camera Offset");
                cameraOffset.transform.SetParent(xrOrigin.transform);
                cameraOffset.transform.localPosition = Vector3.zero;

                var mainCamera = new GameObject("Main Camera");
                mainCamera.tag = "MainCamera";
                mainCamera.transform.SetParent(cameraOffset.transform);
                mainCamera.transform.localPosition = Vector3.zero;
                mainCamera.transform.localRotation = Quaternion.identity;
                mainCamera.AddComponent<Camera>();
                mainCamera.AddComponent<AudioListener>();

                // Best-effort: TrackedPoseDriver (Input System or Legacy)
                TryAddComponentByTypeName(mainCamera, "UnityEngine.InputSystem.XR.TrackedPoseDriver");
                TryAddComponentByTypeName(mainCamera, "UnityEngine.SpatialTracking.TrackedPoseDriver");
            }

            if (rigRoot != null)
            {
                xrOrigin.transform.SetParent(rigRoot.transform);
            }

            // Best-effort: XR Origin component (Core Utils)
            TryAddComponentByTypeName(xrOrigin, "Unity.XR.CoreUtils.XROrigin");
            TryAddComponentByTypeName(xrOrigin, "UnityEngine.XR.Interaction.Toolkit.XROrigin");

            // Add Arsist runtime setup (exists in this project)
            var setupType = Type.GetType("Arsist.Runtime.XROriginSetup, Assembly-CSharp");
            if (setupType != null && xrOrigin.GetComponent(setupType) == null)
            {
                xrOrigin.AddComponent(setupType);
            }

            // AR Session (AR Foundation)
            if (rigRoot != null)
            {
                var arSessionGO = new GameObject("AR Session");
                arSessionGO.transform.SetParent(rigRoot.transform);
                TryAddComponentByTypeName(arSessionGO, "UnityEngine.XR.ARFoundation.ARSession");

                var xrealConfigGO = new GameObject("XREAL Session Config");
                xrealConfigGO.transform.SetParent(rigRoot.transform);
                // SDK固有型は不明なため、名前候補でbest-effort追加
                TryAddComponentByTypeName(xrealConfigGO, "XREALSessionConfig");
                TryAddComponentByTypeName(xrealConfigGO, "XrealSessionConfig");
                TryAddComponentByTypeName(xrealConfigGO, "NRSessionConfig");
            }

            Debug.Log(isXreal ? "[Arsist] XREAL_Rig created" : "[Arsist] XR Origin created");
        }

        /// <summary>
        /// GLB/GLTFモデルをインポートしてGameObjectとして生成
        /// </summary>
        private static GameObject CreateModelGameObject(string name, string modelPath)
        {
            // modelPath: "Assets/Models/xxx.glb" または相対パス
            // ArsistProjectAssets からコピーされたアセットを探す
            var possiblePaths = new[]
            {
                modelPath,
                $"Assets/ArsistProjectAssets/{modelPath}",
                $"Assets/ArsistProjectAssets/Models/{Path.GetFileName(modelPath)}",
                $"Assets/Models/{Path.GetFileName(modelPath)}"
            };

            string foundAssetPath = null;
            foreach (var p in possiblePaths)
            {
                var fullPath = Path.Combine(Application.dataPath, "..", p);
                if (File.Exists(fullPath))
                {
                    foundAssetPath = p;
                    break;
                }
            }

            if (string.IsNullOrEmpty(foundAssetPath))
            {
                Debug.LogWarning($"[Arsist] Model not found: {modelPath}. Creating empty placeholder.");
                var placeholder = new GameObject(name);
                placeholder.AddComponent<MeshRenderer>();
                return placeholder;
            }

            // Unityのインポートを待機
            AssetDatabase.ImportAsset(foundAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            // GLB/GLTFを読み込み（gltfastはランタイム向けなので、Editorではプレハブとしてインスタンス化）
            var loadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(foundAssetPath);
            if (loadedPrefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(loadedPrefab);
                instance.name = name;
                Debug.Log($"[Arsist] Model loaded: {name} from {foundAssetPath}");
                return instance;
            }

            // 失敗時: glTFast RuntimeLoaderを使う準備（ランタイムで動的読み込み）
            Debug.LogWarning($"[Arsist] Could not load model as prefab: {foundAssetPath}. Adding runtime loader.");
            var go = new GameObject(name);
            var runtimeLoader = go.AddComponent<ArsistModelRuntimeLoader>();
            runtimeLoader.modelPath = foundAssetPath;
            return go;
        }

        private static Component TryAddComponentByTypeName(GameObject go, string fullTypeName)
        {
            try
            {
                var t = FindType(fullTypeName);
                if (t == null) return null;
                if (go.GetComponent(t) != null) return go.GetComponent(t);
                return go.AddComponent(t);
            }
            catch
            {
                return null;
            }
        }

        private static Type FindType(string fullTypeName)
        {
            // Fast path
            var t = Type.GetType(fullTypeName);
            if (t != null) return t;

            // Search loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(fullTypeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// ランタイム基盤システムを追加
        /// </summary>
        private static void CreateRuntimeSystems(JObject manifest)
        {
            // [ArsistRuntimeSystems] 親オブジェクト
            var systemsRoot = new GameObject("[ArsistRuntimeSystems]");
            
            // DataManager（永続データ）
            TryAddComponentByTypeName(systemsRoot, "Arsist.Runtime.Data.ArsistDataManager");
            
            // EventBus（イベント通信）
            TryAddComponentByTypeName(systemsRoot, "Arsist.Runtime.Events.ArsistEventBus");
            
            // AudioManager（サウンド）
            TryAddComponentByTypeName(systemsRoot, "Arsist.Runtime.Audio.ArsistAudioManager");
            
            // SceneManager（シーン遷移）
            TryAddComponentByTypeName(systemsRoot, "Arsist.Runtime.Scene.ArsistSceneManager");
            
            // GazeInput（視線入力）- メインカメラに追加
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                TryAddComponentByTypeName(mainCam.gameObject, "Arsist.Runtime.Input.ArsistGazeInput");
            }

            Debug.Log("[Arsist] Runtime systems created");
        }

        private static void GenerateUI()
        {
            var uiPath = Path.Combine(Application.dataPath, "ArsistGenerated", "ui_layouts.json");
            if (!File.Exists(uiPath)) return;

            var uiJson = File.ReadAllText(uiPath);
            var layouts = JArray.Parse(uiJson);

            foreach (JObject layout in layouts)
            {
                var layoutName = layout["name"]?.ToString() ?? "MainUI";
                Debug.Log($"[Arsist] Processing UI layout: {layoutName}");

                // Canvas作成
                var canvasGO = new GameObject($"Canvas_{layoutName}");
                var canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                
                var canvasScaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                canvasScaler.dynamicPixelsPerUnit = 100;
                
                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                // Canvas のサイズ設定（XREAL One: 1920x1080）
                var rectTransform = canvasGO.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(1920, 1080);
                rectTransform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

                // 3DoF/Head-lockedの「最初にどこを見ているか」を決める
                var trackingMode = _manifest?["arSettings"]?["trackingMode"]?.ToString() ?? "6dof";
                var presentationMode = _manifest?["arSettings"]?["presentationMode"]?.ToString() ?? "world_anchored";
                var distance = _manifest?["arSettings"]?["floatingScreen"]?["distance"]?.Value<float>() ?? 2f;

                var mainCam = Camera.main;
                if (mainCam != null && (trackingMode == "3dof" || presentationMode == "head_locked_hud" || presentationMode == "floating_screen"))
                {
                    // カメラ前方に配置（3DoF必須）
                    rectTransform.position = mainCam.transform.position + mainCam.transform.forward * distance;
                    rectTransform.rotation = Quaternion.LookRotation(rectTransform.position - mainCam.transform.position);
                }
                else
                {
                    rectTransform.position = new Vector3(0, 1.5f, 3f);
                }

                // UIエレメントを生成
                var root = layout["root"] as JObject;
                if (root != null)
                {
                    CreateUIElement(root, canvasGO.transform);
                }
            }
        }

        private static void CreateUIElement(JObject elementData, Transform parent)
        {
            var type = elementData["type"]?.ToString() ?? "Panel";
            var go = new GameObject(type);
            go.transform.SetParent(parent, false);

            var rectTransform = go.AddComponent<RectTransform>();
            
            // スタイル適用
            var style = elementData["style"] as JObject;
            
            switch (type)
            {
                case "Panel":
                    var image = go.AddComponent<UnityEngine.UI.Image>();
                    if (style != null)
                    {
                        var bgColor = style["backgroundColor"]?.ToString();
                        if (!string.IsNullOrEmpty(bgColor) && ColorUtility.TryParseHtmlString(bgColor, out Color color))
                        {
                            image.color = color;
                        }
                    }
                    break;
                    
                case "Text":
                    var text = go.AddComponent<TMPro.TextMeshProUGUI>();
                    text.text = elementData["content"]?.ToString() ?? "Text";
                    if (style != null)
                    {
                        text.fontSize = style["fontSize"]?.Value<float>() ?? 24;
                        var textColor = style["color"]?.ToString();
                        if (!string.IsNullOrEmpty(textColor) && ColorUtility.TryParseHtmlString(textColor, out Color tColor))
                        {
                            text.color = tColor;
                        }
                    }
                    break;
                    
                case "Button":
                    var buttonImage = go.AddComponent<UnityEngine.UI.Image>();
                    buttonImage.color = new Color(0.91f, 0.27f, 0.38f, 1f); // #E94560
                    var button = go.AddComponent<UnityEngine.UI.Button>();
                    
                    // Gaze対応: Colliderを追加して視線入力を受け付ける
                    go.AddComponent<BoxCollider>();
                    TryAddComponentByTypeName(go, "Arsist.Runtime.Input.ArsistGazeTarget");
                    
                    // Button text
                    var buttonTextGO = new GameObject("Text");
                    buttonTextGO.transform.SetParent(go.transform, false);
                    var buttonText = buttonTextGO.AddComponent<TMPro.TextMeshProUGUI>();
                    buttonText.text = elementData["content"]?.ToString() ?? "Button";
                    buttonText.alignment = TMPro.TextAlignmentOptions.Center;
                    buttonText.fontSize = 16;
                    var buttonTextRect = buttonTextGO.GetComponent<RectTransform>();
                    buttonTextRect.anchorMin = Vector2.zero;
                    buttonTextRect.anchorMax = Vector2.one;
                    buttonTextRect.offsetMin = Vector2.zero;
                    buttonTextRect.offsetMax = Vector2.zero;
                    break;

                case "Image":
                    var uiImage = go.AddComponent<UnityEngine.UI.Image>();
                    uiImage.color = Color.white;
                    var assetPath = elementData["assetPath"]?.ToString();
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        // Arsistプロジェクトの相対パス (Assets/Textures/...) を Unity側 (Assets/ArsistProjectAssets/Textures/...) にマップ
                        var unityAssetPath = assetPath.StartsWith("Assets/")
                            ? "Assets/ArsistProjectAssets/" + assetPath.Substring("Assets/".Length)
                            : assetPath;

                        EnsureTextureIsSprite(unityAssetPath);
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(unityAssetPath);
                        if (sprite != null)
                        {
                            uiImage.sprite = sprite;
                            uiImage.preserveAspect = true;
                        }
                        else
                        {
                            Debug.LogWarning($"[Arsist] Sprite not found for Image: {unityAssetPath}");
                        }
                    }
                    break;
            }

            // Layout Group 設定
            var layout = elementData["layout"]?.ToString();
            if (layout == "FlexColumn")
            {
                var vlg = go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.spacing = style?["gap"]?.Value<float>() ?? 0;
            }
            else if (layout == "FlexRow")
            {
                var hlg = go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.spacing = style?["gap"]?.Value<float>() ?? 0;
            }

            // 子要素を再帰的に処理
            var children = elementData["children"] as JArray;
            if (children != null)
            {
                foreach (JObject child in children)
                {
                    CreateUIElement(child, go.transform);
                }
            }
        }

        private static void EnsureTextureIsSprite(string unityAssetPath)
        {
            var importer = AssetImporter.GetAtPath(unityAssetPath) as TextureImporter;
            if (importer == null) return;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        private static void ApplyBuildSettings(JObject manifest)
        {
            var build = manifest["build"] as JObject;
            if (build == null) return;

            // Android 設定
            PlayerSettings.productName = manifest["projectName"]?.ToString() ?? "ArsistApp";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, 
                build["packageName"]?.ToString() ?? "com.arsist.app");
            PlayerSettings.bundleVersion = build["version"]?.ToString() ?? "1.0.0";
            PlayerSettings.Android.bundleVersionCode = build["versionCode"]?.Value<int>() ?? 1;
            
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)(build["minSdkVersion"]?.Value<int>() ?? 29);
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)(build["targetSdkVersion"]?.Value<int>() ?? 34);
            
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            Debug.Log("[Arsist] Build settings applied");
        }

        private static void ExecuteBuild(JObject manifest)
        {
            var scenes = new List<string>();
            
            // ビルド対象シーンを収集
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                scenes.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            if (scenes.Count == 0)
            {
                throw new Exception("No scenes found to build");
            }

            var buildOptions = BuildOptions.None;
            if (_developmentBuild)
            {
                buildOptions |= BuildOptions.Development;
                buildOptions |= BuildOptions.AllowDebugging;
            }

            var outputFile = Path.Combine(_outputPath, $"{manifest["projectName"]}.apk");
            
            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outputFile,
                target = BuildTarget.Android,
                options = buildOptions
            };

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"Build failed: {report.summary.totalErrors} errors");
            }

            Debug.Log($"[Arsist] APK created: {outputFile}");
            Debug.Log($"[Arsist] Build size: {report.summary.totalSize / (1024 * 1024):F2} MB");
        }
    }
}
