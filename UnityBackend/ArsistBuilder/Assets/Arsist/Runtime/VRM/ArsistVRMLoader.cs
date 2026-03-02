// ==============================================
// Arsist Engine - VRM Loader
// Assets/Arsist/Runtime/VRM/ArsistVRMLoader.cs
// ==============================================
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace Arsist.Runtime.VRM
{
    /// <summary>
    /// VRMモデルをロードするためのユーティリティクラス
    /// UniVRM 0.131.0 以上をサポート
    /// </summary>
    public class ArsistVRMLoader : MonoBehaviour
    {
        /// <summary>
        /// VRMファイルをロードする（非同期）
        /// StreamingAssets または絶対パスからのロード
        /// </summary>
        public IEnumerator LoadVRMAsync(string vrmPath, Action<GameObject> onLoaded, Action<string> onError)
        {
            if (string.IsNullOrEmpty(vrmPath))
            {
                onError?.Invoke("VRM path is null or empty");
                yield break;
            }

            byte[] vrmData = null;

            // --- ステップ 1: VRMデータを取得 ---
            if (Application.platform == RuntimePlatform.Android)
            {
                // Android: StreamingAssets からロード
                if (!vrmPath.StartsWith("jar:"))
                {
                    vrmPath = Path.Combine(Application.streamingAssetsPath, vrmPath);
                }
                yield return LoadVRMFromAndroidStreamingAssets(vrmPath, onLoaded, onError);
                yield break;
            }
            else if (vrmPath.StartsWith("http"))
            {
                // Web: URLからロード
                yield return LoadVRMFromUrl(vrmPath, onLoaded, onError);
                yield break;
            }
            else
            {
                // ローカル: ファイルパスからロード
                if (!Path.IsPathRooted(vrmPath))
                {
                    vrmPath = Path.Combine(Application.streamingAssetsPath, vrmPath);
                }

                if (!File.Exists(vrmPath))
                {
                    onError?.Invoke($"VRM file not found: {vrmPath}");
                    yield break;
                }

                try
                {
                    vrmData = File.ReadAllBytes(vrmPath);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Failed to read VRM file: {ex.Message}");
                    yield break;
                }
            }

            if (vrmData == null || vrmData.Length == 0)
            {
                onError?.Invoke("VRM file is empty");
                yield break;
            }

            // --- ステップ 2: VRMをロード ---
            yield return LoadVRMFromBytes(vrmData, vrmPath, onLoaded, onError);
        }

        /// <summary>
        /// VRMバイト列からロード
        /// </summary>
        private IEnumerator LoadVRMFromBytes(byte[] vrmData, string vrmPath, Action<GameObject> onLoaded, Action<string> onError)
        {
            Debug.Log($"[ArsistVRMLoader] Loading VRM from bytes: {vrmPath} ({vrmData.Length} bytes)");

            var vrmUtilityType = ResolveType("VRM.VrmUtility");
            if (vrmUtilityType == null)
            {
                onError?.Invoke("UniVRM runtime library not found. Please import UniVRM-0.131.0 or later into Unity project.");
                yield break;
            }

            var loadBytesAsync = vrmUtilityType.GetMethod("LoadBytesAsync", BindingFlags.Public | BindingFlags.Static);
            if (loadBytesAsync == null)
            {
                onError?.Invoke("UniVRM VrmUtility.LoadBytesAsync was not found");
                yield break;
            }

            // Resolve IAwaitCaller type for better async handling
            var awaitCallerType = ResolveType("UniGLTF.AwaitCaller.ImmediateCaller") ?? ResolveType("UniGLTF.IAwaitCaller");
            object awaitCaller = null;
            
            if (awaitCallerType != null)
            {
                try
                {
                    awaitCaller = Activator.CreateInstance(awaitCallerType);
                }
                catch
                {
                    Debug.LogWarning("[ArsistVRMLoader] Failed to create IAwaitCaller, using null");
                }
            }

            object taskObj = null;
            try
            {
                var args = new object[]
                {
                    vrmPath,
                    vrmData,
                    awaitCaller,                      // IAwaitCaller (with fallback)
                    null,                             // MaterialGeneratorCallback
                    null,                             // MetaCallback
                    null,                             // ITextureDeserializer
                    false,                            // loadAnimation
                    null                              // IVrm0XSpringBoneRuntime
                };
                taskObj = loadBytesAsync.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ArsistVRMLoader] ❌ Failed to invoke VrmUtility.LoadBytesAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Debug.LogError($"[ArsistVRMLoader] Inner Exception: {ex.InnerException.Message}");
                }
                onError?.Invoke($"Failed to invoke UniVRM loader: {ex.Message}");
                yield break;
            }

            if (taskObj == null)
            {
                onError?.Invoke("UniVRM loader returned null task");
                yield break;
            }

            var taskType = taskObj.GetType();
            var isCompletedProp = taskType.GetProperty("IsCompleted");

            // Wait for task completion
            int waitFrames = 0;
            while (!(bool)(isCompletedProp?.GetValue(taskObj) ?? true))
            {
                waitFrames++;
                if (waitFrames > 1000) // Safety: 1000 frames timeout
                {
                    onError?.Invoke("VRM loading timeout");
                    yield break;
                }
                yield return null;
            }

            var isFaultedProp = taskType.GetProperty("IsFaulted");
            var isFaulted = (bool)(isFaultedProp?.GetValue(taskObj) ?? false);
            if (isFaulted)
            {
                var exceptionProp = taskType.GetProperty("Exception");
                Exception exceptionObj = null;
                try
                {
                    exceptionObj = exceptionProp?.GetValue(taskObj) as Exception;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ArsistVRMLoader] Exception property retrieval failed: {ex.Message}");
                }
                
                var err = exceptionObj?.InnerException?.Message ?? exceptionObj?.Message ?? "Unknown UniVRM task error";
                Debug.LogError($"[ArsistVRMLoader] ❌ UniVRM task faulted: {err}");
                if (exceptionObj?.InnerException != null)
                {
                    Debug.LogError($"[ArsistVRMLoader] Inner exception: {exceptionObj.InnerException.StackTrace}");
                }
                onError?.Invoke($"UniVRM task failed: {err}");
                yield break;
            }

            object runtimeInstance = null;
            try
            {
                var resultProp = taskType.GetProperty("Result");
                if (resultProp == null)
                {
                    Debug.LogError("[ArsistVRMLoader] ❌ Result property not found on task object");
                    onError?.Invoke("Task Result property not found");
                    yield break;
                }
                runtimeInstance = resultProp.GetValue(taskObj);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ArsistVRMLoader] ❌ Failed to get Result from task: {ex.Message}");
                Debug.LogError($"[ArsistVRMLoader] Exception: {ex.StackTrace}");
                onError?.Invoke($"Failed to get UniVRM load result: {ex.Message}");
                yield break;
            }

            if (runtimeInstance == null)
            {
                onError?.Invoke("UniVRM load result is null");
                yield break;
            }

            var runtimeType = runtimeInstance.GetType();
            var rootProp = runtimeType.GetProperty("Root") ?? runtimeType.GetProperty("gameObject");
            var vrmInstance = rootProp?.GetValue(runtimeInstance) as GameObject;

            if (vrmInstance == null)
            {
                onError?.Invoke("VRM root GameObject is null");
                yield break;
            }

            Debug.Log($"[ArsistVRMLoader] ✅ VRM loaded successfully: {vrmInstance.name}");

            var animator = vrmInstance.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                Debug.Log("[ArsistVRMLoader] ✅ Humanoid Animator found");
            }
            else
            {
                Debug.LogWarning("[ArsistVRMLoader] ⚠️ No Humanoid Animator found on loaded VRM");
            }

            onLoaded?.Invoke(vrmInstance);
        }

        /// <summary>
        /// URLからVRMをロード
        /// </summary>
        private IEnumerator LoadVRMFromUrl(string url, Action<GameObject> onLoaded, Action<string> onError)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Failed to download VRM from URL: {req.error}");
                    yield break;
                }

                byte[] vrmData = req.downloadHandler.data;
                yield return LoadVRMFromBytes(vrmData, url, onLoaded, onError);
            }
        }

        /// <summary>
        /// StreamingAssetsからVRMをロードする
        /// </summary>
        public IEnumerator LoadVRMFromStreamingAssets(string relativePath, Action<GameObject> onLoaded, Action<string> onError)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                onError?.Invoke("Relative path is null or empty");
                yield break;
            }

            var normalizedRelativePath = relativePath
                .Replace('\\', '/')
                .TrimStart('/');
            string fullPath = Path.Combine(Application.streamingAssetsPath, normalizedRelativePath);
            Debug.Log($"[ArsistVRMLoader] StreamingAssets full path: {fullPath}");
            
            // Android は UnityWebRequest で jar:// から読む必要がある
            if (Application.platform == RuntimePlatform.Android)
            {
                var androidUri = BuildAndroidStreamingAssetsUri(normalizedRelativePath);
                Debug.Log($"[ArsistVRMLoader] Android StreamingAssets URI: {androidUri}");
                yield return LoadVRMFromAndroidStreamingAssets(androidUri, onLoaded, onError);
            }
            else
            {
                // その他のプラットフォームは File.ReadAllBytes で読む
                if (!File.Exists(fullPath))
                {
                    onError?.Invoke($"VRM file not found: {fullPath}");
                    yield break;
                }

                byte[] vrmData = null;
                try
                {
                    vrmData = File.ReadAllBytes(fullPath);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Failed to read VRM file: {ex.Message}");
                    yield break;
                }

                if (vrmData != null && vrmData.Length > 0)
                {
                    yield return LoadVRMFromBytes(vrmData, fullPath, onLoaded, onError);
                }
                else
                {
                    onError?.Invoke("VRM file is empty");
                }
            }
        }

        private static string BuildAndroidStreamingAssetsUri(string relativePath)
        {
            var basePath = Application.streamingAssetsPath.Replace('\\', '/');
            var normalizedRelativePath = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');

            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return basePath;
            }

            if (basePath.EndsWith("/"))
            {
                return $"{basePath}{normalizedRelativePath}";
            }

            return $"{basePath}/{normalizedRelativePath}";
        }

        /// <summary>
        /// Android StreamingAssetsからVRMをロードする（UnityWebRequest使用）
        /// </summary>
        private IEnumerator LoadVRMFromAndroidStreamingAssets(string streamingAssetsUri, Action<GameObject> onLoaded, Action<string> onError)
        {
            Debug.Log($"[ArsistVRMLoader] Loading from Android URI: {streamingAssetsUri}");
            using (UnityWebRequest request = UnityWebRequest.Get(streamingAssetsUri))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Failed to load VRM from Android StreamingAssets: {request.error} (uri={streamingAssetsUri})");
                    yield break;
                }

                byte[] vrmData = request.downloadHandler.data;
                if (vrmData == null || vrmData.Length == 0)
                {
                    onError?.Invoke("Downloaded VRM data is empty");
                    yield break;
                }

                yield return LoadVRMFromBytes(vrmData, streamingAssetsUri, onLoaded, onError);
            }
        }

        /// <summary>
        /// VRMインスタンスにスクリプト制御用のコンポーネントを追加
        /// </summary>
        public static void SetupVRMForScripting(GameObject vrmRoot, string assetId)
        {
            if (vrmRoot == null || string.IsNullOrEmpty(assetId))
            {
                Debug.LogWarning("[ArsistVRMLoader] Invalid VRM root or asset ID");
                return;
            }

            // VRMWrapperに登録
            var scriptEngine = Scripting.ScriptEngineManager.Instance;
            if (scriptEngine != null)
            {
                scriptEngine.VRMWrapper.RegisterVRM(assetId, vrmRoot);
                Debug.Log($"[ArsistVRMLoader] Registered VRM '{assetId}' for scripting");
            }

            // SceneWrapperにも登録（scene APIでも操作可能にする）
            if (scriptEngine != null)
            {
                scriptEngine.SceneWrapper.RegisterObject(assetId, vrmRoot);
            }
        }

        /// <summary>
        /// フルネームからタイプを解決する（reflection helper）
        /// </summary>
        private static Type ResolveType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            try
            {
                // 現在ロードされているすべてのアセンブリから検索
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var type = assembly.GetType(fullName, false);
                        if (type != null)
                            return type;
                    }
                    catch
                    {
                        // アセンブリによってはエラーが発生する可能性があるため無視
                    }
                }
            }
            catch
            {
                // 全体エラーも無視
            }

            return null;
        }    }
}