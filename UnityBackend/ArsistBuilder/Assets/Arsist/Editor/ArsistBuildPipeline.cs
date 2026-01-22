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
using System.Reflection;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Arsist.Runtime.RemoteInput;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;
using UnityEngine.Rendering;

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
        private static BuildTarget _buildTarget = BuildTarget.Android;
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

                // Phase 2.5: HTMLベースUIをStreamingAssetsへ配置
                Debug.Log("[Arsist] Phase 2.5: Copying HTML UI to StreamingAssets...");
                CopyHtmlUiToStreamingAssets();

                // Phase 3: ビルド設定適用
                Debug.Log("[Arsist] Phase 3: Applying build settings...");
                ApplyBuildSettings(_manifest);

                // Phase 3.05: XR Plugin Management 設定を強制適用（XREAL SDK 3.1必須）
                Debug.Log("[Arsist] Phase 3.05: Forcing XR Plugin Management settings...");
                ForceXrPluginSettings(_targetDevice);

                // Phase 3.1: デバイス固有パッチ（Editorスクリプト）を実行
                Debug.Log("[Arsist] Phase 3.1: Applying device patches...");
                ApplyDevicePatches(_targetDevice);

                // Phase 3.2: ビルド前検証（ここで落とすことで“成功したけど動かない”を避ける）
                Debug.Log("[Arsist] Phase 3.2: Validating build readiness...");
                ValidateBuildReadiness(_targetDevice);

                // OpenXR は初回ロード直後だと Settings が未ロード扱いになり、BuildPlayer が失敗することがある。
                // Build 前に明示的にロードしておく。
                EnsureOpenXRPackageSettingsLoaded();
                EnsureOpenXRSettingsLoaded();

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
                    case "-buildTarget":
                        _buildTarget = ParseBuildTarget(args[++i]);
                        break;
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

            Debug.Log($"[Arsist] Target: {_buildTarget}, Output: {_outputPath}, Device: {_targetDevice}, Dev: {_developmentBuild}");
        }

        private static BuildTarget ParseBuildTarget(string raw)
        {
            var v = (raw ?? "").Trim().ToLowerInvariant();
            return v switch
            {
                "android" => BuildTarget.Android,
                "ios" => BuildTarget.iOS,
                "windows" => BuildTarget.StandaloneWindows64,
                "macos" => BuildTarget.StandaloneOSX,
                _ => BuildTarget.Android,
            };
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

                // demo.txt 準拠: すべての空間オブジェクトは WorldRoot 配下に配置し、
                // リセンターや座標補正は WorldRoot を動かす設計に寄せる。
                EnsureWorldRoot();

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

            // 先にワールド座標でTransformを適用し、その後WorldRoot配下へ移動（ワールド座標維持）

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

            // WorldRoot配下へ（ワールド座標は維持）
            var worldRoot = EnsureWorldRoot();
            if (worldRoot != null)
            {
                go.transform.SetParent(worldRoot.transform, true);
            }

            // マテリアル適用
            var material = objData["material"] as JObject;
            if (material != null && go.TryGetComponent<Renderer>(out var renderer))
            {
                var shader = FindSafeShader(new[]
                {
                    "Standard",
                    "Universal Render Pipeline/Lit",
                    "Universal Render Pipeline/Unlit",
                    "Unlit/Color",
                    "Sprites/Default",
                });

                if (shader == null)
                {
                    Debug.LogWarning("[Arsist] No compatible shader found for material. Skipping material setup.");
                    return;
                }

                var mat = new Material(shader);
                
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

        private static Shader FindSafeShader(IEnumerable<string> candidates)
        {
            foreach (var name in candidates)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var s = Shader.Find(name);
                if (s != null) return s;
            }
            return null;
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
            // ===== XREAL SDK 3.1 準拠のXR Origin作成 =====
            // Hierarchy:
            // XREAL_Rig
            //  ├── XR Origin
            //  │    └── Camera Offset
            //  │         └── Main Camera (Clear Flags: Solid Color, Background: Black RGBA(0,0,0,0))
            //  ├── AR Session

            bool isXreal = !string.IsNullOrEmpty(_targetDevice) && _targetDevice.ToLower().Contains("xreal");

            GameObject rigRoot = null;
            if (isXreal)
            {
                rigRoot = new GameObject("XREAL_Rig");
            }

            // XR Origin の作成（手動構築 - XREAL SDK 3.1要件）
            GameObject xrOrigin = new GameObject("XR Origin");
            xrOrigin.transform.position = Vector3.zero;

            // Camera Offset の作成
            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform);
            cameraOffset.transform.localPosition = Vector3.zero;
            cameraOffset.transform.localRotation = Quaternion.identity;

            // Main Camera の作成と設定（XREAL SDK 3.1 最重要設定）
            var mainCamera = new GameObject("Main Camera");
            mainCamera.tag = "MainCamera";
            mainCamera.transform.SetParent(cameraOffset.transform);
            mainCamera.transform.localPosition = Vector3.zero;
            mainCamera.transform.localRotation = Quaternion.identity;
            
            var camera = mainCamera.AddComponent<Camera>();
            // XREAL の透過設定：黒(RGB 0,0,0)を透明として扱う
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // Black with Alpha 0
            camera.nearClipPlane = 0.01f; // XR推奨値
            camera.farClipPlane = 1000f;
            
            mainCamera.AddComponent<AudioListener>();

            // Tracked Pose Driver の追加（Input System優先）
            var trackedPoseAdded = TryAddComponentByTypeName(mainCamera, "UnityEngine.InputSystem.XR.TrackedPoseDriver");
            if (!trackedPoseAdded)
            {
                TryAddComponentByTypeName(mainCamera, "UnityEngine.SpatialTracking.TrackedPoseDriver");
            }

            // AR Camera Background コンポーネントの削除（存在する場合）
            // これが残っていると現実が見えなくなる
            var arCameraBg = mainCamera.GetComponent(Type.GetType("UnityEngine.XR.ARFoundation.ARCameraBackground, Unity.XR.ARFoundation"));
            if (arCameraBg != null)
            {
                UnityEngine.Object.DestroyImmediate(arCameraBg);
                Debug.Log("[Arsist] Removed AR Camera Background component for XREAL transparency");
            }

            if (rigRoot != null)
            {
                xrOrigin.transform.SetParent(rigRoot.transform);
            }

            // XR Origin コンポーネントの追加
            var xrOriginAdded = TryAddComponentByTypeName(xrOrigin, "Unity.XR.CoreUtils.XROrigin");
            if (!xrOriginAdded)
            {
                xrOriginAdded = TryAddComponentByTypeName(xrOrigin, "UnityEngine.XR.Interaction.Toolkit.XROrigin");
            }

            if (xrOriginAdded)
            {
                // XROrigin の Camera Offset 参照を設定
                var xrOriginComp = xrOrigin.GetComponent("Unity.XR.CoreUtils.XROrigin") 
                    ?? xrOrigin.GetComponent("UnityEngine.XR.Interaction.Toolkit.XROrigin");
                if (xrOriginComp != null)
                {
                    var cameraOffsetProperty = xrOriginComp.GetType().GetProperty("CameraFloorOffsetObject");
                    if (cameraOffsetProperty != null && cameraOffsetProperty.CanWrite)
                    {
                        cameraOffsetProperty.SetValue(xrOriginComp, cameraOffset);
                    }
                }
            }

            // Arsist runtime setup の追加
            var setupType = Type.GetType("Arsist.Runtime.XROriginSetup, Assembly-CSharp");
            if (setupType != null && xrOrigin.GetComponent(setupType) == null)
            {
                xrOrigin.AddComponent(setupType);
            }

            // AR Session の作成（AR Foundation）
            if (rigRoot != null)
            {
                var arSessionGO = new GameObject("AR Session");
                arSessionGO.transform.SetParent(rigRoot.transform);
                arSessionGO.transform.localPosition = Vector3.zero;
                TryAddComponentByTypeName(arSessionGO, "UnityEngine.XR.ARFoundation.ARSession");
            }

            Debug.Log(isXreal 
                ? "[Arsist] XREAL_Rig created with XREAL SDK 3.1 compliant camera settings (Clear Flags: Solid Color, Black RGBA(0,0,0,0))" 
                : "[Arsist] XR Origin created");
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

            // GLB/GLTFはStreamingAssetsへコピーし、ランタイムでglTFast読み込みに切り替える
            var runtimePath = PrepareModelForRuntime(foundAssetPath, modelPath);
            var runtimeGo = new GameObject(name);
            if (!TryConfigureRuntimeModelLoader(runtimeGo, runtimePath))
            {
                Debug.LogWarning($"[Arsist] Runtime model loader not available. Creating placeholder for: {foundAssetPath}");
                runtimeGo.AddComponent<MeshRenderer>();
            }
            else
            {
                Debug.Log($"[Arsist] Model scheduled for runtime load: {runtimePath}");
            }
            return runtimeGo;
        }

        private static string PrepareModelForRuntime(string assetPath, string originalPath)
        {
            if (!string.IsNullOrWhiteSpace(originalPath) &&
                (originalPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 originalPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return originalPath;
            }

            var ext = Path.GetExtension(assetPath)?.ToLowerInvariant();
            // .gib はエンジン側のtypo/独自拡張子でGLBを指しているケースがあるためGLB扱いする
            if (ext != ".glb" && ext != ".gltf" && ext != ".gib")
            {
                return assetPath;
            }

            var streamingDir = Path.Combine(Application.dataPath, "StreamingAssets", "ArsistModels");
            Directory.CreateDirectory(streamingDir);

            var srcFull = Path.Combine(Application.dataPath, "..", assetPath);
            var fileName = Path.GetFileName(assetPath);
            // glTFastは拡張子依存の分岐が入るケースがあるため、.gib は .glb に正規化して配置する
            var destFileName = fileName;
            if (string.Equals(ext, ".gib", StringComparison.OrdinalIgnoreCase))
            {
                destFileName = Path.GetFileNameWithoutExtension(fileName) + ".glb";
            }

            var destFull = Path.Combine(streamingDir, destFileName);

            try
            {
                File.Copy(srcFull, destFull, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to copy model to StreamingAssets: {e.Message}");
                return assetPath;
            }

            var assetRelative = $"Assets/StreamingAssets/ArsistModels/{destFileName}";
            try
            {
                AssetDatabase.ImportAsset(assetRelative, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
            }
            catch { }

            return $"ArsistModels/{destFileName}";
        }

        private static bool TryConfigureRuntimeModelLoader(GameObject go, string runtimePath)
        {
            try
            {
                var comp = TryAddComponentByTypeName(go, "Arsist.Runtime.ArsistModelRuntimeLoader");
                if (comp == null) return false;

                var t = comp.GetType();
                var field = t.GetField("modelPath");
                if (field != null)
                {
                    field.SetValue(comp, runtimePath);
                }

                var destroyField = t.GetField("destroyAfterLoad");
                if (destroyField != null)
                {
                    destroyField.SetValue(comp, true);
                }

                return true;
            }
            catch
            {
                return false;
            }
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

            // HTML UI (WebView) - エンジン内で定義されたUIを表示
            var htmlUiPath = Path.Combine(Application.dataPath, "ArsistGenerated", "html_ui.html");
            if (File.Exists(htmlUiPath))
            {
                var webViewGO = new GameObject("[ArsistWebViewUI]");
                var webViewComp = TryAddComponentByTypeName(webViewGO, "Arsist.Runtime.UI.ArsistWebViewUI");
                if (webViewComp != null)
                {
                    // htmlPath, autoLoad を設定
                    var t = webViewComp.GetType();
                    var fieldPath = t.GetField("_htmlPath", BindingFlags.NonPublic | BindingFlags.Instance);
                    var fieldAutoLoad = t.GetField("_autoLoad", BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (fieldPath != null)
                    {
                        fieldPath.SetValue(webViewComp, "ArsistUI/index.html");
                    }
                    if (fieldAutoLoad != null)
                    {
                        fieldAutoLoad.SetValue(webViewComp, true);
                    }
                    
                    Debug.Log("[Arsist] WebView UI component added for HTML UI");
                }
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

        private static void CopyHtmlUiToStreamingAssets()
        {
            try
            {
                // Try multiple paths
                var possiblePaths = new[]
                {
                    Path.Combine(Application.dataPath, "ArsistGenerated", "html", "html_ui.html"),
                    Path.Combine(Application.dataPath, "ArsistGenerated", "html_ui.html"),
                };

                string htmlUiPath = null;
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        htmlUiPath = path;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(htmlUiPath))
                {
                    Debug.Log("[Arsist] html_ui.html not found in ArsistGenerated, skipping HTML UI copy");
                    return;
                }

                var streamingDir = Path.Combine(Application.dataPath, "StreamingAssets", "ArsistUI");
                Directory.CreateDirectory(streamingDir);

                var destPath = Path.Combine(streamingDir, "index.html");
                File.Copy(htmlUiPath, destPath, true);

                Debug.Log($"[Arsist] HTML UI copied to StreamingAssets: {destPath}");
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to copy HTML UI: {e.Message}");
            }
        }

        private static void ForceXrPluginSettings(string targetDevice)
        {
            var normalized = (targetDevice ?? "").ToLowerInvariant();
            var isXreal = normalized.Contains("xreal");

            if (!isXreal)
            {
                Debug.Log("[Arsist] Not XREAL device, skipping XR Plugin forced settings");
                return;
            }

            try
            {
                // XR General Settings を取得または作成
                var generalSettings = GetOrCreateXRGeneralSettings(BuildTargetGroup.Android);
                if (generalSettings == null)
                {
                    Debug.LogError("[Arsist] Failed to get/create XR General Settings for Android");
                    return;
                }

                generalSettings.InitManagerOnStart = true;

                var manager = generalSettings.Manager;
                if (manager == null)
                {
                    // XRManagerSettings を作成してアセットとして保存
                    var managerType = typeof(XRManagerSettings);
                    manager = ScriptableObject.CreateInstance(managerType) as XRManagerSettings;
                    if (manager != null)
                    {
                        // アセットとして保存
                        var xrDir = "Assets/XR/Settings";
                        Directory.CreateDirectory(xrDir);
                        var managerPath = $"{xrDir}/XRManagerSettings.asset";
                        AssetDatabase.CreateAsset(manager, managerPath);
                        
                        // GeneralSettingsに設定
                        var propManager = generalSettings.GetType().GetProperty("AssignedSettings", BindingFlags.Public | BindingFlags.Instance);
                        if (propManager == null)
                        {
                            // Unity 6では Manager プロパティを直接使う
                            propManager = generalSettings.GetType().GetProperty("Manager", BindingFlags.Public | BindingFlags.Instance);
                        }
                        
                        if (propManager != null && propManager.CanWrite)
                        {
                            propManager.SetValue(generalSettings, manager);
                            Debug.Log($"[Arsist] XRManagerSettings created and assigned: {managerPath}");
                        }
                        else
                        {
                            Debug.LogWarning("[Arsist] Could not find AssignedSettings or Manager property to set");
                        }
                    }
                }

                if (manager == null)
                {
                    Debug.LogError("[Arsist] Failed to get/create XRManagerSettings");
                    return;
                }

                // XREAL Loader を有効化
                EnsureXrealLoaderActive(manager);

                // XREAL Settings を作成・登録
                EnsureXrealSettings();

                // ★ XRGeneralSettings と XRManagerSettings を Preloaded Assets に追加
                AddToPreloadedAssets(generalSettings);
                AddToPreloadedAssets(manager);

                EditorUtility.SetDirty(generalSettings);
                EditorUtility.SetDirty(manager);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                Debug.Log("[Arsist] XR Plugin Management settings forced (XREAL Loader enabled)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Arsist] Failed to force XR Plugin settings: {e.Message}\n{e.StackTrace}");
            }
        }

        private static void EnsureXrealSettings()
        {
            try
            {
                // XREAL Settings 型を検索
                var xrealSettingsType = FindTypeInLoadedAssemblies("Unity.XR.XREAL.XREALSettings");
                if (xrealSettingsType == null)
                {
                    Debug.LogWarning("[Arsist] XREALSettings type not found. XREAL SDK may not be imported.");
                    return;
                }

                // 設定キーを取得
                string settingsKey = "com.unity.xr.management.xrealsettings";
                var fiKey = xrealSettingsType.GetField("k_SettingsKey", BindingFlags.Public | BindingFlags.Static);
                if (fiKey != null && fiKey.FieldType == typeof(string))
                {
                    var v = fiKey.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        settingsKey = v;
                    }
                }

                // 既存設定を確認
                UnityEngine.Object existing = null;
                if (EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(settingsKey, out existing) && existing != null)
                {
                    Debug.Log($"[Arsist] XREALSettings already registered (key: {settingsKey})");
                    // 既存でもPreloaded Assetsに追加されているか確認・追加
                    AddToPreloadedAssets(existing);
                    return;
                }

                // 新規作成
                var settings = ScriptableObject.CreateInstance(xrealSettingsType);
                if (settings == null)
                {
                    Debug.LogError("[Arsist] Failed to create XREALSettings instance");
                    return;
                }

                // デフォルト値を設定（XREAL SDK 3.1準拠）
                SetXrealSettingsDefaults(settings, xrealSettingsType);

                // アセットとして保存
                var assetPath = "Assets/XR/Settings/XREALSettings.asset";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", assetPath)));
                AssetDatabase.CreateAsset(settings, assetPath);
                AssetDatabase.SaveAssets();

                // EditorBuildSettings に登録
                EditorBuildSettings.AddConfigObject(settingsKey, settings, true);

                // ★ Preloaded Assets に追加（ランタイムでGetSettings()がnullにならないようにする）
                AddToPreloadedAssets(settings);

                Debug.Log($"[Arsist] XREALSettings created and registered: {assetPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Arsist] Failed to ensure XREAL Settings: {e.Message}\n{e.StackTrace}");
            }
        }

        private static void SetXrealSettingsDefaults(UnityEngine.Object settings, Type settingsType)
        {
            try
            {
                // ★ SupportDevices: REALITY & VISION を明示的に設定（XREAL One は VISION カテゴリ）
                // XREALDeviceCategory enum: INVALID=0, REALITY=1, VISION=2
                // SupportDevices は List<XREALDeviceCategory> 型
                var fiSupportDevices = settingsType.GetField("SupportDevices", BindingFlags.Public | BindingFlags.Instance);
                if (fiSupportDevices != null)
                {
                    // XREALDeviceCategory 型を取得
                    var deviceCategoryType = FindTypeInLoadedAssemblies("Unity.XR.XREAL.XREALDeviceCategory");
                    if (deviceCategoryType != null)
                    {
                        var listType = typeof(List<>).MakeGenericType(deviceCategoryType);
                        var list = Activator.CreateInstance(listType);
                        var addMethod = listType.GetMethod("Add");
                        
                        // REALITY (1) を追加
                        var realityValue = Enum.ToObject(deviceCategoryType, 1);
                        addMethod.Invoke(list, new[] { realityValue });
                        
                        // VISION (2) を追加 - XREAL One はここに含まれる
                        var visionValue = Enum.ToObject(deviceCategoryType, 2);
                        addMethod.Invoke(list, new[] { visionValue });
                        
                        fiSupportDevices.SetValue(settings, list);
                        Debug.Log("[Arsist] XREAL SupportDevices set to [REALITY, VISION] (includes XREAL One)");
                    }
                    else
                    {
                        Debug.LogWarning("[Arsist] XREALDeviceCategory type not found");
                    }
                }
                else
                {
                    Debug.LogWarning("[Arsist] Could not find SupportDevices field on XREALSettings");
                }

                // Stereo Rendering Mode: SinglePassInstanced (推奨)
                var propStereo = settingsType.GetField("StereoRendering", BindingFlags.Public | BindingFlags.Instance);
                if (propStereo != null)
                {
                    // StereoRenderingMode.SinglePassInstanced = 1
                    var stereoType = FindTypeInLoadedAssemblies("Unity.XR.XREAL.StereoRenderingMode");
                    if (stereoType != null)
                    {
                        var singlePassValue = Enum.ToObject(stereoType, 1); // SinglePassInstanced
                        propStereo.SetValue(settings, singlePassValue);
                        Debug.Log("[Arsist] XREAL StereoRendering set to SinglePassInstanced");
                    }
                }

                // Initial Tracking Type: MODE_3DOF (XREAL One は 3DoF デバイス)
                var propTracking = settingsType.GetField("InitialTrackingType", BindingFlags.Public | BindingFlags.Instance);
                if (propTracking != null)
                {
                    // TrackingType: MODE_6DOF=0, MODE_3DOF=1
                    // XREAL One は 3DoF なので MODE_3DOF を設定
                    var trackingType = FindTypeInLoadedAssemblies("Unity.XR.XREAL.TrackingType");
                    if (trackingType != null)
                    {
                        var mode3dof = Enum.ToObject(trackingType, 1);
                        propTracking.SetValue(settings, mode3dof);
                        Debug.Log("[Arsist] XREAL TrackingType set to MODE_3DOF (for XREAL One)");
                    }
                }

                // Initial Input Source: Controller (Beam Pro)
                var propInput = settingsType.GetField("InitialInputSource", BindingFlags.Public | BindingFlags.Instance);
                if (propInput != null)
                {
                    // InputSource: Hands=0, Controller=1, None=2, ControllerAndHands=3
                    var inputType = FindTypeInLoadedAssemblies("Unity.XR.XREAL.InputSource");
                    if (inputType != null)
                    {
                        var controllerValue = Enum.ToObject(inputType, 1);
                        propInput.SetValue(settings, controllerValue);
                        Debug.Log("[Arsist] XREAL InputSource set to Controller");
                    }
                }

                Debug.Log("[Arsist] XREAL Settings defaults applied");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to set XREAL Settings defaults: {e.Message}");
            }
        }

        private static XRGeneralSettings GetOrCreateXRGeneralSettings(BuildTargetGroup target)
        {
            var settings = GetXRGeneralSettingsForBuildTarget(target);
            if (settings != null) return settings;

            // 作成が必要
            try
            {
                var xrGeneralSettingsType = typeof(XRGeneralSettings);
                settings = ScriptableObject.CreateInstance(xrGeneralSettingsType) as XRGeneralSettings;

                if (settings != null)
                {
                    // EditorBuildSettings に登録
                    var perBuildTargetType = typeof(XRGeneralSettingsPerBuildTarget);
                    var piInstance = perBuildTargetType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    var inst = piInstance?.GetValue(null, null);

                    if (inst != null)
                    {
                        var miSet = perBuildTargetType.GetMethod(
                            "SetSettingsForBuildTarget",
                            BindingFlags.Public | BindingFlags.Instance,
                            null,
                            new[] { typeof(BuildTargetGroup), typeof(XRGeneralSettings) },
                            null
                        );

                        if (miSet != null)
                        {
                            miSet.Invoke(inst, new object[] { target, settings });
                        }
                    }
                }

                return settings;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Arsist] Failed to create XRGeneralSettings: {e.Message}");
                return null;
            }
        }

        private static void EnsureXrealLoaderActive(XRManagerSettings manager)
        {
            try
            {
                // XREAL Loader型を検索
                var xrealLoaderType = FindTypeInLoadedAssemblies("Unity.XR.XREAL.XREALXRLoader");
                if (xrealLoaderType == null)
                {
                    Debug.LogWarning("[Arsist] XREAL XR Loader type not found. Ensure XREAL SDK is imported.");
                    return;
                }

                // 既に有効化されているか確認
                var hasXreal = false;
                foreach (var loader in manager.activeLoaders)
                {
                    if (loader != null && (loader.GetType() == xrealLoaderType || 
                        loader.GetType().Name == "XREALXRLoader"))
                    {
                        hasXreal = true;
                        break;
                    }
                }

                if (hasXreal)
                {
                    Debug.Log("[Arsist] XREAL Loader already active");
                    return;
                }

                // loaders リストに追加してTrySetLoadersで設定（Unity 6対応）
                var loadersList = new List<XRLoader>();
                
                // 既存のloadersを保持
                var existingLoadersField = manager.GetType().GetProperty("loaders", 
                    BindingFlags.Public | BindingFlags.Instance);
                if (existingLoadersField != null)
                {
                    var existing = existingLoadersField.GetValue(manager) as List<XRLoader>;
                    if (existing != null)
                    {
                        loadersList.AddRange(existing);
                    }
                }
                
                // XREAL Loader を作成してアセットとして保存
                var loaderInstance = ScriptableObject.CreateInstance(xrealLoaderType) as XRLoader;
                if (loaderInstance == null)
                {
                    Debug.LogWarning("[Arsist] Failed to create XREAL Loader instance");
                    return;
                }
                
                // Loaderをアセットとして保存
                var xrDir = "Assets/XR/Settings";
                Directory.CreateDirectory(xrDir);
                var loaderPath = $"{xrDir}/XREALLoader.asset";
                AssetDatabase.CreateAsset(loaderInstance, loaderPath);
                Debug.Log($"[Arsist] XREAL Loader asset created: {loaderPath}");
                
                loadersList.Add(loaderInstance);
                
                // TrySetLoaders で設定
                var trySetLoadersMethod = manager.GetType().GetMethod("TrySetLoaders",
                    BindingFlags.Public | BindingFlags.Instance);
                
                if (trySetLoadersMethod != null)
                {
                    var result = trySetLoadersMethod.Invoke(manager, new object[] { loadersList });
                    Debug.Log($"[Arsist] TrySetLoaders result: {result}");
                    EditorUtility.SetDirty(manager);
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    Debug.LogWarning("[Arsist] TrySetLoaders method not found, using loaders property directly");
                    if (existingLoadersField != null)
                    {
                        existingLoadersField.SetValue(manager, loadersList);
                        EditorUtility.SetDirty(manager);
                        AssetDatabase.SaveAssets();
                        Debug.Log("[Arsist] Loaders set via property");
                    }
                }
                
                // 再度確認
                hasXreal = false;
                foreach (var loader in manager.activeLoaders)
                {
                    if (loader != null && (loader.GetType() == xrealLoaderType || 
                        loader.GetType().Name == "XREALXRLoader"))
                    {
                        hasXreal = true;
                        break;
                    }
                }
                
                if (hasXreal)
                {
                    Debug.Log("[Arsist] XREAL Loader is now active in activeLoaders");
                }
                else
                {
                    Debug.LogWarning("[Arsist] XREAL Loader still not in activeLoaders after adding to loaders list");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Arsist] Failed to ensure XREAL Loader: {e.Message}\n{e.StackTrace}");
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
            
            // XREAL One: Portrait推奨（XrealOne.txt準拠）
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            
            // Auto Graphics API を無効化し、OpenGLES3 のみに設定（Vulkan削除）
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            
            // VSync Count: Don't Sync（XREAL推奨）
            QualitySettings.vSyncCount = 0;

            // ★ GLTFAST scripting define symbol を追加（glTFast パッケージ使用のため必須）
            EnsureScriptingDefineSymbol(BuildTargetGroup.Android, "GLTFAST");
            EnsureScriptingDefineSymbol(BuildTargetGroup.Standalone, "GLTFAST");

            // ★ glTFastのシェーダーがビルドでストリップされるとInstantiate中にNREになりやすい。
            // Always Included Shaders に追加して、最低限のglTFシェーダーを必ず同梱する。
            EnsureGltfShadersAlwaysIncluded();

            Debug.Log("[Arsist] Build settings applied (XREAL SDK 3.1 compliant)");
        }

        private static GameObject EnsureWorldRoot()
        {
            var existing = GameObject.Find("WorldRoot");
            if (existing != null) return existing;
            var root = new GameObject("WorldRoot");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static void EnsureGltfShadersAlwaysIncluded()
        {
            try
            {
                // Built-in pipeline向け（BuiltInMaterialGeneratorが参照）
                var shaderPaths = new[]
                {
                    "Packages/com.atteneder.gltfast/Runtime/Shader/Built-In/glTFUnlit.shader",
                    "Packages/com.atteneder.gltfast/Runtime/Shader/Built-In/glTFPbrMetallicRoughness.shader",
                    "Packages/com.atteneder.gltfast/Runtime/Shader/Built-In/glTFPbrSpecularGlossiness.shader",
                };

                var shadersToAdd = new List<Shader>();
                foreach (var p in shaderPaths)
                {
                    var s = AssetDatabase.LoadAssetAtPath<Shader>(p);
                    if (s != null) shadersToAdd.Add(s);
                }

                // 念のため、glTFast配下のShader/ShaderGraphも拾える範囲で追加
                var extraGuids = AssetDatabase.FindAssets("glTF t:Shader", new[] { "Packages/com.atteneder.gltfast/Runtime/Shader" });
                foreach (var guid in extraGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var s = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (s != null) shadersToAdd.Add(s);
                }

                shadersToAdd = shadersToAdd.Where(s => s != null).Distinct().ToList();
                if (shadersToAdd.Count == 0)
                {
                    Debug.Log("[Arsist] No glTFast shaders found to include (skipping)");
                    return;
                }

                // Unity 6000系では UnityEditor.GraphicsSettings API が存在しない環境があるため、
                // Always Included Shaders を直接触らず、Resources配下にマテリアルを生成して参照を固定する。
                // Resources 配下のアセットはビルドに同梱されるため、シェーダーのストリップ回避に効く。
                const string resourcesRoot = "Assets/Arsist/Runtime/Resources";
                const string outFolder = "Assets/Arsist/Runtime/Resources/ArsistGltfShaders";
                if (!AssetDatabase.IsValidFolder(resourcesRoot))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Arsist")) AssetDatabase.CreateFolder("Assets", "Arsist");
                    if (!AssetDatabase.IsValidFolder("Assets/Arsist/Runtime")) AssetDatabase.CreateFolder("Assets/Arsist", "Runtime");
                    AssetDatabase.CreateFolder("Assets/Arsist/Runtime", "Resources");
                }
                if (!AssetDatabase.IsValidFolder(outFolder))
                {
                    AssetDatabase.CreateFolder(resourcesRoot, "ArsistGltfShaders");
                }

                var created = 0;
                var updated = 0;
                foreach (var shader in shadersToAdd)
                {
                    if (shader == null) continue;
                    var safeName = (shader.name ?? "shader")
                        .Replace("/", "_")
                        .Replace("\\", "_")
                        .Replace(":", "_")
                        .Replace(" ", "_");
                    var matPath = $"{outFolder}/{safeName}.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null)
                    {
                        mat = new Material(shader);
                        AssetDatabase.CreateAsset(mat, matPath);
                        created++;
                    }
                    else if (mat.shader != shader)
                    {
                        mat.shader = shader;
                        EditorUtility.SetDirty(mat);
                        updated++;
                    }
                }

                AssetDatabase.SaveAssets();
                Debug.Log($"[Arsist] Ensured glTFast shader references via Resources materials (created={created}, updated={updated}, totalShaders={shadersToAdd.Count})");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to ensure glTFast shaders included: {e.Message}");
            }
        }

        private static void ApplyDevicePatches(string targetDevice)
        {
            try
            {
                var normalized = (targetDevice ?? "").ToLowerInvariant();
                if (normalized.Contains("xreal"))
                {
                    // Adapters/XREAL_One/XrealBuildPatcher.cs がUnityプロジェクト側にコピーされている前提
                    InvokeStaticIfExists(
                        "Arsist.Adapters.XrealOne.XrealBuildPatcher",
                        "ApplyAllPatches"
                    );
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to apply device patches: {e.Message}");
            }
        }

        private static void ValidateBuildReadiness(string targetDevice)
        {
            var problems = new List<string>();

            var normalized = (targetDevice ?? "").ToLowerInvariant();
            var isXreal = normalized.Contains("xreal");

            // ==== Android 基本要件（XrealOneガイド準拠）====
            if (EditorUserBuildSettings.activeBuildTarget != _buildTarget)
            {
                problems.Add($"BuildTarget mismatch (expected: {_buildTarget}, actual: {EditorUserBuildSettings.activeBuildTarget})");
            }

            // 現状のヘッドレスビルドは Android を主対象
            if (_buildTarget == BuildTarget.Android && PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
            {
                problems.Add("Scripting Backend is not IL2CPP");
            }

            if (_buildTarget == BuildTarget.Android && (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
            {
                problems.Add("Target Architectures does not include ARM64");
            }

            if (_buildTarget == BuildTarget.Android && (int)PlayerSettings.Android.minSdkVersion < 29)
            {
                problems.Add($"minSdkVersion is too low: {(int)PlayerSettings.Android.minSdkVersion} (need >=29)");
            }

            // Graphics API（XrealOne: Vulkan削除 & OpenGLES3のみ）
            if (isXreal)
            {
                try
                {
                    if (_buildTarget == BuildTarget.Android && PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android))
                    {
                        problems.Add("Auto Graphics API is enabled (must be disabled)");
                    }

                    var apis = _buildTarget == BuildTarget.Android ? PlayerSettings.GetGraphicsAPIs(BuildTarget.Android) : null;
                    if (apis == null || apis.Length == 0)
                    {
                        problems.Add("Graphics APIs list is empty");
                    }
                    else
                    {
                        if (apis[0] != GraphicsDeviceType.OpenGLES3)
                        {
                            problems.Add($"Graphics API[0] is not OpenGLES3 (actual: {apis[0]})");
                        }

                        foreach (var api in apis)
                        {
                            if (api == GraphicsDeviceType.Vulkan)
                            {
                                problems.Add("Vulkan is present in Graphics APIs (must be removed for XREAL transparency stability)");
                                break;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    problems.Add($"Failed to validate Graphics APIs: {e.Message}");
                }
            }

            // Input System（XREAL SDK 3.x は Input System 対応）
            try
            {
                var psType = typeof(PlayerSettings);
                var prop = psType.GetProperty("activeInputHandling", BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    var value = prop.GetValue(null);
                    var str = value?.ToString() ?? "";
                    // 代表的な値: Both / InputSystemPackage / OldInputManager
                    if (!(str.IndexOf("Both", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          str.IndexOf("InputSystem", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        problems.Add($"Input handling is not using Input System (actual: {str}). Set to 'Both' or 'Input System Package'.");
                    }
                }
            }
            catch (Exception e)
            {
                problems.Add($"Failed to validate Input System setting: {e.Message}");
            }

            // ==== XR Plug-in Management（XREAL Loader が有効になっていること）====
            if (isXreal && _buildTarget == BuildTarget.Android)
            {
                try
                {
                    var generalSettings = GetXRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
                    if (generalSettings == null)
                    {
                        Debug.LogWarning("[Arsist] XR General Settings (Android) is missing. Creating now...");
                        generalSettings = GetOrCreateXRGeneralSettings(BuildTargetGroup.Android);
                        if (generalSettings != null)
                        {
                            generalSettings.InitManagerOnStart = true;
                            EditorUtility.SetDirty(generalSettings);
                            AssetDatabase.SaveAssets();
                        }
                        else
                        {
                            problems.Add("XR General Settings (Android) could not be created");
                        }
                    }
                    
                    if (generalSettings != null)
                    {
                        if (!generalSettings.InitManagerOnStart)
                        {
                            Debug.LogWarning("[Arsist] Initialize XR on Startup was not enabled. Enabling now...");
                            generalSettings.InitManagerOnStart = true;
                            EditorUtility.SetDirty(generalSettings);
                            AssetDatabase.SaveAssets();
                        }

                        // Get latest Manager state (without full Refresh to avoid crash)
                        generalSettings = GetXRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
                        
                        Debug.Log($"[Arsist] generalSettings: {(generalSettings != null ? "Found" : "NULL")}");
                        if (generalSettings != null)
                        {
                            Debug.Log($"[Arsist] generalSettings.Manager: {(generalSettings.Manager != null ? "Found" : "NULL")}");
                            if (generalSettings.Manager != null)
                            {
                                Debug.Log($"[Arsist] activeLoaders count: {generalSettings.Manager.activeLoaders.Count}");
                            }
                        }
                        
                        var manager = generalSettings?.Manager;
                        if (manager == null)
                        {
                            problems.Add("XR Manager Settings is missing after Phase 3.05. ForceXrPluginSettings may have failed.");
                        }
                        
                        if (manager != null)
                        {
                            var hasXrealLoader = false;
                            Debug.Log($"[Arsist] Enumerating {manager.activeLoaders.Count} activeLoaders...");
                            foreach (var loader in manager.activeLoaders)
                            {
                                if (loader == null)
                                {
                                    Debug.LogWarning("[Arsist] Found NULL loader in activeLoaders");
                                    continue;
                                }
                                Debug.Log($"[Arsist] Checking loader: {loader.GetType().FullName}, Name: {loader.GetType().Name}");
                                if (loader.GetType().FullName == "Unity.XR.XREAL.XREALXRLoader" || 
                                    loader.GetType().Name == "XREALXRLoader")
                                {
                                    hasXrealLoader = true;
                                    break;
                                }
                            }

                            if (!hasXrealLoader)
                            {
                                problems.Add("XREAL XR Loader is not enabled in XR Plug-in Management (Android). Ensure Phase 3.05 completed successfully.");
                            }
                            else
                            {
                                Debug.Log("[Arsist] XREAL XR Loader validation passed");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    problems.Add($"Failed to validate XR settings: {e.Message}");
                }
            }

            // ==== XREAL Settings（XREAL SDK 3.x が内部参照するため必須）====
            if (isXreal)
            {
                try
                {
                    if (!TryHasXrealSettingsConfigObject(out var key, out var existing))
                    {
                        problems.Add($"XREALSettings config object is missing (key: {key}). Ensure XREALSettings is registered in EditorBuildSettings.");
                    }
                }
                catch (Exception e)
                {
                    problems.Add($"Failed to validate XREALSettings config: {e.Message}");
                }
            }

            // ==== カメラ透過要件（XrealOne: 黒=透明 / ARCameraBackground除去）====
            if (isXreal)
            {
                try
                {
                    ValidateTransparentCameraScenes(ref problems);
                }
                catch (Exception e)
                {
                    problems.Add($"Failed to validate transparent camera settings: {e.Message}");
                }
            }

            if (problems.Count > 0)
            {
                var message = "Build validation failed:\n- " + string.Join("\n- ", problems);
                throw new Exception(message);
            }
        }

        private static XRGeneralSettings GetXRGeneralSettingsForBuildTarget(BuildTargetGroup target)
        {
            try
            {
                var t = typeof(XRGeneralSettingsPerBuildTarget);

                // 1) static XRGeneralSettingsForBuildTarget(BuildTargetGroup)
                var miStatic = t.GetMethod(
                    "XRGeneralSettingsForBuildTarget",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(BuildTargetGroup) },
                    null
                );
                if (miStatic != null)
                {
                    return miStatic.Invoke(null, new object[] { target }) as XRGeneralSettings;
                }

                // 2) instance: XRGeneralSettingsPerBuildTarget.Instance.XRGeneralSettingsForBuildTarget(BuildTargetGroup)
                var piInstance = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var inst = piInstance != null ? piInstance.GetValue(null, null) : null;
                if (inst != null)
                {
                    var mi = t.GetMethod(
                        "XRGeneralSettingsForBuildTarget",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(BuildTargetGroup) },
                        null
                    );
                    if (mi != null)
                    {
                        return mi.Invoke(inst, new object[] { target }) as XRGeneralSettings;
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool TryHasXrealSettingsConfigObject(out string key, out UnityEngine.Object existing)
        {
            existing = null;
            key = "com.unity.xr.management.xrealsettings";

            // SDK側の定数が取れるなら優先
            var xrealSettingsType = FindTypeInLoadedAssemblies("Unity.XR.XREAL.XREALSettings");
            if (xrealSettingsType != null)
            {
                var fiKey = xrealSettingsType.GetField("k_SettingsKey", BindingFlags.Public | BindingFlags.Static);
                if (fiKey != null && fiKey.FieldType == typeof(string))
                {
                    var v = fiKey.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        key = v;
                    }
                }
            }

            if (EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(key, out existing) && existing != null)
            {
                return true;
            }
            return false;
        }


        private static void ValidateTransparentCameraScenes(ref List<string> problems)
        {
            // Build対象シーン（未設定なら Assets 配下の Scene を対象）
            var scenePaths = EditorBuildSettings.scenes
                .Where(s => s != null && s.enabled && !string.IsNullOrWhiteSpace(s.path) && File.Exists(s.path))
                .Select(s => s.path)
                .Distinct()
                .ToList();

            if (scenePaths.Count == 0)
            {
                var guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
                foreach (var guid in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrWhiteSpace(p) && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                    {
                        scenePaths.Add(p);
                    }
                }
                scenePaths = scenePaths.Distinct().ToList();
            }

            if (scenePaths.Count == 0)
            {
                problems.Add("No scenes found. XrealOne requires a scene containing a MainCamera configured for transparency.");
                return;
            }

            var arCameraBackgroundType = FindTypeInLoadedAssemblies("UnityEngine.XR.ARFoundation.ARCameraBackground");
            var desiredBg = new Color(0f, 0f, 0f, 0f);
            var foundCamera = false;

            foreach (var scenePath in scenePaths)
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

                Camera targetCamera = null;
#if UNITY_2023_1_OR_NEWER
                var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
#else
                var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
#endif

                targetCamera = cameras.FirstOrDefault(c => c != null && c.gameObject != null && SafeCompareTag(c.gameObject, "MainCamera"));
                if (targetCamera == null)
                {
                    targetCamera = cameras.FirstOrDefault(c => c != null && c.gameObject != null && string.Equals(c.gameObject.name, "Main Camera", StringComparison.Ordinal));
                }

                if (targetCamera == null)
                {
                    continue;
                }

                foundCamera = true;

                if (targetCamera.clearFlags != CameraClearFlags.SolidColor)
                {
                    problems.Add($"{scenePath}: MainCamera clearFlags is not SolidColor");
                }

                if (targetCamera.backgroundColor != desiredBg)
                {
                    problems.Add($"{scenePath}: MainCamera backgroundColor is not (0,0,0,0)");
                }

                if (arCameraBackgroundType != null)
                {
                    var comps = targetCamera.GetComponents(arCameraBackgroundType);
                    if (comps != null && comps.Length > 0)
                    {
                        problems.Add($"{scenePath}: ARCameraBackground is attached to MainCamera (must be removed for XREAL transparency)");
                    }
                }
            }

            if (!foundCamera)
            {
                problems.Add("No Camera found in scenes. XrealOne requires a MainCamera.");
            }
        }

        private static bool SafeCompareTag(GameObject go, string tag)
        {
            try
            {
                return go != null && go.CompareTag(tag);
            }
            catch
            {
                return false;
            }
        }

        private static void InvokeStaticIfExists(string typeName, string methodName)
        {
            var t = FindTypeInLoadedAssemblies(typeName);
            if (t == null)
            {
                Debug.LogWarning($"[Arsist] Type not found: {typeName}");
                return;
            }

            var mi = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null)
            {
                Debug.LogWarning($"[Arsist] Method not found: {typeName}.{methodName}");
                return;
            }

            mi.Invoke(null, null);
        }

        private static Type FindTypeInLoadedAssemblies(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
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
                target = _buildTarget,
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

        private static void EnsureOpenXRSettingsLoaded()
        {
            try
            {
                // OpenXR は環境/アダプターによっては入っていない可能性があるため、reflectionでbest-effort
                var openXrSettingsType = FindTypeInLoadedAssemblies("UnityEngine.XR.OpenXR.OpenXRSettings");
                if (openXrSettingsType == null)
                {
                    Debug.Log("[Arsist] OpenXRSettings type not found (skipping)");
                    return;
                }

                var propActive = openXrSettingsType.GetProperty("ActiveBuildTargetInstance", BindingFlags.Public | BindingFlags.Static);
                var miGet = openXrSettingsType.GetMethod("GetSettingsForBuildTargetGroup", BindingFlags.Public | BindingFlags.Static);

                object active = null;
                if (propActive != null)
                {
                    active = propActive.GetValue(null);
                }
                if (active == null && miGet != null)
                {
                    active = miGet.Invoke(null, new object[] { BuildTargetGroup.Android });
                }

                if (active != null) Debug.Log("[Arsist] OpenXRSettings loaded");
                else Debug.LogWarning("[Arsist] OpenXRSettings not available yet");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to load OpenXRSettings: {e.Message}");
            }
        }

        private static void EnsureOpenXRPackageSettingsLoaded()
        {
            try
            {
                // OpenXRPackageSettings は internal なので reflection で呼び出す
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings");
                        if (t != null) break;
                    }
                    catch { }
                }

                if (t == null)
                {
                    Debug.LogWarning("[Arsist] OpenXRPackageSettings type not found");
                    return;
                }

                var mi = t.GetMethod("GetOrCreateInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null)
                {
                    Debug.LogWarning("[Arsist] OpenXRPackageSettings.GetOrCreateInstance not found");
                    return;
                }

                mi.Invoke(null, null);
                Debug.Log("[Arsist] OpenXRPackageSettings loaded");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to load OpenXRPackageSettings: {e.Message}");
            }
        }

        /// <summary>
        /// Preloaded Assets にアセットを追加する
        /// XRGeneralSettings, XREALSettings などはランタイムで参照されるため必須
        /// </summary>
        private static void AddToPreloadedAssets(UnityEngine.Object asset)
        {
            if (asset == null) return;
            
            try
            {
                var preloadedAssets = PlayerSettings.GetPreloadedAssets()?.ToList() ?? new List<UnityEngine.Object>();
                
                // 既に含まれているか確認
                if (preloadedAssets.Any(a => a == asset))
                {
                    Debug.Log($"[Arsist] {asset.name} already in Preloaded Assets");
                    return;
                }
                
                // nullエントリを除去
                preloadedAssets.RemoveAll(a => a == null);
                
                preloadedAssets.Add(asset);
                PlayerSettings.SetPreloadedAssets(preloadedAssets.ToArray());
                Debug.Log($"[Arsist] Added {asset.name} ({asset.GetType().Name}) to Preloaded Assets");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to add {asset?.name} to Preloaded Assets: {e.Message}");
            }
        }

        /// <summary>
        /// Scripting Define Symbol を追加する（既に存在する場合は何もしない）
        /// </summary>
        private static void EnsureScriptingDefineSymbol(BuildTargetGroup targetGroup, string symbol)
        {
            try
            {
#if UNITY_2023_1_OR_NEWER
                // Unity 2023.1+ では NamedBuildTarget を使用
                var namedTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup);
                var defines = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
#else
                var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
#endif
                if (string.IsNullOrEmpty(defines))
                {
                    defines = symbol;
                }
                else if (!defines.Split(';').Any(d => d.Trim() == symbol))
                {
                    defines = defines + ";" + symbol;
                }
                else
                {
                    Debug.Log($"[Arsist] Scripting define '{symbol}' already exists for {targetGroup}");
                    return;
                }

#if UNITY_2023_1_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(namedTarget, defines);
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
#endif
                Debug.Log($"[Arsist] Added scripting define '{symbol}' to {targetGroup}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Arsist] Failed to add scripting define '{symbol}': {e.Message}");
            }
        }
    }
}
