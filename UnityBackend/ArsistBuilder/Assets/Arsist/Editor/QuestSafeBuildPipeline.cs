using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Arsist.Builder
{
    public static class QuestSafeBuildPipeline
    {
        private static string _outputPath;
        private static string _targetDevice;
        private static bool _developmentBuild;
        private static BuildTarget _buildTarget = BuildTarget.Android;

        public static void BuildFromCLI()
        {
            ParseCommandLineArgs();

            try
            {
                EnsureGeneratedDataFolders();
                CopyUICodeToStreamingAssets();

                if (IsQuestTarget())
                {
                    ApplyQuestLaunchSafety();
                }

                EnsureOpenXRReady();
                EnsureLinearColorSpace();

                var scenePath = GenerateDeterministicScene();
                ExecuteBuild(scenePath);

                Debug.Log("[ArsistSafe] Build completed successfully.");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArsistSafe] Build failed: {e.Message}\n{e.StackTrace}");
                EditorApplication.Exit(1);
            }
        }

        private static void ParseCommandLineArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "-buildTarget":
                        _buildTarget = ParseBuildTarget(GetArgValue(args, ref index));
                        break;
                    case "-outputPath":
                        _outputPath = GetArgValue(args, ref index);
                        break;
                    case "-targetDevice":
                        _targetDevice = GetArgValue(args, ref index);
                        break;
                    case "-developmentBuild":
                        _developmentBuild = string.Equals(GetArgValue(args, ref index), "true", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            Debug.Log($"[ArsistSafe] Target={_buildTarget}, Device={_targetDevice}, Output={_outputPath}, Dev={_developmentBuild}");
        }

        private static string GetArgValue(string[] args, ref int index)
        {
            var next = index + 1;
            if (next >= args.Length) return string.Empty;
            index = next;
            return args[next] ?? string.Empty;
        }

        private static BuildTarget ParseBuildTarget(string raw)
        {
            var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "android" => BuildTarget.Android,
                "ios" => BuildTarget.iOS,
                "windows" => BuildTarget.StandaloneWindows64,
                "macos" => BuildTarget.StandaloneOSX,
                _ => BuildTarget.Android,
            };
        }

        private static bool IsQuestTarget()
        {
            var value = (_targetDevice ?? string.Empty).ToLowerInvariant();
            return value.Contains("quest") || value.Contains("meta");
        }

        private static void EnsureGeneratedDataFolders()
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "StreamingAssets"));
            AssetDatabase.Refresh();
        }

        private static void CopyUICodeToStreamingAssets()
        {
            var sourceDir = Path.Combine(Application.dataPath, "ArsistGenerated", "UICode");
            if (!Directory.Exists(sourceDir))
            {
                Debug.LogWarning("[ArsistSafe] UICode not found. Skipping StreamingAssets UI copy.");
                return;
            }

            var destinationDir = Path.Combine(Application.dataPath, "StreamingAssets", "ArsistUI");
            Directory.CreateDirectory(destinationDir);

            foreach (var sourcePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = sourcePath.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destinationPath = Path.Combine(destinationDir, relativePath);
                var destinationFolder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }
                File.Copy(sourcePath, destinationPath, true);
            }

            AssetDatabase.Refresh();
            Debug.Log("[ArsistSafe] UI assets copied to StreamingAssets/ArsistUI.");
        }

        private static void ApplyQuestLaunchSafety()
        {
            CopyQuestAdapterManifest();
            PatchOculusProjectConfigYaml();
            EnsureQuestFocusAwareManifestMetadata();
            ApplyQuestPlayerSettings();
        }

        private static void CopyQuestAdapterManifest()
        {
            // Arsistリポジトリルート = Application.dataPath の3階層上
            var adapterManifest = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "..", "Adapters", "Meta_Quest", "AndroidManifest.xml"));

            if (!File.Exists(adapterManifest))
            {
                Debug.LogWarning($"[ArsistSafe] Adapter AndroidManifest not found: {adapterManifest}");
                return;
            }

            var destDir = Path.Combine(Application.dataPath, "Plugins", "Android");
            Directory.CreateDirectory(destDir);
            var destPath = Path.Combine(destDir, "AndroidManifest.xml");
            File.Copy(adapterManifest, destPath, overwrite: true);
            AssetDatabase.Refresh();
            Debug.Log($"[ArsistSafe] AndroidManifest.xml copied: {adapterManifest} -> {destPath}");
        }

        private static void ApplyQuestPlayerSettings()
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
            });
            PlayerSettings.MTRendering = true;

            // Quest必須: ロード画面中もUnityゲームループを継続させる（15秒タイムアウトクラッシュ防止）
            PlayerSettings.runInBackground = true;

            Debug.Log("[ArsistSafe] Quest PlayerSettings applied (IL2CPP/ARM64/SDK32/Vulkan/Instancing/RunInBackground).");
        }

        private static void PatchOculusProjectConfigYaml()
        {
            // まずC# API経由で設定を変更（YAMLがOVR SDKに上書きされる問題を回避）
            if (TryPatchOculusProjectConfigViaApi())
            {
                return;
            }

            // フォールバック: YAML直接書き換え
            var configPath = Path.Combine(Application.dataPath, "Oculus", "OculusProjectConfig.asset");
            if (!File.Exists(configPath))
            {
                Debug.LogWarning($"[ArsistSafe] OculusProjectConfig not found: {configPath}");
                return;
            }

            var yaml = File.ReadAllText(configPath);
            yaml = ReplaceYamlInt(yaml, "handTrackingSupport", 1);
            yaml = ReplaceYamlInt(yaml, "handTrackingFrequency", 1);
            yaml = ReplaceYamlInt(yaml, "insightPassthroughEnabled", 1);
            yaml = ReplaceYamlInt(yaml, "_insightPassthroughSupport", 2);
            yaml = ReplaceYamlInt(yaml, "focusAware", 1);
            yaml = ReplaceYamlInt(yaml, "sceneSupport", 1);
            File.WriteAllText(configPath, yaml);

            // ImportAsset で YAML を再読み込みさせてから、その値で ScriptableObject を SaveAssets する
            var assetPath = "Assets/Oculus/OculusProjectConfig.asset";
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            // ImportAsset 後にロードして SetDirty → SaveAssets で確定
            var loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (loadedAsset != null)
            {
                EditorUtility.SetDirty(loadedAsset);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[ArsistSafe] OculusProjectConfig patched for Quest (YAML fallback).");
        }

        private static bool TryPatchOculusProjectConfigViaApi()
        {
            try
            {
                var configType = FindType("OVRProjectConfig");
                if (configType == null)
                {
                    Debug.LogWarning("[ArsistSafe] OVRProjectConfig type not found.");
                    return false;
                }

                // 正しいメソッド名: GetOrCreateProjectConfig
                var getOrCreate = configType.GetMethod("GetOrCreateProjectConfig",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (getOrCreate == null)
                {
                    // フォールバック: GetOrCreateConfig も試す
                    getOrCreate = configType.GetMethod("GetOrCreateConfig",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                }
                if (getOrCreate == null)
                {
                    Debug.LogWarning("[ArsistSafe] OVRProjectConfig.GetOrCreateProjectConfig not found.");
                    return false;
                }

                var config = getOrCreate.Invoke(null, null) as UnityEngine.Object;
                if (config == null)
                {
                    Debug.LogWarning("[ArsistSafe] OVRProjectConfig.GetOrCreateProjectConfig returned null.");
                    return false;
                }

                // insightPassthroughEnabled = true (bool フィールド)
                TrySetFieldOrProperty(config, "insightPassthroughEnabled", true);

                // focusAware = true
                TrySetFieldOrProperty(config, "focusAware", true);

                // _insightPassthroughSupport = 2 (Required) / enum OVRProjectConfig.FeatureSupport
                SetOvrEnumField(config, configType, "_insightPassthroughSupport", "Required");

                // sceneSupport = 1 (Supported)
                SetOvrEnumField(config, configType, "sceneSupport", "Supported");

                // handTrackingSupport = ControllersAndHands
                SetOvrEnumField(config, configType, "handTrackingSupport", "ControllersAndHands");

                // CommitUpdates があれば呼ぶ
                var commit = configType.GetMethod("CommitUpdates",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (commit != null)
                {
                    commit.Invoke(null, new object[] { config });
                }
                else
                {
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssets();
                }

                Debug.Log("[ArsistSafe] OculusProjectConfig patched via C# API (passthrough/scene/hands enabled).");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistSafe] OculusProjectConfig API patch failed: {e.Message}");
                return false;
            }
        }

        private static void TrySetFieldOrProperty(object target, string name, object value)
        {
            var type = target.GetType();
            var field = type.GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field != null) { field.SetValue(target, value); return; }

            var prop = type.GetProperty(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(target, value);
        }

        private static void SetOvrEnumField(object target, Type configType, string fieldName, string enumValueName)
        {
            try
            {
                var field = target.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field == null) return;

                var enumType = field.FieldType;
                if (!enumType.IsEnum) return;

                // 指定名からEnum値を試みる。失敗したら整数でセット
                if (Enum.IsDefined(enumType, enumValueName))
                {
                    field.SetValue(target, Enum.Parse(enumType, enumValueName));
                }
                else
                {
                    // Required=2, Supported=1 で整数キャスト試みる
                    var intVal = enumValueName switch
                    {
                        "Required" => 2,
                        "Supported" => 1,
                        "ControllersAndHands" => 2,
                        _ => 1
                    };
                    field.SetValue(target, Enum.ToObject(enumType, intVal));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistSafe] SetOvrEnumField {fieldName}: {e.Message}");
            }
        }

        private static string ReplaceYamlInt(string yaml, string key, int value)
        {
            // verbatim string: \s は1文字のbackslash+s = regexで空白にマッチ（\\ だと2文字でリテラルbackslash）
            return System.Text.RegularExpressions.Regex.Replace(
                yaml,
                $@"(?m)^(\s*{System.Text.RegularExpressions.Regex.Escape(key)}:\s*)\d+(\s*)$",
                $"$1{value}$2");
        }

        private static void EnsureQuestFocusAwareManifestMetadata()
        {
            var manifestPath = Path.Combine(Application.dataPath, "Plugins", "Android", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                return;
            }

            var xml = File.ReadAllText(manifestPath);
            if (!xml.Contains("com.oculus.vr.focusaware"))
            {
                xml = xml.Replace(
                    "</activity>",
                    "    <meta-data android:name=\"com.oculus.vr.focusaware\" android:value=\"true\" />\n        </activity>");
                File.WriteAllText(manifestPath, xml);
            }

            if (xml.Contains("android:hardwareAccelerated=\"false\""))
            {
                xml = xml.Replace("android:hardwareAccelerated=\"false\"", "android:hardwareAccelerated=\"true\"");
                File.WriteAllText(manifestPath, xml);
            }
        }

        private static string GenerateDeterministicScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var xrOrigin = CreateXROrigin();
            var camera = FindMainCamera(xrOrigin);

            if (camera == null)
            {
                throw new Exception("Main camera could not be created.");
            }

            CreateAlwaysVisibleHud(camera);
            CreateTextFromLayouts(camera.transform);
            CreateRuntimeBootstrapObjects();

            var scenePath = "Assets/Scenes/MainScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[ArsistSafe] Scene saved: {scenePath}");

            return scenePath;
        }

        private static GameObject CreateXROrigin()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Arsist/Prefabs/XROrigin.prefab");
            GameObject xrOrigin;

            if (prefab != null)
            {
                xrOrigin = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                xrOrigin.name = "XR Origin";
            }
            else
            {
                xrOrigin = new GameObject("XR Origin");
                var cameraOffset = new GameObject("Camera Offset");
                cameraOffset.transform.SetParent(xrOrigin.transform, false);

                var mainCamera = new GameObject("Main Camera");
                mainCamera.tag = "MainCamera";
                mainCamera.transform.SetParent(cameraOffset.transform, false);
                mainCamera.AddComponent<Camera>();
                mainCamera.AddComponent<AudioListener>();
            }

            var setupType = FindType("Arsist.Runtime.XROriginSetup");
            if (setupType != null && xrOrigin.GetComponent(setupType) == null)
            {
                xrOrigin.AddComponent(setupType);
            }

            if (IsQuestTarget())
            {
                EnsureOvrManager(xrOrigin);
            }

            return xrOrigin;
        }

        private static Camera FindMainCamera(GameObject root)
        {
            var mainCamera = Camera.main;
            if (mainCamera == null && root != null)
            {
                mainCamera = root.GetComponentInChildren<Camera>(true);
            }
            if (mainCamera == null)
            {
                mainCamera = UnityEngine.Object.FindObjectOfType<Camera>();
            }

            if (mainCamera != null)
            {
                if (mainCamera.tag != "MainCamera") mainCamera.tag = "MainCamera";
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                mainCamera.nearClipPlane = 0.1f;
                mainCamera.farClipPlane = 100f;
            }

            return mainCamera;
        }

        private static void EnsureOvrManager(GameObject xrOrigin)
        {
            var type = FindType("OVRManager");
            if (type == null)
            {
                Debug.LogWarning("[ArsistSafe] OVRManager type not found.");
                return;
            }

            var existing = UnityEngine.Object.FindObjectOfType(type) as Component;
            var target = existing != null ? existing : xrOrigin.AddComponent(type) as Component;
            if (target == null) { Debug.LogWarning("[ArsistSafe] OVRManager could not be added."); return; }

            // runInBackground=true が設定されないとQuest起動待機中に15秒タイムアウトで強制終了される
            var rbField = type.GetField("_runInBackground",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (rbField != null) rbField.SetValue(target, true);

            var rbProp = type.GetProperty("runInBackground",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (rbProp != null && rbProp.CanWrite) rbProp.SetValue(target, true);

            Debug.Log("[ArsistSafe] OVRManager attached with runInBackground=true.");
        }

        private static void CreateAlwaysVisibleHud(Camera camera)
        {
            var canvasObject = new GameObject("Canvas_AlwaysVisible");
            canvasObject.transform.SetParent(camera.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0f, 1.4f);
            canvasObject.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(canvasObject, camera.gameObject.layer);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 2000;
            canvas.worldCamera = camera;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 100f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.localScale = new Vector3(0.0012f, 0.0012f, 0.0012f);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(canvasObject.transform, false);
            titleObject.layer = canvasObject.layer;

            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -32f);
            titleRect.sizeDelta = new Vector2(-64f, 140f);

            var titleText = titleObject.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 72;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            titleText.text = "Arsist HUD Ready";
        }

        private static void CreateTextFromLayouts(Transform hudRoot)
        {
            var layoutsPath = Path.Combine(Application.dataPath, "ArsistGenerated", "ui_layouts.json");
            if (!File.Exists(layoutsPath))
            {
                CreateInfoLine(hudRoot, 0, "ui_layouts.json not found");
                return;
            }

            var json = File.ReadAllText(layoutsPath);
            var layouts = JArray.Parse(json);
            var lines = new List<string>();

            foreach (var token in layouts)
            {
                var layout = token as JObject;
                if (layout == null) continue;
                var root = layout["root"] as JObject;
                CollectTextNodes(root, lines);
            }

            if (lines.Count == 0)
            {
                CreateInfoLine(hudRoot, 0, "No text nodes found in ui_layouts.json");
                return;
            }

            for (var i = 0; i < Mathf.Min(lines.Count, 8); i++)
            {
                CreateInfoLine(hudRoot, i, lines[i]);
            }
        }

        private static void CollectTextNodes(JObject node, List<string> lines)
        {
            if (node == null) return;

            var type = node["type"]?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
            if (type == "text" || type == "label" || type == "span")
            {
                var content = node["content"]?.ToString();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    lines.Add(content.Trim());
                }
            }

            var children = node["children"] as JArray;
            if (children == null) return;

            foreach (var childToken in children)
            {
                CollectTextNodes(childToken as JObject, lines);
            }
        }

        private static void CreateInfoLine(Transform hudRoot, int index, string line)
        {
            var go = new GameObject($"Line_{index + 1}");
            go.transform.SetParent(hudRoot, false);
            go.layer = hudRoot.gameObject.layer;

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(48f, -180f - (index * 96f));
            rect.sizeDelta = new Vector2(-96f, 80f);

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 54;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = new Color(1f, 0.95f, 0.25f, 1f);
            text.text = line;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void CreateRuntimeBootstrapObjects()
        {
            var root = new GameObject("[ArsistRuntimeSystems]");
            TryAddComponentByName(root, "Arsist.Runtime.DataFlow.ArsistDataFlowEngine");
            TryAddComponentByName(root, "Arsist.Runtime.Data.ArsistDataManager");
            TryAddComponentByName(root, "Arsist.Runtime.Events.ArsistEventBus");
            TryAddComponentByName(root, "Arsist.Runtime.Scene.ArsistSceneManager");
        }

        private static void TryAddComponentByName(GameObject target, string fullTypeName)
        {
            var type = FindType(fullTypeName);
            if (type == null) return;
            if (target.GetComponent(type) != null) return;
            target.AddComponent(type);
        }

        private static Type FindType(string fullTypeName)
        {
            var type = Type.GetType(fullTypeName);
            if (type != null) return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(fullTypeName, false);
                    if (type != null) return type;
                }
                catch
                {
                    // ignore and continue
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetTypes().FirstOrDefault(t => t.Name == fullTypeName || t.Name == fullTypeName.Split('.').Last());
                    if (type != null) return type;
                }
                catch
                {
                    // ignore and continue
                }
            }

            return null;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static void ExecuteBuild(string scenePath)
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

            var outputFile = _buildTarget == BuildTarget.Android
                ? Path.Combine(_outputPath, "MyARApp.apk")
                : Path.Combine(_outputPath, "Build");

            var options = BuildOptions.None;
            if (_developmentBuild)
            {
                options |= BuildOptions.Development;
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = outputFile,
                target = _buildTarget,
                options = options
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"Build failed with {report.summary.totalErrors} errors");
            }

            Debug.Log($"[ArsistSafe] APK created: {outputFile}");
        }

        private static void EnsureOpenXRReady()
        {
            try
            {
                var packageSettingsType = FindType("UnityEditor.XR.OpenXR.OpenXRPackageSettings");
                var getOrCreate = packageSettingsType?.GetMethod("GetOrCreateInstance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                getOrCreate?.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistSafe] OpenXRPackageSettings load warning: {e.Message}");
            }

            try
            {
                var openXrSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
                var activeInstance = openXrSettingsType?.GetProperty("ActiveBuildTargetInstance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)?.GetValue(null);
                if (activeInstance == null)
                {
                    var getSettings = openXrSettingsType?.GetMethod("GetSettingsForBuildTargetGroup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    getSettings?.Invoke(null, new object[] { BuildTargetGroup.Android });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistSafe] OpenXRSettings load warning: {e.Message}");
            }
        }

        private static void EnsureLinearColorSpace()
        {
            try
            {
                if (PlayerSettings.colorSpace != ColorSpace.Linear)
                {
                    PlayerSettings.colorSpace = ColorSpace.Linear;
                    Debug.Log("[ArsistSafe] PlayerSettings.colorSpace set to Linear.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistSafe] Failed to set Linear color space: {e.Message}");
            }
        }
    }
}
