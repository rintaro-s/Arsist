using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
#if GLTFAST
using GLTFast;
#endif

namespace Arsist.Runtime
{
    /// <summary>
    /// ランタイムでGLB/GLTFモデルを読み込むコンポーネント（glTFast 6.x対応）
    /// StreamingAssetsからUnityWebRequestでバイト取得→glTFast.Load(byte[])→InstantiateSceneAsync
    /// </summary>
    public class ArsistModelRuntimeLoader : MonoBehaviour
    {
        [Tooltip("モデルファイルのパス（StreamingAssets相対またはURL）")]
        public string modelPath;

        [Tooltip("読み込み完了後に自動でこのコンポーネントを削除")]
        public bool destroyAfterLoad = true;

        private void Start()
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                Debug.LogWarning("[ArsistModelLoader] modelPath is empty");
                return;
            }
            StartCoroutine(LoadModelCoroutine());
        }

        private IEnumerator LoadModelCoroutine()
        {
#if GLTFAST
            Debug.Log($"[ArsistModelLoader] Start loading: {modelPath}");

            // --- Step 1: URIを組み立て ---
            string uri = modelPath;
            if (!modelPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !System.IO.Path.IsPathRooted(modelPath))
            {
                var basePath = Application.streamingAssetsPath;
                if (!basePath.EndsWith("/")) basePath += "/";
                uri = basePath + modelPath;
            }
            Debug.Log($"[ArsistModelLoader] Resolved URI: {uri}");

            // --- Step 2: UnityWebRequestでバイトダウンロード（全プラットフォーム共通） ---
            byte[] data = null;
            using (var req = UnityWebRequest.Get(uri))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[ArsistModelLoader] Download failed: {req.error} ({uri})");
                    CreateFallbackCube("DL Error");
                    yield break;
                }
                data = req.downloadHandler.data;
                Debug.Log($"[ArsistModelLoader] Downloaded {data.Length} bytes");
            }

            if (data == null || data.Length == 0)
            {
                Debug.LogError($"[ArsistModelLoader] Empty data for: {uri}");
                CreateFallbackCube("Empty");
                yield break;
            }

            // --- Step 3: glTFastでロード ---
            var gltf = new GltfImport();
            Debug.Log($"[ArsistModelLoader] Creating GltfImport, about to Load {data.Length} bytes");
            var loadTask = gltf.Load(data, new Uri(uri));
            while (!loadTask.IsCompleted) yield return null;

            if (loadTask.IsFaulted)
            {
                var ex = loadTask.Exception;
                Debug.LogError($"[ArsistModelLoader] Load FAULTED: {ex?.GetBaseException()?.Message}");
                Debug.LogError($"[ArsistModelLoader] Full exception: {ex}");
                CreateFallbackCube("Load Error");
                yield break;
            }

            bool loaded = loadTask.Result;
            Debug.Log($"[ArsistModelLoader] gltf.Load result={loaded}, SceneCount={gltf.SceneCount}");

            if (!loaded)
            {
                Debug.LogError($"[ArsistModelLoader] Failed to parse GLB: {modelPath}");
                CreateFallbackCube("Parse Error");
                yield break;
            }

            // LoadingError プロパティを確認
            if (gltf.LoadingError)
            {
                Debug.LogError($"[ArsistModelLoader] gltf.LoadingError is TRUE after load!");
                CreateFallbackCube("Loading Error");
                yield break;
            }

            // --- Step 4: シーンのインスタンス化 ---
            Debug.Log($"[ArsistModelLoader] Starting instantiation. SceneCount={gltf.SceneCount}");
            bool instantiated = false;
            Exception instantiateError = null;

            if (gltf.SceneCount > 0)
            {
                Debug.Log($"[ArsistModelLoader] Calling InstantiateSceneAsync((Transform)null, 0)...");
                var instantiateTask = gltf.InstantiateSceneAsync((Transform)null, 0);
                while (!instantiateTask.IsCompleted) yield return null;

                if (instantiateTask.IsFaulted)
                {
                    instantiateError = instantiateTask.Exception;
                    Debug.LogError($"[ArsistModelLoader] InstantiateSceneAsync FAULTED: {instantiateError?.GetBaseException()?.Message}");
                    Debug.LogError($"[ArsistModelLoader] Full exception: {instantiateError}");
                }
                else
                {
                    instantiated = instantiateTask.Result;
                    Debug.Log($"[ArsistModelLoader] InstantiateSceneAsync completed, result={instantiated}");
                }
            }
            else
            {
                Debug.LogWarning($"[ArsistModelLoader] SceneCount is 0, cannot instantiate");
            }

            if (!instantiated && instantiateError != null)
            {
                Debug.LogWarning($"[ArsistModelLoader] InstantiateSceneAsync(0) failed: {instantiateError.Message}");
                // フォールバック: Main Scene を試す
                var mainTask = gltf.InstantiateMainSceneAsync((Transform)null);
                while (!mainTask.IsCompleted) yield return null;

                if (mainTask.IsFaulted)
                {
                    Debug.LogWarning($"[ArsistModelLoader] MainScene fallback also failed: {mainTask.Exception?.Message}");
                }
                else
                {
                    instantiated = mainTask.Result;
                }
            }

            if (instantiated)
            {
                Debug.Log($"[ArsistModelLoader] Successfully instantiated: {modelPath}");
                // スケール調整: XRシーンでは巨大すぎるモデルを補正
                AdjustScaleForXR();
            }
            else
            {
                Debug.LogError($"[ArsistModelLoader] Could not instantiate any scene from: {modelPath}");
                CreateFallbackCube("Instance Error");
            }

            if (destroyAfterLoad && this != null)
            {
                Destroy(this);
            }
#else
            Debug.LogWarning("[ArsistModelLoader] GLTFAST not defined. glTFast package required.");
            CreateFallbackCube("No glTFast");
            yield break;
#endif
        }

        /// <summary>
        /// XR空間で適切なサイズに自動調整
        /// </summary>
        private void AdjustScaleForXR()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var combinedBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                combinedBounds.Encapsulate(renderers[i].bounds);
            }

            float maxExtent = Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
            if (maxExtent > 10f)
            {
                // 10mを超える場合は1m以内に縮小
                float scale = 1f / maxExtent;
                transform.localScale = Vector3.one * scale;
                Debug.Log($"[ArsistModelLoader] Auto-scaled model from {maxExtent:F1}m to {scale:F3}x");
            }
            else if (maxExtent < 0.01f)
            {
                // 1cm未満の場合は50cmに拡大
                float scale = 0.5f / maxExtent;
                transform.localScale = Vector3.one * scale;
                Debug.Log($"[ArsistModelLoader] Auto-scaled model from {maxExtent:F4}m to {scale:F1}x");
            }
        }

        /// <summary>
        /// エラー時にフォールバックキューブを生成（デバッグ用）
        /// </summary>
        private void CreateFallbackCube(string label)
        {
            Debug.LogError($"[ArsistModelLoader] ===== FALLBACK CUBE CREATED =====");
            Debug.LogError($"[ArsistModelLoader] Reason: {label}");
            Debug.LogError($"[ArsistModelLoader] ModelPath: {modelPath}");
            Debug.LogError($"[ArsistModelLoader] This means GLB loading or instantiation FAILED");
            Debug.LogError($"[ArsistModelLoader] =====================================");
            
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"[FALLBACK] {label}";
            cube.transform.SetParent(transform, false);
            cube.transform.localScale = Vector3.one * 0.3f;

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.color = Color.red;
                    renderer.material = mat;
                }
                else
                {
                    Debug.LogError($"[ArsistModelLoader] Could not even find Unlit/Color or Standard shader!");
                }
            }
        }
    }
}
