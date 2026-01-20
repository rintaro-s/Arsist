// ==============================================
// Arsist Engine - XREAL One Build Patcher
// Adapters/XREAL_One/XrealBuildPatcher.cs
// ==============================================

using UnityEngine;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using System.IO;
using System.Xml;
using System.Collections.Generic;

namespace Arsist.Adapters.XrealOne
{
    /// <summary>
    /// XREAL One 用のビルドパッチャー
    /// Arsistビルドパイプラインから呼び出され、デバイス固有の設定を適用
    /// </summary>
    public static class XrealBuildPatcher
    {
        private const string ADAPTER_ID = "xreal-one";
        private const string SDK_VERSION = "3.1.0";

        /// <summary>
        /// 全てのパッチを一括適用
        /// </summary>
        [MenuItem("Arsist/Adapters/XREAL One/Apply All Patches")]
        public static void ApplyAllPatches()
        {
            Debug.Log($"[Arsist-{ADAPTER_ID}] Applying all patches...");
            
            ApplyPlayerSettings();
            ConfigureXRLoader();
            ConfigureXRInteraction();
            ApplyQualitySettings();
            
            Debug.Log($"[Arsist-{ADAPTER_ID}] All patches applied successfully");
        }

        /// <summary>
        /// XREAL One用のPlayerSettings設定を適用
        /// </summary>
        [MenuItem("Arsist/Adapters/XREAL One/Apply Player Settings")]
        public static void ApplyPlayerSettings()
        {
            Debug.Log($"[Arsist-{ADAPTER_ID}] Applying Player Settings...");

            // === Android基本設定 ===
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29; // Android 10
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34; // Android 14
            
            // ARM64のみ（XREAL Oneは64bit専用）
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            
            // IL2CPP必須（パフォーマンス最適化）
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            
            // API互換性
            // Unity バージョンによって ApiCompatibilityLevel の列挙子が異なるため、文字列パースで安全に選択する
            ApiCompatibilityLevel apiLevel;
            if (!System.Enum.TryParse("NET_Standard_2_1", out apiLevel) &&
                !System.Enum.TryParse("NET_Standard_2_0", out apiLevel) &&
                !System.Enum.TryParse("NET_Unity_4_8", out apiLevel) &&
                !System.Enum.TryParse("NET_4_6", out apiLevel))
            {
                var values = System.Enum.GetValues(typeof(ApiCompatibilityLevel));
                apiLevel = values.Length > 0 ? (ApiCompatibilityLevel)values.GetValue(0) : default;
            }
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, apiLevel);

            // === グラフィックス設定 ===
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.MTRendering = true; // マルチスレッドレンダリング
            PlayerSettings.graphicsJobs = true;
            PlayerSettings.gpuSkinning = true;
            
            // OpenGLES3を優先、Vulkanをフォールバック
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan
            });

            // === 画面設定（XREAL One固定）===
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            
            // フルスクリーン設定
            PlayerSettings.useAnimatedAutorotation = false;
            PlayerSettings.resizableWindow = false;

            // === ランタイム設定 ===
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            
            // Sustained Performance Mode（発熱抑制）
            PlayerSettings.Android.optimizedFramePacing = true;

            Debug.Log($"[Arsist-{ADAPTER_ID}] Player Settings applied");
        }

        /// <summary>
        /// OpenXR Loader設定
        /// </summary>
        [MenuItem("Arsist/Adapters/XREAL One/Configure XR Loader")]
        public static void ConfigureXRLoader()
        {
            Debug.Log($"[Arsist-{ADAPTER_ID}] Configuring XR Loader...");

#if UNITY_XR_MANAGEMENT
            // XR General Settingsを取得または作成
            var generalSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (generalSettings == null)
            {
                generalSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                XRGeneralSettingsPerBuildTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, generalSettings);
            }

            // XR Manager Settingsを設定
            var managerSettings = generalSettings.Manager;
            if (managerSettings == null)
            {
                managerSettings = ScriptableObject.CreateInstance<XRManagerSettings>();
                generalSettings.Manager = managerSettings;
            }

            // OpenXR Loaderを追加
            var loaders = managerSettings.activeLoaders;
            bool hasOpenXR = false;
            foreach (var loader in loaders)
            {
                if (loader is OpenXRLoader)
                {
                    hasOpenXR = true;
                    break;
                }
            }

            if (!hasOpenXR)
            {
                Debug.Log($"[Arsist-{ADAPTER_ID}] Adding OpenXR Loader");
                // OpenXR Loaderを追加する処理
                // 注: 実際にはXR Plugin Management UIまたはスクリプトで設定
            }

            // 自動初期化を有効化
            generalSettings.InitManagerOnStart = true;
#else
            Debug.LogWarning($"[Arsist-{ADAPTER_ID}] XR Management not found. Please install XR Plugin Management.");
#endif

            Debug.Log($"[Arsist-{ADAPTER_ID}] XR Loader configured");
        }

        /// <summary>
        /// XR Interaction Toolkit設定
        /// </summary>
        public static void ConfigureXRInteraction()
        {
            Debug.Log($"[Arsist-{ADAPTER_ID}] Configuring XR Interaction...");

            // InputActionアセットをコピー
            var sourceInputActions = "Packages/com.unity.xr.interaction.toolkit/Runtime/Interaction/Actions/XRI Default Input Actions.inputactions";
            var destInputActions = "Assets/Arsist/Input/XrealInputActions.inputactions";

            if (File.Exists(sourceInputActions) && !File.Exists(destInputActions))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destInputActions));
                File.Copy(sourceInputActions, destInputActions);
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Arsist-{ADAPTER_ID}] XR Interaction configured");
        }

        /// <summary>
        /// Quality Settings最適化
        /// </summary>
        [MenuItem("Arsist/Adapters/XREAL One/Apply Quality Settings")]
        public static void ApplyQualitySettings()
        {
            Debug.Log($"[Arsist-{ADAPTER_ID}] Applying Quality Settings...");

            // 最適なQualityレベルを設定
            QualitySettings.SetQualityLevel(2); // Medium相当
            
            // アンチエイリアシング（MSAAx4）
            QualitySettings.antiAliasing = 4;
            
            // テクスチャ品質
            QualitySettings.globalTextureMipmapLimit = 0; // フル解像度
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            
            // シャドウ設定
            QualitySettings.shadows = ShadowQuality.HardOnly;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 20f;
            QualitySettings.shadowCascades = 2;
            
            // LOD設定
            QualitySettings.lodBias = 1.0f;
            QualitySettings.maximumLODLevel = 0;
            
            // Skin Weights
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            
            // VSync（AR用に無効化、フレームレート制御はSDKに任せる）
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            Debug.Log($"[Arsist-{ADAPTER_ID}] Quality Settings applied");
        }

        /// <summary>
        /// AndroidManifest.xmlにXREAL固有の設定を追加
        /// </summary>
        public static void PatchAndroidManifest()
        {
            Debug.Log($"[Arsist-{ADAPTER_ID}] Patching AndroidManifest.xml...");

            var manifestPath = Path.Combine(Application.dataPath, "Plugins", "Android", "AndroidManifest.xml");
            
            if (!File.Exists(manifestPath))
            {
                // テンプレートからコピー
                CreateBaseManifest(manifestPath);
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);

            var manifest = doc.DocumentElement;
            var nsManager = new XmlNamespaceManager(doc.NameTable);
            nsManager.AddNamespace("android", "http://schemas.android.com/apk/res/android");

            // === パーミッション追加 ===
            AddPermissionIfMissing(doc, manifest, "android.permission.CAMERA");
            AddPermissionIfMissing(doc, manifest, "android.permission.INTERNET");
            AddPermissionIfMissing(doc, manifest, "android.permission.ACCESS_NETWORK_STATE");

            // === uses-feature追加 ===
            AddFeatureIfMissing(doc, manifest, "android.hardware.camera", true);
            AddFeatureIfMissing(doc, manifest, "android.hardware.camera.autofocus", false);

            // === Application/Activity設定 ===
            var application = manifest.SelectSingleNode("application") as XmlElement;
            if (application != null)
            {
                // meta-data追加
                AddMetaDataIfMissing(doc, application, "com.xreal.sdk.version", SDK_VERSION, nsManager);
                
                var activity = application.SelectSingleNode("activity[@android:name='com.unity3d.player.UnityPlayerActivity']", nsManager) as XmlElement;
                if (activity != null)
                {
                    // AR用カテゴリ追加
                    var intentFilter = activity.SelectSingleNode("intent-filter") as XmlElement;
                    if (intentFilter != null)
                    {
                        AddCategoryIfMissing(doc, intentFilter, "com.xreal.intent.category.AR", nsManager);
                    }

                    // 画面設定
                    activity.SetAttribute("screenOrientation", "http://schemas.android.com/apk/res/android", "landscape");
                    activity.SetAttribute("configChanges", "http://schemas.android.com/apk/res/android", 
                        "keyboard|keyboardHidden|orientation|screenSize|screenLayout|uiMode");
                }
            }

            doc.Save(manifestPath);
            AssetDatabase.Refresh();

            Debug.Log($"[Arsist-{ADAPTER_ID}] AndroidManifest.xml patched");
        }

        private static void CreateBaseManifest(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            
            var content = @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android""
    package=""com.arsist.app""
    android:versionCode=""1""
    android:versionName=""1.0"">
    
    <uses-sdk android:minSdkVersion=""29"" android:targetSdkVersion=""34"" />
    
    <application
        android:allowBackup=""false""
        android:icon=""@mipmap/app_icon""
        android:label=""@string/app_name""
        android:theme=""@style/UnityThemeSelector"">
        
        <activity
            android:name=""com.unity3d.player.UnityPlayerActivity""
            android:exported=""true""
            android:screenOrientation=""landscape""
            android:configChanges=""keyboard|keyboardHidden|orientation|screenSize|screenLayout|uiMode"">
            <intent-filter>
                <action android:name=""android.intent.action.MAIN"" />
                <category android:name=""android.intent.category.LAUNCHER"" />
            </intent-filter>
        </activity>
    </application>
</manifest>";
            
            File.WriteAllText(path, content);
        }

        private static void AddPermissionIfMissing(XmlDocument doc, XmlElement manifest, string permission)
        {
            var existing = manifest.SelectSingleNode($"uses-permission[@android:name='{permission}']", 
                CreateNamespaceManager(doc));
            
            if (existing == null)
            {
                var element = doc.CreateElement("uses-permission");
                element.SetAttribute("name", "http://schemas.android.com/apk/res/android", permission);
                manifest.AppendChild(element);
            }
        }

        private static void AddFeatureIfMissing(XmlDocument doc, XmlElement manifest, string feature, bool required)
        {
            var existing = manifest.SelectSingleNode($"uses-feature[@android:name='{feature}']", 
                CreateNamespaceManager(doc));
            
            if (existing == null)
            {
                var element = doc.CreateElement("uses-feature");
                element.SetAttribute("name", "http://schemas.android.com/apk/res/android", feature);
                element.SetAttribute("required", "http://schemas.android.com/apk/res/android", required.ToString().ToLower());
                manifest.AppendChild(element);
            }
        }

        private static void AddMetaDataIfMissing(XmlDocument doc, XmlElement parent, string name, string value, XmlNamespaceManager nsManager)
        {
            var existing = parent.SelectSingleNode($"meta-data[@android:name='{name}']", nsManager);
            
            if (existing == null)
            {
                var element = doc.CreateElement("meta-data");
                element.SetAttribute("name", "http://schemas.android.com/apk/res/android", name);
                element.SetAttribute("value", "http://schemas.android.com/apk/res/android", value);
                parent.AppendChild(element);
            }
        }

        private static void AddCategoryIfMissing(XmlDocument doc, XmlElement intentFilter, string category, XmlNamespaceManager nsManager)
        {
            var existing = intentFilter.SelectSingleNode($"category[@android:name='{category}']", nsManager);
            
            if (existing == null)
            {
                var element = doc.CreateElement("category");
                element.SetAttribute("name", "http://schemas.android.com/apk/res/android", category);
                intentFilter.AppendChild(element);
            }
        }

        private static XmlNamespaceManager CreateNamespaceManager(XmlDocument doc)
        {
            var nsManager = new XmlNamespaceManager(doc.NameTable);
            nsManager.AddNamespace("android", "http://schemas.android.com/apk/res/android");
            return nsManager;
        }

        /// <summary>
        /// XREAL One用のXR Originプレハブを生成
        /// </summary>
        [MenuItem("Arsist/Adapters/XREAL One/Create XR Origin Prefab")]
        public static void CreateXROriginPrefab()
        {
            Debug.Log($"[Arsist-{ADAPTER_ID}] Creating XR Origin prefab...");

            // XR Origin
            var xrOrigin = new GameObject("XR Origin (XREAL One)");
            
            // Camera Offset
            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform);
            
            // Main Camera
            var mainCamera = new GameObject("Main Camera");
            mainCamera.tag = "MainCamera";
            mainCamera.transform.SetParent(cameraOffset.transform);
            var camera = mainCamera.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.fieldOfView = 50f; // XREAL One FOV
            mainCamera.AddComponent<AudioListener>();
            
            // Gaze Interactor
            var gazeInteractor = new GameObject("Gaze Interactor");
            gazeInteractor.transform.SetParent(mainCamera.transform);
            gazeInteractor.transform.localPosition = Vector3.zero;
            
            // Ray Interactor（コントローラー用）
            var rayInteractor = new GameObject("Ray Interactor");
            rayInteractor.transform.SetParent(xrOrigin.transform);
            var lineRenderer = rayInteractor.AddComponent<LineRenderer>();
            lineRenderer.startWidth = 0.005f;
            lineRenderer.endWidth = 0.005f;

            // プレハブとして保存
            var prefabPath = "Assets/Arsist/Prefabs/XROrigin.prefab";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", prefabPath)));
            PrefabUtility.SaveAsPrefabAsset(xrOrigin, prefabPath);
            GameObject.DestroyImmediate(xrOrigin);
            
            AssetDatabase.Refresh();
            Debug.Log($"[Arsist-{ADAPTER_ID}] XR Origin prefab created at {prefabPath}");
        }
    }
}
