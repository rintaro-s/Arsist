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

            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            Debug.Log($"[Arsist] Building project: {manifest["projectName"]}");

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
                ApplyBuildSettings(manifest);

                // Phase 4: ビルド実行
                Debug.Log("[Arsist] Phase 4: Building APK...");
                ExecuteBuild(manifest);

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

            GameObject go = null;

            // プリミティブ作成
            if (type == "primitive")
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
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                
                var colorHex = material["color"]?.ToString() ?? "#FFFFFF";
                if (ColorUtility.TryParseHtmlString(colorHex, out Color color))
                {
                    mat.color = color;
                }
                
                mat.SetFloat("_Metallic", material["metallic"]?.Value<float>() ?? 0);
                mat.SetFloat("_Smoothness", 1 - (material["roughness"]?.Value<float>() ?? 0.5f));
                
                renderer.material = mat;
            }
        }

        private static void CreateXROrigin()
        {
            // XR Origin プレハブを探してインスタンス化
            var xrOriginPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Arsist/Prefabs/XROrigin.prefab");
            if (xrOriginPrefab != null)
            {
                var xrOrigin = (GameObject)PrefabUtility.InstantiatePrefab(xrOriginPrefab);
                xrOrigin.transform.position = Vector3.zero;
                Debug.Log("[Arsist] XR Origin instantiated");
            }
            else
            {
                // フォールバック: 基本的なXR Originを作成
                Debug.LogWarning("[Arsist] XR Origin prefab not found, creating basic setup");
                
                var xrOriginGO = new GameObject("XR Origin");
                // XR Origin コンポーネントは SDK に依存するため、ここでは構造のみ作成
                
                var cameraOffset = new GameObject("Camera Offset");
                cameraOffset.transform.SetParent(xrOriginGO.transform);
                
                var mainCamera = new GameObject("Main Camera");
                mainCamera.tag = "MainCamera";
                mainCamera.transform.SetParent(cameraOffset.transform);
                mainCamera.AddComponent<Camera>();
                mainCamera.AddComponent<AudioListener>();
            }
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

                // Canvas のサイズ設定（AR用に2m x 1.125m @ 3m距離）
                var rectTransform = canvasGO.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(1920, 1080);
                rectTransform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                rectTransform.position = new Vector3(0, 1.5f, 3f);

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
