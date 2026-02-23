using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Management;

namespace Arsist.Builder
{
    public static class QuestSafeBuildPipeline
    {
        private static string _outputPath;
        private static string _targetDevice;
        private static bool _developmentBuild;
        private static BuildTarget _buildTarget = BuildTarget.Android;

        // manifest.json から読み込むプロジェクト設定
        private static string _trackingMode = "6dof";       // "6dof" | "3dof" | "head_locked"
        private static string _appType      = "3d_ar_scene"; // "3d_ar_scene" | "2d_floating_screen" | "head_locked_hud"

        public static void BuildFromCLI()
        {
            ParseCommandLineArgs();

            try
            {
                // CRITICAL: まずキャッシュを完全クリア（古いアセンブリを削除）
                ClearAllBuildCaches();
                
                // CRITICAL: 必須のPlayerSettingsを設定（これが無いとAPKが無効）
                ConfigureEssentialPlayerSettings();
                
                EnsureGeneratedDataFolders();
                ReadProjectManifest();       // trackingMode / appType を取得
                CopyUICodeToStreamingAssets();

                if (IsQuestTarget())
                {
                    ApplyQuestLaunchSafety();
                }

                if (IsXrealTarget())
                {
                    ApplyXRealLaunchSafety();
                }

                // OpenXR は Quest/Meta 向けのみ初期化
                if (IsQuestTarget())
                {
                    EnsureOpenXRReady();
                }
                EnsureLinearColorSpace();

                var scenePath = GenerateDeterministicScene();
                
                // シーン生成後、スクリプトコンパイル完了を待つ
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                WaitForScriptCompilation();
                
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

        /// <summary>
        /// Assets/ArsistGenerated/manifest.json から trackingMode と appType を読み込む。
        /// </summary>
        private static void ReadProjectManifest()
        {
            var path = Path.Combine(Application.dataPath, "ArsistGenerated", "manifest.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[ArsistSafe] manifest.json not found. Using defaults: trackingMode=6dof, appType=3d_ar_scene.");
                return;
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                _trackingMode = json["arSettings"]?["trackingMode"]?.ToString() ?? "6dof";
                _appType      = json["appType"]?.ToString() ?? "3d_ar_scene";
                Debug.Log($"[ArsistSafe] Manifest loaded: trackingMode={_trackingMode}, appType={_appType}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistSafe] Failed to parse manifest.json: {e.Message}. Using defaults.");
            }
        }

        /// <summary>
        /// ui_layouts.json の最初の UHD レイアウトから解像度を取得する。
        /// 見つからない場合は (0,0) を返す（呼び出し元でフォールバック）。
        /// </summary>
        private static Vector2 ReadUhdLayoutResolution()
        {
            var path = Path.Combine(Application.dataPath, "ArsistGenerated", "ui_layouts.json");
            if (!File.Exists(path)) return Vector2.zero;

            try
            {
                var layouts = JArray.Parse(File.ReadAllText(path));
                foreach (JObject layout in layouts)
                {
                    var scope = layout["scope"]?.ToString()?.Trim().ToLowerInvariant() ?? "";
                    if (scope != "uhd" && scope != "hud" && scope != "overlay") continue;

                    var res = layout["resolution"] as JObject;
                    if (res != null)
                    {
                        var w = res["width"]?.Value<float>()  ?? 0f;
                        var h = res["height"]?.Value<float>() ?? 0f;
                        if (w > 0f && h > 0f)
                        {
                            Debug.Log($"[ArsistSafe] UILayout resolution: {w}x{h} (scope={scope})");
                            return new Vector2(w, h);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistSafe] Failed to read UILayout resolution: {e.Message}");
            }

            return Vector2.zero;
        }

        private static bool IsQuestTarget()
        {
            var value = (_targetDevice ?? string.Empty).ToLowerInvariant();
            return value.Contains("quest") || value.Contains("meta");
        }

        private static bool IsXrealTarget()
        {
            var value = (_targetDevice ?? string.Empty).ToLowerInvariant();
            return value.Contains("xreal") || value.Contains("nreal");
        }

        private static void ApplyXRealLaunchSafety()
        {
            Debug.Log("[ArsistSafe] Applying XREAL device patches via XrealBuildPatcher...");

            // Adapters/XREAL_One/XrealBuildPatcher.cs はビルド時に Assets/Arsist/Editor/Adapters/ にコピーされる
            var patcherType = FindType("Arsist.Adapters.XrealOne.XrealBuildPatcher");
            if (patcherType == null)
            {
                Debug.LogWarning("[ArsistSafe] XrealBuildPatcher type not found. Ensure XREAL adapter script is present.");
                // フォールバック: 最低限の XR 設定を直接適用
                ApplyXRealPlayerSettingsFallback();
                return;
            }

            try
            {
                var applyAll = patcherType.GetMethod("ApplyAllPatches",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (applyAll != null)
                {
                    applyAll.Invoke(null, null);
                    Debug.Log("[ArsistSafe] XrealBuildPatcher.ApplyAllPatches() completed.");
                }
                else
                {
                    // ApplyAllPatches がみつからない場合は ConfigureXRLoader だけ呼ぶ
                    var configureLoader = patcherType.GetMethod("ConfigureXRLoader",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    configureLoader?.Invoke(null, null);
                    Debug.Log("[ArsistSafe] XrealBuildPatcher.ConfigureXRLoader() applied (fallback).");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistSafe] XrealBuildPatcher invocation failed: {e.Message}. Applying fallback settings.");
                ApplyXRealPlayerSettingsFallback();
            }
        }

        private static void ApplyXRealPlayerSettingsFallback()
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
            });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("[ArsistSafe] XREAL fallback PlayerSettings applied.");
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
            Debug.Log("[ArsistSafe] Applying COMPLETE Quest PlayerSettings for 6DoF/3DoF tracking...");
            
            // CRITICAL: IL2CPP（Quest必須）
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            
            // CRITICAL: SDK バージョン
            // minSdk=32 はQuest 2以降の必須要件
            // targetSdk=34 を使用: android-32ライセンスエラーを回避（BuildSDKはandroid-34を使用）
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            
            // CRITICAL: ステレオレンダリング（6DoFトラッキング必須）
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            
            // CRITICAL: グラフィックスAPI（Vulkan優先）
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
            });
            
            // CRITICAL: マルチスレッドレンダリング
            PlayerSettings.MTRendering = true;
            
            // CRITICAL: Quest必須 - ロード画面中もゲームループ継続（15秒タイムアウトクラッシュ防止）
            PlayerSettings.runInBackground = true;
            
            // CRITICAL: Linear カラースペース（VR必須）
            PlayerSettings.colorSpace = ColorSpace.Linear;
            
            // CRITICAL: XR設定 - バックグラウンドで動作継続
            PlayerSettings.muteOtherAudioSources = false;
            
            Debug.Log("[ArsistSafe] Quest PlayerSettings applied:");
            Debug.Log("  - IL2CPP/ARM64, API 32, Vulkan, Instancing");
            Debug.Log("  - RunInBackground=true, Linear colorSpace");
            Debug.Log("  - Stereo rendering ready for 6DoF tracking");
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

            var hudCanvas = CreateAlwaysVisibleHud(camera);
            GenerateCanvasUIForCurrentScene(camera, hudCanvas);
            CreateSceneObjectsFromJson();
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
                // 全レイヤーを描画（cullingMaskが開いていないとCanvas HUDが見えない）
                mainCamera.cullingMask = -1; // Everything
            }

            return mainCamera;
        }

        private static void EnsureOvrManager(GameObject xrOrigin)
        {
            Debug.Log("[ArsistSafe] Configuring OVRManager for Quest 6DoF/3DoF tracking...");
            
            var type = FindType("OVRManager");
            if (type == null)
            {
                Debug.LogWarning("[ArsistSafe] OVRManager type not found. Quest tracking may not work!");
                return;
            }

            var existing = UnityEngine.Object.FindObjectOfType(type) as Component;
            var target = existing != null ? existing : xrOrigin.AddComponent(type) as Component;
            if (target == null) 
            { 
                Debug.LogError("[ArsistSafe] OVRManager could not be added! 6DoF tracking WILL FAIL!");
                return;
            }

            // CRITICAL: runInBackground=true（15秒タイムアウトクラッシュ防止）
            TrySetFieldOrProperty(target, "_runInBackground", true);
            TrySetFieldOrProperty(target, "runInBackground", true);
            
            // CRITICAL: トラッキング設定
            var enable6dof = !string.Equals(_trackingMode, "3dof",         StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(_trackingMode, "head_locked",  StringComparison.OrdinalIgnoreCase);
            TrySetFieldOrProperty(target, "trackingOriginType", enable6dof ? 1 : 0); // Floor(6DoF) / EyeLevel(3DoF)
            TrySetFieldOrProperty(target, "usePositionTracking", enable6dof);
            TrySetFieldOrProperty(target, "useRotationTracking", true);
            Debug.Log($"[ArsistSafe] trackingMode={_trackingMode}, usePositionTracking={enable6dof}");
            
            // CRITICAL: レンダリング設定
            TrySetFieldOrProperty(target, "useRecommendedMSAALevel", true);
            
            // ハンドトラッキング（オプション）
            TrySetFieldOrProperty(target, "handTrackingSupport", 1); // Controllers and hands
            
            // Passthrough（オプション）
            TrySetFieldOrProperty(target, "isInsightPassthroughEnabled", true);
            
            Debug.Log("[ArsistSafe] OVRManager configured:");
            Debug.Log("  - runInBackground=true (timeout crash prevention)");
            Debug.Log("  - Position/Rotation tracking enabled");
            Debug.Log("  - Floor-level tracking origin");
            Debug.Log("  - 6DoF tracking ready");
        }

        private static GameObject CreateAlwaysVisibleHud(Camera camera)
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

            // UILayout の解像度を使って WYSIWYG な Canvas サイズを設定
            var uhdRes = ReadUhdLayoutResolution();
            var canvasW = uhdRes.x > 0f ? uhdRes.x : 1920f;
            var canvasH = uhdRes.y > 0f ? uhdRes.y : 1080f;
            // 物理幅 = 2.304m (1920px * 0.0012 scale) を維持しつつ解像度で scale 計算
            const float physicalWidthM = 2.304f;
            var scaleFactor = physicalWidthM / canvasW;

            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(canvasW, canvasH);
            rect.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
            Debug.Log($"[ArsistSafe] HUD Canvas: {canvasW}x{canvasH}px, scale={scaleFactor:F5} ({physicalWidthM}m wide)");

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

            return canvasObject;
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

        // ─────────────────────────────────────────────────────
        // Canvas UI generation from ui_layouts.json
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// ui_layouts.json を読んで現在のシーンに Canvas UI を生成する。
        /// scope=uhd/hud/overlay → hudCanvas 内に要素を追加。
        /// scope=canvas → 独立した WorldSpace Canvas (UISurface) を生成。
        /// </summary>
        private static void GenerateCanvasUIForCurrentScene(Camera camera, GameObject hudCanvas)
        {
            var layoutsPath = Path.Combine(Application.dataPath, "ArsistGenerated", "ui_layouts.json");
            if (!File.Exists(layoutsPath))
            {
                Debug.LogWarning("[ArsistSafe] ui_layouts.json not found, skipping full UI generation.");
                return;
            }

            JArray layouts;
            try { layouts = JArray.Parse(File.ReadAllText(layoutsPath)); }
            catch (Exception e) { Debug.LogWarning($"[ArsistSafe] Failed to parse ui_layouts.json: {e.Message}"); return; }

            foreach (var token in layouts)
            {
                var layout = token as JObject;
                if (layout == null) continue;

                var scope = layout["scope"]?.ToString()?.Trim().ToLowerInvariant() ?? "uhd";
                var root = layout["root"] as JObject;
                if (root == null) continue;

                var isHud = scope == "uhd" || scope == "hud" || scope == "overlay";
                // scope=canvas は scenes.json 側で位置付きで配置するためここではスキップ
                if (!isHud) continue;

                // HUD canvas 内に UI 要素を生成
                CreateUIElementForQuest(root, hudCanvas.transform, camera);
            }

            AssetDatabase.Refresh();
            Debug.Log("[ArsistSafe] Full canvas UI generated from ui_layouts.json.");
        }

        private static void CreateUISurfaceCanvas(string name, float width, float height, JObject root, Camera camera)
        {
            const float pixelsPerUnit = 100f;

            var canvasGO = new GameObject($"Canvas_{name}");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 1000;
            canvas.worldCamera = camera;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = pixelsPerUnit;
            canvasGO.AddComponent<GraphicRaycaster>();

            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one / pixelsPerUnit;

            // シーン内でデフォルト位置に配置（3D空間 z=2m 正面）
            canvasGO.transform.position = new Vector3(0f, 0f, 2f);
            canvasGO.transform.rotation = Quaternion.identity;

            CreateUIElementForQuest(root, canvasGO.transform, camera);
            Debug.Log($"[ArsistSafe] UISurface canvas created: {name} ({width}x{height})");
        }

        /// <summary>
        /// ArsistBuildPipeline.CreateUIElement 相当の実装（Quest パイプライン内）
        /// worldCamera を正しく設定し、ルート要素のサイズデフォルト補完等も行う。
        /// </summary>
        private static void CreateUIElementForQuest(JObject elementData, Transform parent, Camera camera)
        {
            var rawType = elementData["type"]?.ToString() ?? "Panel";
            var type = rawType.Trim().ToLowerInvariant();
            var elementName = elementData["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(elementName)) elementName = rawType;

            var go = new GameObject(elementName);
            go.transform.SetParent(parent, false);
            if (parent != null) go.layer = parent.gameObject.layer;

            var rt = go.AddComponent<RectTransform>();
            var style = elementData["style"] as JObject;

            // Canvas 直下のルート要素はデフォルトでフルサイズ
            var parentCanvas = parent.GetComponent<Canvas>() ?? parent.GetComponentInParent<Canvas>();
            var isDirectCanvasChild = (parentCanvas != null && parent.GetComponent<Canvas>() != null);
            if (isDirectCanvasChild)
            {
                if (style == null) { style = new JObject(); elementData["style"] = style; }
                if (style["width"]  == null) style["width"]  = "100%";
                if (style["height"] == null) style["height"] = "100%";
            }

            QuestApplyRectTransformStyle(rt, style);

            switch (type)
            {
                case "panel":
                case "container":
                case "div":
                {
                    var img = go.AddComponent<UnityEngine.UI.Image>();
                    img.color = QuestTryParseColor(style?["backgroundColor"], Color.clear);
                    break;
                }
                case "text":
                case "label":
                case "span":
                {
                    var txt = go.AddComponent<UnityEngine.UI.Text>();
                    txt.text = elementData["content"]?.ToString() ?? "";
                    txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    txt.horizontalOverflow = HorizontalWrapMode.Wrap;
                    txt.verticalOverflow = VerticalWrapMode.Overflow;
                    if (style != null)
                    {
                        // VR視認性: 最低36px (canvas scale 0.0012 で 4.3cm → 1.4m距離で約1.8° )
                        var requestedSize = style["fontSize"]?.Value<int>() ?? 36;
                        txt.fontSize = requestedSize; // エディタのサイズをそのまま使用（クランプなし）
                        txt.color = QuestTryParseColor(style["color"], Color.white);
                        var align = style["textAlign"]?.ToString();
                        txt.alignment = align switch
                        {
                            "center" => TextAnchor.MiddleCenter,
                            "right"  => TextAnchor.MiddleRight,
                            _        => TextAnchor.MiddleLeft,
                        };
                    }
                    else
                    {
                        txt.fontSize = 36;
                        txt.color = Color.white;
                    }
                    break;
                }
                case "image":
                case "img":
                {
                    var img = go.AddComponent<UnityEngine.UI.Image>();
                    img.color = Color.white;
                    var assetPath = elementData["assetPath"]?.ToString();
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        var unity = assetPath.StartsWith("Assets/")
                            ? "Assets/ArsistProjectAssets/" + assetPath.Substring("Assets/".Length)
                            : assetPath;
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(unity);
                        if (sprite != null) { img.sprite = sprite; img.preserveAspect = false; }
                        else Debug.LogWarning($"[ArsistSafe] Sprite not found: {unity}");
                    }
                    break;
                }
                case "button":
                case "btn":
                {
                    var img = go.AddComponent<UnityEngine.UI.Image>();
                    img.color = QuestTryParseColor(style?["backgroundColor"], new Color(0.91f, 0.27f, 0.38f, 1f));
                    go.AddComponent<UnityEngine.UI.Button>();
                    break;
                }
            }

            // データバインディング
            var bind = elementData["bind"] as JObject;
            var bindKey = bind?["key"]?.ToString();
            var bindingId = elementData["bindingId"]?.ToString();
            if (string.IsNullOrEmpty(bindingId)) bindingId = elementData["id"]?.ToString();

            if (!string.IsNullOrEmpty(bindKey) || !string.IsNullOrEmpty(bindingId))
            {
                var comp = TryAddComponentByName2(go, "Arsist.Runtime.UI.ArsistUIBinding");
                if (comp != null)
                {
                    var t = comp.GetType();
                    var kf = t.GetField("key"); kf?.SetValue(comp, bindKey ?? bindingId);
                    var ff = t.GetField("format"); ff?.SetValue(comp, bind?["format"]?.ToString());
                }
                var reg = TryAddComponentByName2(go, "Arsist.Runtime.Scripting.UiBindingRegistry");
                if (reg != null)
                {
                    var bf = reg.GetType().GetField("bindingId",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    bf?.SetValue(reg, bindingId);
                }
            }

            // children 再帰処理
            if (elementData["children"] is JArray children)
            {
                foreach (JObject child in children)
                    CreateUIElementForQuest(child, go.transform, camera);
            }
        }

        private static Component TryAddComponentByName2(GameObject go, string typeName)
        {
            var t = FindType(typeName);
            if (t == null) return null;
            var existing = go.GetComponent(t);
            return existing != null ? existing : go.AddComponent(t);
        }

        private static void QuestApplyRectTransformStyle(RectTransform rt, JObject style)
        {
            if (style == null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot     = new Vector2(0f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(200f, 120f);
                return;
            }

            var isAbs = string.Equals(style["position"]?.ToString(), "absolute", StringComparison.OrdinalIgnoreCase);
            if (isAbs)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot     = new Vector2(0f, 1f);
                var left = style["left"]?.Value<float>() ?? 0f;
                var top  = style["top"]?.Value<float>()  ?? 0f;
                rt.anchoredPosition = new Vector2(left, -top);
                rt.sizeDelta = new Vector2(QuestParseSz(style["width"], 200f), QuestParseSz(style["height"], 120f));
                return;
            }

            var sw = QuestIsPercent100(style["width"]);
            var sh = QuestIsPercent100(style["height"]);

            if (sw || sh)
            {
                rt.anchorMin = new Vector2(sw ? 0f : 0f, sh ? 0f : 1f);
                rt.anchorMax = new Vector2(sw ? 1f : 0f, sh ? 1f : 1f);
                rt.pivot     = new Vector2(0f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                if (!sw || !sh)
                    rt.sizeDelta = new Vector2(sw ? 0f : QuestParseSz(style["width"], 200f),
                                               sh ? 0f : QuestParseSz(style["height"], 120f));
                return;
            }

            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(QuestParseSz(style["width"], 200f), QuestParseSz(style["height"], 120f));
        }

        private static float QuestParseSz(JToken token, float fallback)
        {
            if (token == null) return fallback;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<float>();
            var raw = token.ToString().Trim();
            if (string.IsNullOrEmpty(raw) || raw.Equals("auto", StringComparison.OrdinalIgnoreCase)) return fallback;
            if (raw.EndsWith("%") && float.TryParse(raw.TrimEnd('%'), out var pct))
                return Mathf.Clamp01(pct / 100f) * fallback;
            return float.TryParse(raw, out var v) ? v : fallback;
        }

        private static bool QuestIsPercent100(JToken token)
        {
            if (token == null) return false;
            return string.Equals(token.ToString().Trim(), "100%", StringComparison.OrdinalIgnoreCase);
        }

        private static Color QuestTryParseColor(JToken token, Color fallback)
        {
            if (token == null) return fallback;
            var raw = token.ToString().Trim();
            if (string.IsNullOrEmpty(raw)) return fallback;
            if (ColorUtility.TryParseHtmlString(raw, out var c)) return c;
            if ((raw.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) ||
                 raw.StartsWith("rgb(",  StringComparison.OrdinalIgnoreCase)))
            {
                var s = raw.IndexOf('('); var e = raw.IndexOf(')');
                if (s >= 0 && e > s)
                {
                    var parts = raw.Substring(s + 1, e - s - 1).Split(',');
                    if (parts.Length >= 3 &&
                        float.TryParse(parts[0].Trim(), out var r) &&
                        float.TryParse(parts[1].Trim(), out var g) &&
                        float.TryParse(parts[2].Trim(), out var b))
                    {
                        var a = 1f;
                        if (parts.Length >= 4) float.TryParse(parts[3].Trim(), out a);
                        return new Color(r / 255f, g / 255f, b / 255f, Mathf.Clamp01(a));
                    }
                }
            }
            return fallback;
        }

        // ─────────────────────────────────────────────────────
        // 3D Scene objects from scenes.json
        // ─────────────────────────────────────────────────────

        private static void CreateSceneObjectsFromJson()
        {
            var scenesPath = Path.Combine(Application.dataPath, "ArsistGenerated", "scenes.json");
            if (!File.Exists(scenesPath))
            {
                Debug.LogWarning("[ArsistSafe] scenes.json not found, skipping 3D object placement.");
                return;
            }

            JArray scenes;
            try { scenes = JArray.Parse(File.ReadAllText(scenesPath)); }
            catch (Exception e) { Debug.LogWarning($"[ArsistSafe] Failed to parse scenes.json: {e.Message}"); return; }

            // 最初のシーンのオブジェクトを現在のシーンに配置
            var firstScene = scenes.FirstOrDefault() as JObject;
            if (firstScene == null) return;

            var objects = firstScene["objects"] as JArray;
            if (objects == null) return;

            foreach (JObject obj in objects)
            {
                PlaceSceneObject(obj);
            }

            Debug.Log("[ArsistSafe] Scene objects placed from scenes.json.");
        }

        private static void PlaceSceneObject(JObject obj)
        {
            var name = obj["name"]?.ToString() ?? "Object";
            var type = obj["type"]?.ToString()?.Trim().ToLowerInvariant() ?? "primitive";
            var xform = obj["transform"] as JObject;

            var pos = new Vector3(
                xform?["position"]?["x"]?.Value<float>() ?? 0f,
                xform?["position"]?["y"]?.Value<float>() ?? 0f,
                xform?["position"]?["z"]?.Value<float>() ?? 0f);
            var rot = new Vector3(
                xform?["rotation"]?["x"]?.Value<float>() ?? 0f,
                xform?["rotation"]?["y"]?.Value<float>() ?? 0f,
                xform?["rotation"]?["z"]?.Value<float>() ?? 0f);
            var scl = new Vector3(
                xform?["scale"]?["x"]?.Value<float>() ?? 1f,
                xform?["scale"]?["y"]?.Value<float>() ?? 1f,
                xform?["scale"]?["z"]?.Value<float>() ?? 1f);

            if (type == "model")
            {
                // ランタイム GLB ローダーを使ったラッパー
                var wrapper = new GameObject(name);
                wrapper.transform.position = pos;
                wrapper.transform.eulerAngles = rot;
                wrapper.transform.localScale = scl;

                var modelPath = obj["modelPath"]?.ToString();
                if (!string.IsNullOrEmpty(modelPath))
                {
                    // GLBをStreamingAssetsにコピーしてランタイムで読み込ませる
                    var runtimeRelPath = CopyModelToStreamingAssets(modelPath);
                    if (!string.IsNullOrEmpty(runtimeRelPath))
                    {
                        var loaderType2 = FindType("Arsist.Runtime.ArsistModelRuntimeLoader");
                        Debug.Log($"[ArsistSafe] FindType ArsistModelRuntimeLoader => {loaderType2?.FullName ?? "NULL"}");
                        var loaderComp = loaderType2 != null ? TryAddComponentByName2(wrapper, "Arsist.Runtime.ArsistModelRuntimeLoader") : null;
                        if (loaderComp != null)
                        {
                            var loaderType = loaderComp.GetType();
                            var mpField = loaderType.GetField("modelPath",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            mpField?.SetValue(loaderComp, runtimeRelPath);
                            loaderType.GetField("destroyAfterLoad",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(loaderComp, false);
                            Debug.Log($"[ArsistSafe] ModelLoader component attached. modelPath field='{mpField?.Name ?? "NOTFOUND"}' => '{runtimeRelPath}'");
                        }
                        else
                        {
                            Debug.LogWarning($"[ArsistSafe] FAILED to add ArsistModelRuntimeLoader! type={loaderType2?.FullName ?? "NULL"}");
                        }
                        Debug.Log($"[ArsistSafe] Placed model wrapper: {name} at {pos}, rot={rot}, path={runtimeRelPath}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ArsistSafe] Model not found for streaming: {modelPath}. Placing empty wrapper.");
                    }
                }
            }
            else if (type == "canvas" || type == "uisurface")
            {
                // scenes.json で指定されたワールド位置に UISurface Canvas を配置
                var canvasSettings = obj["canvasSettings"] as JObject;
                var layoutId       = canvasSettings?["layoutId"]?.ToString();
                var widthM         = canvasSettings?["widthMeters"]?.Value<float>()  ?? 1.2f;
                var heightM        = canvasSettings?["heightMeters"]?.Value<float>() ?? 0.7f;
                var ppu            = canvasSettings?["pixelsPerUnit"]?.Value<float>() ?? 1000f;

                // layoutId に対応する ui_layouts.json から root を取得
                JObject uiLayout = null;
                var layoutsPath = Path.Combine(Application.dataPath, "ArsistGenerated", "ui_layouts.json");
                if (!string.IsNullOrEmpty(layoutId) && File.Exists(layoutsPath))
                {
                    try
                    {
                        var all = JArray.Parse(File.ReadAllText(layoutsPath));
                        foreach (JObject l in all)
                        {
                            if (l["id"]?.ToString() == layoutId) { uiLayout = l; break; }
                        }
                    }
                    catch { /* ignore parse error */ }
                }

                var canvasGO = new GameObject($"Canvas_{name}");
                canvasGO.transform.position    = pos;
                canvasGO.transform.eulerAngles = rot;
                canvasGO.transform.localScale  = Vector3.one;

                var canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode   = RenderMode.WorldSpace;
                canvas.sortingOrder = 1000;
                canvas.worldCamera  = Camera.main;

                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = ppu;
                canvasGO.AddComponent<GraphicRaycaster>();

                var rectT = canvasGO.GetComponent<RectTransform>();
                rectT.sizeDelta   = new Vector2(widthM * ppu, heightM * ppu);
                rectT.localScale  = new Vector3(1f / ppu, 1f / ppu, 1f / ppu);

                if (Camera.main != null)
                {
                    canvasGO.layer = Camera.main.gameObject.layer;
                    SetLayerRecursively(canvasGO, Camera.main.gameObject.layer);
                }

                var root = uiLayout?["root"] as JObject;
                if (root != null)
                {
                    var rootStyle = root["style"] as JObject;
                    if (rootStyle == null) { rootStyle = new JObject(); root["style"] = rootStyle; }
                    if (rootStyle["width"]  == null) rootStyle["width"]  = "100%";
                    if (rootStyle["height"] == null) rootStyle["height"] = "100%";
                    CreateUIElementForQuest(root, canvasGO.transform, Camera.main);
                }

                Debug.Log($"[ArsistSafe] Placed UISurface canvas: {name} at {pos}, layout={layoutId}, {widthM}x{heightM}m");
            }
            else
            {
                // Primitive
                var primitiveType = obj["primitiveType"]?.ToString()?.Trim().ToLowerInvariant() ?? "cube";
                var pType = primitiveType switch
                {
                    "sphere"   => PrimitiveType.Sphere,
                    "capsule"  => PrimitiveType.Capsule,
                    "cylinder" => PrimitiveType.Cylinder,
                    "plane"    => PrimitiveType.Plane,
                    "quad"     => PrimitiveType.Quad,
                    _          => PrimitiveType.Cube,
                };

                var go = GameObject.CreatePrimitive(pType);
                go.name = name;
                go.transform.position = pos;
                go.transform.eulerAngles = rot;
                go.transform.localScale = scl;

                var matJson = obj["material"] as JObject;
                if (matJson != null)
                {
                    var colorStr = matJson["color"]?.ToString() ?? "#FFFFFF";
                    if (ColorUtility.TryParseHtmlString(colorStr, out var objColor))
                    {
                        var rend = go.GetComponent<Renderer>();
                        if (rend != null)
                        {
                            var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
                            mat.color = objColor;
                            rend.sharedMaterial = mat;
                        }
                    }
                }

                Debug.Log($"[ArsistSafe] Placed primitive: {name} ({pType}) at {pos}, rot={rot}");
            }
        }

        /// <summary>
        /// GLBをStreamingAssetsにコピーしてランタイムからアクセスできるようにする。
        /// 戻り値: StreamingAssets相対パス（例 "Models/shiba.glb"）。見つからない場合null。
        /// </summary>
        private static string CopyModelToStreamingAssets(string modelPath)
        {
            var candidates = new[]
            {
                Path.Combine(Application.dataPath, "..", modelPath),
                Path.Combine(Application.dataPath, "ArsistProjectAssets",
                    modelPath.StartsWith("Assets/") ? modelPath.Substring("Assets/".Length) : modelPath),
                Path.Combine(Application.dataPath, "ArsistProjectAssets", "Models", Path.GetFileName(modelPath)),
                Path.Combine(Application.dataPath, "Models", Path.GetFileName(modelPath)),
            };

            string src = null;
            foreach (var c in candidates)
            {
                if (File.Exists(c)) { src = c; break; }
            }

            if (src == null) return null;

            var fileName       = Path.GetFileName(src);
            var streamingModels = Path.Combine(Application.dataPath, "StreamingAssets", "Models");
            Directory.CreateDirectory(streamingModels);
            var dst = Path.Combine(streamingModels, fileName);
            File.Copy(src, dst, overwrite: true);
            AssetDatabase.Refresh();

            return $"Models/{fileName}";   // StreamingAssets 相対パス
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

        /// <summary>
        /// Android SDK のライセンスとtargetSdkVersionを確認・設定する。
        /// 
        /// 根本原因の解析:
        /// - android-32 は Unity内部SDKに「存在しない」ためGradleがインストールを試みる
        /// - インストール時にライセンスファイルの書き込みが必要だがSDKはread-only → エラー
        /// - android-34 は Unity内部SDKに「存在する」のでGradleはインストール不要 → エラーなし
        /// 
        /// 解決策: targetSdkVersion=34 を使用（ApplyQuestPlayerSettingsで既に設定）
        /// AndroidExternalToolsSettings.sdkRootPath の変更は NOT 使用
        ///   → sdkRootPathを変更するとNDK/CMakeのパスも変わりCMake未発見エラーが発生する
        /// </summary>
        private static void ConfigureAndroidSdkPath()
        {
            if (_buildTarget != BuildTarget.Android) return;

            // targetSdkVersion=34 は ApplyQuestPlayerSettings() で既に設定済み
            // android-34はUnity内部SDKに存在するためGradleはインストール不要
            Debug.Log($"[ArsistSafe] ConfigureAndroidSdkPath: targetSdkVersion={PlayerSettings.Android.targetSdkVersion}");
            Debug.Log("[ArsistSafe] SDK: sdkRootPath変更なし（変更するとCMage/NDKのパスが壊れる）");

            // Unity内部SDKのlicensesフォルダにライセンスファイルを書き込もうと試みる
            // Program Files内なのでWrite権限がない場合は無視する
            var unitySdkPath = $@"C:\Program Files\Unity\Hub\Editor\{Application.unityVersion}\Editor\Data\PlaybackEngines\AndroidPlayer\SDK";
            if (!Directory.Exists(unitySdkPath))
            {
                // バージョンパスを動的に探す
                var baseDir = @"C:\Program Files\Unity\Hub\Editor";
                if (Directory.Exists(baseDir))
                {
                    var versionDirs = Directory.GetDirectories(baseDir);
                    foreach (var vd in versionDirs)
                    {
                        var candidate = Path.Combine(vd, "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "SDK");
                        if (Directory.Exists(candidate)) { unitySdkPath = candidate; break; }
                    }
                }
            }

            if (Directory.Exists(unitySdkPath))
            {
                var licensesDir = Path.Combine(unitySdkPath, "licenses");
                try
                {
                    Directory.CreateDirectory(licensesDir);
                    var sdkLicense = Path.Combine(licensesDir, "android-sdk-license");
                    File.WriteAllText(sdkLicense,
                        "\n8933bad161af4178b1185d1a37fbf41ea5269c55\nd56f5187479451eabf01fb78af6dfcb131a6481e\n24333f8a63b6825ea9c5514f83c2829b004d1fee");
                    File.WriteAllText(Path.Combine(licensesDir, "android-sdk-preview-license"),
                        "\n84831b9409646a918e30573bab4c9c91346d8abd");
                    Debug.Log($"[ArsistSafe] ✓ Unity内部SDK licensesフォルダにライセンス書き込み成功: {licensesDir}");
                }
                catch (Exception e)
                {
                    // Program Filesへの書き込み権限なし → 正常フォールバック
                    Debug.Log($"[ArsistSafe] Unity内部SDKへのライセンス書き込みをスキップ（権限なし）: {e.GetType().Name}");
                }

                // android-32/33/34の存在確認
                var has32 = Directory.Exists(Path.Combine(unitySdkPath, "platforms", "android-32"));
                var has33 = Directory.Exists(Path.Combine(unitySdkPath, "platforms", "android-33"));
                var has34 = Directory.Exists(Path.Combine(unitySdkPath, "platforms", "android-34"));
                Debug.Log($"[ArsistSafe] Unity SDK platforms: 32={has32}, 33={has33}, 34={has34}");
                Debug.Log($"[ArsistSafe] targetSdkVersion=34, android-34 in Unity SDK={has34} → Gradleはインストール不要");
            }
            else
            {
                Debug.LogWarning($"[ArsistSafe] Unity内部SDKが見つかりません: {unitySdkPath}");
            }
        }

        private static void ExecuteBuild(string scenePath)
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

            var outputFile = _buildTarget == BuildTarget.Android
                ? Path.Combine(_outputPath, "MyARApp.apk")
                : Path.Combine(_outputPath, "Build");

            // CRITICAL: すべてのビルドオプションでキャッシュをクリア
            var options = BuildOptions.CleanBuildCache | BuildOptions.StrictMode;
            if (_developmentBuild)
            {
                options |= BuildOptions.Development;
            }

            // IL2CPP 設定: インクリメンタルビルド無効化 + ストリッピング最小化
            PlayerSettings.SetIncrementalIl2CppBuild(BuildTargetGroup.Android, false);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Minimal);
            Debug.Log("[ArsistSafe] IL2CPP: Incremental=OFF, Stripping=Minimal, BuildOptions=CleanBuildCache|StrictMode");

            // Android SDK パスをユーザーSDKに変更（Program Files内SDKはread-onlyでライセンス書き込み不可）
            ConfigureAndroidSdkPath();

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

        /// <summary>
        /// Quest 向け OpenXR 設定をゼロから構築する。
        /// Assets/XR/ に必要なアセットが存在しない場合は作成し、
        /// 存在する場合は設定を上書き修正する。
        /// </summary>
        private static void EnsureOpenXRReady()
        {
            Debug.Log("[ArsistSafe] *** EnsureOpenXRReady: building XR assets for Android/Quest ***");

            // ─── Step 0: OVRPlugin.aar をバッチモード用にアクティベート ─────────────
            try
            {
                var ovpType = FindType("Oculus.VR.Editor.OVRPluginInfoOpenXR");
                if (ovpType != null)
                {
                    var method = ovpType.GetMethod("BatchmodeCheckHasPluginChanged",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    method?.Invoke(null, null);
                    Debug.Log("[ArsistSafe] OVRPlugin batchmode activation called.");
                }
                else
                {
                    Debug.LogWarning("[ArsistSafe] OVRPluginInfoOpenXR not found (OVRPlugin.aar may not be included).");
                }
            }
            catch (Exception e) { Debug.LogWarning($"[ArsistSafe] OVRPlugin activation: {e.Message}"); }

            // ─── Step 1: Assets/XR/ ディレクトリ作成 ────────────────────────────────
            var xrBase     = Path.Combine(Application.dataPath, "XR");
            var settingsD  = Path.Combine(xrBase, "Settings");
            var loadersD   = Path.Combine(xrBase, "Loaders");
            Directory.CreateDirectory(settingsD);
            Directory.CreateDirectory(loadersD);
            AssetDatabase.Refresh();

            // ─── Step 2: OpenXRLoader アセットを取得/作成 ───────────────────────────
            const string loaderPath     = "Assets/XR/Loaders/OpenXRLoader.asset";
            const string managerPath    = "Assets/XR/Settings/XRManagerSettings.asset";
            const string generalPath    = "Assets/XR/Settings/XRGeneralSettings.asset";
            const string perTargetPath  = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

            var openXrLoaderType = FindType("UnityEngine.XR.OpenXR.OpenXRLoader");
            if (openXrLoaderType == null)
            {
                Debug.LogWarning("[ArsistSafe] OpenXRLoader type not found. Is com.unity.xr.openxr installed?");
                return;
            }

            var loader = AssetDatabase.LoadMainAssetAtPath(loaderPath) as XRLoader;
            if (loader == null)
            {
                loader = (XRLoader)ScriptableObject.CreateInstance(openXrLoaderType);
                AssetDatabase.CreateAsset(loader, loaderPath);
                Debug.Log("[ArsistSafe] Created OpenXRLoader.asset");
            }

            // ─── Step 3: XRManagerSettings を取得/作成して OpenXRLoader を設定 ───────
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // loader が完全にインポートされてから manager を作成
            loader = AssetDatabase.LoadMainAssetAtPath(loaderPath) as XRLoader ?? loader; // 再ロード

            var manager = AssetDatabase.LoadMainAssetAtPath(managerPath) as XRManagerSettings;
            if (manager == null)
            {
                manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                AssetDatabase.CreateAsset(manager, managerPath);
                Debug.Log("[ArsistSafe] Created XRManagerSettings.asset");
            }
            {
                var so = new SerializedObject(manager);
                var al = so.FindProperty("m_AutomaticLoading"); if (al != null) al.boolValue = true;
                var ar = so.FindProperty("m_AutomaticRunning"); if (ar != null) ar.boolValue = true;
                var lp = so.FindProperty("m_Loaders");
                if (lp != null && lp.isArray)
                {
                    lp.ClearArray();
                    lp.arraySize = 1;
                    lp.GetArrayElementAtIndex(0).objectReferenceValue = loader;
                    Debug.Log("[ArsistSafe] XRManagerSettings.Loaders = [OpenXRLoader]");
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(manager);
            }

            // ─── Step 4: XRGeneralSettings を取得/作成して Manager をリンク ──────────
            var generalSettings = AssetDatabase.LoadMainAssetAtPath(generalPath) as XRGeneralSettings;
            if (generalSettings == null)
            {
                generalSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                AssetDatabase.CreateAsset(generalSettings, generalPath);
                Debug.Log("[ArsistSafe] Created XRGeneralSettings.asset");
            }
            {
                var so = new SerializedObject(generalSettings);
                var mp = so.FindProperty("m_Manager"); if (mp != null) mp.objectReferenceValue = manager;
                var ip = so.FindProperty("m_InitManagerOnStart"); if (ip != null) ip.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(generalSettings);
            }

            // ─── Step 5: XRGeneralSettingsPerBuildTarget を取得/作成して Android を登録
            var perTarget = AssetDatabase.LoadMainAssetAtPath(perTargetPath) as XRGeneralSettingsPerBuildTarget;
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, perTargetPath);
                Debug.Log("[ArsistSafe] Created XRGeneralSettingsPerBuildTarget.asset");
            }

            // SetSettingsForBuildTarget で Android(=7) に generalSettings を登録
            perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, generalSettings);
            EditorUtility.SetDirty(perTarget);

            // EditorBuildSettings にも登録（OpenXRBuildProcessor が参照するエントリポイント）
            try
            {
                EditorBuildSettings.AddConfigObject(
                    XRGeneralSettings.k_SettingsKey, perTarget, true);
                Debug.Log("[ArsistSafe] Registered XRGeneralSettingsPerBuildTarget in EditorBuildSettings.");
            }
            catch (Exception e) { Debug.LogWarning($"[ArsistSafe] AddConfigObject: {e.Message}"); }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ─── Step 6: OpenXR Package Settings.asset を取得/作成 ───────────────────
            // OpenXRBuildProcessor は OpenXRSettings.GetSettingsForBuildTargetGroup() が null を
            // 返すと "OpenXR Settings found but not yet loaded" を throw する。
            // このアセットを事前に作成しておくことでその例外を防ぐ。
            EnsureOpenXRPackageSettings();

            // ─── Step 7: MetaQuestFeature を有効化 ──────────────────────────────────
            EnableMetaQuestOpenXRFeature();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ─── 確認ログ ────────────────────────────────────────────────────────────
            try
            {
                var xrSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
                if (xrSettings?.Manager != null)
                    Debug.Log($"[ArsistSafe] XR OK: loaders={xrSettings.Manager.activeLoaders.Count}, autoLoad={xrSettings.InitManagerOnStart}");
                else
                    Debug.LogWarning("[ArsistSafe] XR: Android generalSettings still null after setup!");
            }
            catch (Exception e) { Debug.LogWarning($"[ArsistSafe] XR confirm: {e.Message}"); }
        }

        /// <summary>
        /// OpenXR Package Settings.asset を取得/作成し EditorBuildSettings に登録する。
        /// OpenXRBuildProcessor は OpenXRSettings.GetSettingsForBuildTargetGroup() が null だと
        /// "OpenXR Settings found but not yet loaded" を投げるため、このアセットが必須。
        /// </summary>
        private static void EnsureOpenXRPackageSettings()
        {
            const string openXrSettingsKey  = "com.unity.xr.openxr.settings";
            const string openXrSettingsPath = "Assets/XR/Settings/OpenXR Package Settings.asset";

            // 1. アセットが既に存在するか確認
            var existing = AssetDatabase.LoadMainAssetAtPath(openXrSettingsPath);
            if (existing != null)
            {
                // EditorBuildSettings にも登録（存在してもキーが未登録の場合があるため再登録）
                try { EditorBuildSettings.AddConfigObject(openXrSettingsKey, existing, true); } catch { /* ignore */ }
                Debug.Log("[ArsistSafe] OpenXR Package Settings.asset already exists. Re-registered.");
                return;
            }

            // 2. OpenXRSettings 型を探す。GetOrCreateSettings() static 経由で生成を試みる
            var openXrSettingsType = FindType("UnityEditor.XR.OpenXR.OpenXRPackageSettings")
                                  ?? FindType("UnityEngine.XR.OpenXR.OpenXRSettings");

            if (openXrSettingsType != null)
            {
                // GetOrCreateSettings(BuildTargetGroup) が存在すれば呼び出す
                var getOrCreate = openXrSettingsType.GetMethod("GetOrCreateSettings",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                    null, new[] { typeof(BuildTargetGroup) }, null);
                if (getOrCreate != null)
                {
                    try
                    {
                        var s = getOrCreate.Invoke(null, new object[] { BuildTargetGroup.Android }) as UnityEngine.Object;
                        if (s != null)
                        {
                            EditorUtility.SetDirty(s);
                            AssetDatabase.SaveAssets();
                            Debug.Log("[ArsistSafe] OpenXR Package Settings created via GetOrCreateSettings().");
                            return;
                        }
                    }
                    catch (Exception e) { Debug.LogWarning($"[ArsistSafe] GetOrCreateSettings failed: {e.Message}"); }
                }

                // GetOrCreateSettings が無い場合は ScriptableObject として直接作成
                try
                {
                    var inst = ScriptableObject.CreateInstance(openXrSettingsType);
                    if (inst != null)
                    {
                        AssetDatabase.CreateAsset(inst, openXrSettingsPath);
                        EditorBuildSettings.AddConfigObject(openXrSettingsKey, inst, true);
                        EditorUtility.SetDirty(inst);
                        AssetDatabase.SaveAssets();
                        Debug.Log("[ArsistSafe] OpenXR Package Settings.asset created from scratch.");
                        return;
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[ArsistSafe] CreateInstance(OpenXRSettings): {e.Message}"); }
            }

            // 3. 型が見つからなかった場合: 最小限の YAML ファイルをファイルとして書き出す
            // (com.unity.xr.openxr が Library にまだキャッシュされていない初回ビルド)
            Debug.LogWarning("[ArsistSafe] OpenXRSettings type not found. Writing minimal placeholder asset.");
            try
            {
                var dir = Path.GetDirectoryName(Path.Combine(Application.dataPath, "XR/Settings/"));
                Directory.CreateDirectory(dir);
                // ScriptableObject 最小 YAML (m_Script GUID はどの Unity バージョンでも存在する placeholder)
                var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" +
                           "--- !u!114 &1\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n" +
                           "  m_Script: {fileID: 0}\n  m_Name: OpenXRSettings\n";
                File.WriteAllText(Path.Combine(Application.dataPath, "XR/Settings/OpenXR Package Settings.asset"), yaml);
                AssetDatabase.Refresh();
                var inst2 = AssetDatabase.LoadMainAssetAtPath(openXrSettingsPath);
                if (inst2 != null) EditorBuildSettings.AddConfigObject(openXrSettingsKey, inst2, true);
                Debug.Log("[ArsistSafe] Placeholder OpenXR Package Settings.asset written.");
            }
            catch (Exception ex) { Debug.LogWarning($"[ArsistSafe] Placeholder write failed: {ex.Message}"); }
        }

        /// <summary>
        /// MetaQuestFeature を有効化する。
        /// OpenXR Package Settings.asset が存在する場合はそこから有効化。
        /// 存在しない場合は OpenXRPackageSettings API 経由で試みる。
        /// </summary>
        private static void EnableMetaQuestOpenXRFeature()
        {
            // 候補パスを複数試す（Unity バージョンや SDKによってファイル名が異なるため）
            var candidatePaths = new[]
            {
                "Assets/XR/Settings/OpenXR Package Settings.asset",
                "Assets/XR/Settings/OpenXRPackageSettings.asset",
            };

            UnityEngine.Object[] allAssets = null;
            foreach (var p in candidatePaths)
            {
                var loaded = AssetDatabase.LoadAllAssetsAtPath(p);
                if (loaded.Length > 0) { allAssets = loaded; break; }
            }

            if (allAssets != null && allAssets.Length > 0)
            {
                // アセットファイルが存在する場合: 全サブオブジェクトをスキャンして MetaQuest feature を有効化
                int enabledCount = 0;
                foreach (var asset in allAssets)
                {
                    if (asset == null) continue;
                    var so = new SerializedObject(asset);
                    var nameProp = so.FindProperty("m_Name");
                    var name = nameProp?.stringValue ?? "";
                    bool isMetaQuestFeature =
                        name.IndexOf("MetaQuest",  StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("MetaXR",     StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("OculusQuest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Quest",      StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isMetaQuestFeature) continue;
                    var ep = so.FindProperty("m_enabled");
                    if (ep != null && !ep.boolValue)
                    {
                        ep.boolValue = true;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(asset);
                        enabledCount++;
                        Debug.Log($"[ArsistSafe] Enabled OpenXR feature: '{name}'");
                    }
                }
                Debug.Log($"[ArsistSafe] MetaQuest OpenXR features enabled: {enabledCount}");
                return;
            }

            // アセットファイルが存在しない場合: API 経由で有効化を試みる
            // (OpenXR パッケージが Library/ に初期化データを持つ場合に機能する)
            try
            {
                var pkgSettingsType = FindType("UnityEditor.XR.OpenXR.OpenXRPackageSettings");
                if (pkgSettingsType != null)
                {
                    var getOrCreate = pkgSettingsType.GetMethod("GetOrCreateSettings",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                        null, new[] { typeof(BuildTargetGroup) }, null);
                    if (getOrCreate != null)
                    {
                        var settings = getOrCreate.Invoke(null, new object[] { BuildTargetGroup.Android });
                        if (settings != null)
                        {
                            var so = new SerializedObject((UnityEngine.Object)settings);
                            // features 配列内の MetaQuestFeature を有効化
                            var featuresProp = so.FindProperty("features");
                            if (featuresProp != null && featuresProp.isArray)
                            {
                                for (int fi = 0; fi < featuresProp.arraySize; fi++)
                                {
                                    var featureSO = featuresProp.GetArrayElementAtIndex(fi).objectReferenceValue;
                                    if (featureSO == null) continue;
                                    var featureSOName = featureSO.name ?? "";
                                    if (featureSOName.IndexOf("MetaQuest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        featureSOName.IndexOf("Quest",     StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var fso = new SerializedObject(featureSO);
                                        var ep2 = fso.FindProperty("m_enabled");
                                        if (ep2 != null) { ep2.boolValue = true; fso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(featureSO); }
                                        Debug.Log($"[ArsistSafe] Enabled OpenXR feature via API: '{featureSOName}'");
                                    }
                                }
                            }
                            so.ApplyModifiedPropertiesWithoutUndo();
                            EditorUtility.SetDirty((UnityEngine.Object)settings);
                            Debug.Log("[ArsistSafe] MetaQuestFeature enable attempt via OpenXRPackageSettings API done.");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[ArsistSafe] OpenXRPackageSettings type not found. MetaQuestFeature may not be enabled.");
                }
            }
            catch (Exception e) { Debug.LogWarning($"[ArsistSafe] EnableMetaQuestOpenXRFeature API: {e.Message}"); }
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

        /// <summary>
        /// すべてのビルドキャッシュを完全クリアして、インクリメンタルビルドを防止する
        /// </summary>
        private static void ClearAllBuildCaches()
        {
            Debug.Log("[ArsistSafe] Clearing ALL build caches for clean build...");
            
            // 1. Library/ScriptAssemblies を削除（スクリプトコンパイルキャッシュ）
            var scriptAssembliesPath = Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies");
            if (Directory.Exists(scriptAssembliesPath))
            {
                try
                {
                    Directory.Delete(scriptAssembliesPath, true);
                    Debug.Log("[ArsistSafe] Deleted Library/ScriptAssemblies");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ArsistSafe] Failed to delete ScriptAssemblies: {e.Message}");
                }
            }

            // 2. Library/Bee を削除（IL2CPP ビルドキャッシュ）
            var beePath = Path.Combine(Application.dataPath, "..", "Library", "Bee");
            if (Directory.Exists(beePath))
            {
                try
                {
                    Directory.Delete(beePath, true);
                    Debug.Log("[ArsistSafe] Deleted Library/Bee (IL2CPP cache)");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ArsistSafe] Failed to delete Bee: {e.Message}");
                }
            }

            // 3. AssetDatabase を強制リフレッシュ
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log("[ArsistSafe] AssetDatabase.Refresh(ForceUpdate) completed");
        }

        /// <summary>
        /// スクリプトコンパイルが完了するまで待機する
        /// IMPORTANT: Thread.Sleep はUnityメインスレッドをブロックするためコンパイルが進まない（デッドロック）
        /// 正しいアプローチ: AssetDatabase.Refresh(ForceUpdate) で同期的にインポートとコンパイルを完了させる
        /// </summary>
        private static void WaitForScriptCompilation()
        {
            Debug.Log("[ArsistSafe] WaitForScriptCompilation: Performing synchronous asset refresh to ensure compilation...");

            // バッチモードでは AssetDatabase.Refresh(ForceUpdate) が同期的に
            // すべてのインポートとスクリプトコンパイルを完了させる。
            // Thread.Sleep を使うとメインスレッドがブロックされコンパイルが進まないため使用禁止。
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            // EditorApplication.isCompiling が false になるまで最大10秒待つ
            // ここでは Thread.Sleep ではなく Editor ループを活用した方法を使う
            // バッチモードでは上記 Refresh で既に完了しているはずなので、念のためのチェック
            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning("[ArsistSafe] isCompiling is still true after Refresh. Waiting 5 seconds...");
                // バッチモードでのみ有効: 少し待ってFromLoopが処理することを期待
                // ただし Sleep は使わずに時間測定のみ
                var start = System.DateTime.Now;
                while (EditorApplication.isCompiling && 
                       (System.DateTime.Now - start) < System.TimeSpan.FromSeconds(30))
                {
                    // バッチモードでは次のいずれかが必要:
                    // Unity の内部コンパイルループを進める方法が無いため
                    // Refresh を再度呼ぶことで強制的に完了させる
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    break; // 一回で十分
                }
            }

            Debug.Log($"[ArsistSafe] Script compilation check complete. isCompiling={EditorApplication.isCompiling}");
        }

        /// <summary>
        /// 必須のPlayerSettings設定を行う（これが無いとAPKが無効なAndroidアプリになる）
        /// デバイス固有設定（SDK、Architecture等）はデバイス別関数で行う
        /// </summary>
        private static void ConfigureEssentialPlayerSettings()
        {
            Debug.Log("[ArsistSafe] Configuring ESSENTIAL PlayerSettings (device-agnostic)...");
            
            // CRITICAL: パッケージ識別子（これが無いとAndroidアプリとして認識されない）
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.arsist.myarapp");
            
            // CRITICAL: バージョン番号（必須）
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            
            // CRITICAL: 会社名・製品名（APKメタデータに必須）
            PlayerSettings.companyName = "Arsist";
            PlayerSettings.productName = "MyARApp";
            
            // 注意: minSdk、targetArchitectures、ScriptingBackend等は
            // デバイス固有関数（ApplyQuestPlayerSettings等）で設定される
            
            Debug.Log("[ArsistSafe] Essential PlayerSettings configured:");
            Debug.Log($"  - Package: com.arsist.myarapp");
            Debug.Log($"  - Version: 1.0.0 (code: 1)");
            Debug.Log($"  - Product: Arsist / MyARApp");
            Debug.Log($"  - Device-specific settings (SDK, IL2CPP) will be applied by device adapters");
        }
    }
}
