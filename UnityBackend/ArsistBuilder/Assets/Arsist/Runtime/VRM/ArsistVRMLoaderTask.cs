// ==============================================
// Arsist Engine - VRM Loader Task
// Assets/Arsist/Runtime/VRM/ArsistVRMLoaderTask.cs
// ==============================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Arsist.Runtime.Scripting;

namespace Arsist.Runtime.VRM
{
    /// <summary>
    /// VRM ファイルをストリーミングアセットからロード
    /// ビルドパイプラインで GameObject に添付されるコンポーネント
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class ArsistVRMLoaderTask : MonoBehaviour
    {
        [SerializeField] public string vrmPath;
        [SerializeField] public string assetId;

        private void Start()
        {
            if (string.IsNullOrEmpty(vrmPath))
            {
                Debug.LogWarning("[ArsistVRMLoaderTask] VRM path is not set!");
                Destroy(this);
                return;
            }

            StartCoroutine(LoadVRMCoroutine());
        }

        private IEnumerator LoadVRMCoroutine()
        {
            Debug.Log($"[ArsistVRMLoaderTask] Starting VRM load: {vrmPath} (assetId: {assetId})");

            var loaderInstance = gameObject.AddComponent<ArsistVRMLoader>();
            var actualAssetId = assetId ?? gameObject.name;

            // ビルド時に StreamingAssets/VRM にコピーされるため、ファイル名を抽出
            var fileName = System.IO.Path.GetFileName(vrmPath);
            var streamingAssetsPath = $"VRM/{fileName}";
            Debug.Log($"[ArsistVRMLoaderTask] Resolved StreamingAssets path: {streamingAssetsPath}");

            // VRM ファイルをロード
            GameObject vrmInstance = null;
            var error = "";

            yield return loaderInstance.LoadVRMFromStreamingAssets(
                streamingAssetsPath,
                onLoaded: (loadedVRM) =>
                {
                    vrmInstance = loadedVRM;
                    Debug.Log($"[ArsistVRMLoaderTask] ✅ VRM loaded: {loadedVRM.name}");
                },
                onError: (errorMsg) =>
                {
                    error = errorMsg;
                    Debug.LogError($"[ArsistVRMLoaderTask] ❌ Failed to load VRM: {errorMsg}");
                }
            );

            if (vrmInstance != null)
            {
                // VRM をこのゲームオブジェクトの子として配置
                vrmInstance.transform.SetParent(gameObject.transform, false);
                vrmInstance.transform.localPosition = Vector3.zero;
                vrmInstance.transform.localRotation = Quaternion.identity;

                // 表示保証（見えない経路を潰す）
                EnsureVRMVisible(vrmInstance);

                // スクリプトエンジンに登録（初期化タイミングずれを吸収）
                yield return RegisterVRMWhenScriptEngineReady(actualAssetId, vrmInstance);
            }
            else
            {
                Debug.LogError($"[ArsistVRMLoaderTask] ❌ VRM load failed: {error}");
            }

            // このコンポーネントは不要になったので削除
            Destroy(loaderInstance);
            Destroy(this);
        }

        private IEnumerator RegisterVRMWhenScriptEngineReady(string actualAssetId, GameObject vrmInstance)
        {
            const float timeoutSeconds = 10f;
            var elapsed = 0f;

            while (ScriptEngineManager.Instance == null && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            var scriptEngine = ScriptEngineManager.Instance;
            if (scriptEngine == null)
            {
                Debug.LogWarning($"[ArsistVRMLoaderTask] ScriptEngineManager not available after {timeoutSeconds:F0}s. VRM '{actualAssetId}' was not registered.");
                yield break;
            }

            scriptEngine.VRMWrapper.RegisterVRM(actualAssetId, vrmInstance);
            scriptEngine.SceneWrapper.RegisterObject(actualAssetId, vrmInstance);
            Debug.Log($"[ArsistVRMLoaderTask] ✅ VRM '{actualAssetId}' registered for scripting");

            // Inspector 表示用のメタデータコンポーネントを追加
            var metadataDisplay = gameObject.AddComponent<VRMMetadataDisplay>();
            var animator = vrmInstance.GetComponent<Animator>();
            metadataDisplay.UpdateMetadata(actualAssetId, animator);
        }

        private void EnsureVRMVisible(GameObject vrmRoot)
        {
            if (vrmRoot == null) return;

            // 1) 非アクティブ経路を潰す
            SetActiveRecursively(vrmRoot, true);

            // 2) レンダラー有効化 + レイヤーをDefaultへ統一
            var renderers = vrmRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.enabled = true;
                renderer.gameObject.layer = 0;
            }

            if (renderers.Length == 0)
            {
                Debug.LogWarning("[ArsistVRMLoaderTask] No renderer found on loaded VRM");
                return;
            }

            // 3) バウンディングを元にサイズを正規化（極小/極大を防ぐ）
            var bounds = ComputeBounds(renderers);
            var height = Mathf.Max(bounds.size.y, 0.0001f);
            if (height < 0.3f || height > 4.0f)
            {
                var targetHeight = 1.6f;
                var scaleFactor = targetHeight / height;
                vrmRoot.transform.localScale = vrmRoot.transform.localScale * scaleFactor;
                Debug.Log($"[ArsistVRMLoaderTask] Normalized VRM scale: x{scaleFactor:F2}");
                bounds = ComputeBounds(renderers);
            }

            // 4) カメラ前へ配置（遠すぎ/近すぎ/背面を防ぐ）
            var cameraTransform = ResolveMainCameraTransform();
            if (cameraTransform != null)
            {
                var center = bounds.center;
                var toModel = center - cameraTransform.position;
                var distance = toModel.magnitude;
                var forwardDot = Vector3.Dot(cameraTransform.forward, toModel.normalized);

                var shouldReposition = distance < 0.2f || distance > 8.0f || forwardDot < 0.2f;
                if (shouldReposition)
                {
                    var targetPos = cameraTransform.position + cameraTransform.forward * 1.5f;
                    targetPos.y = Mathf.Max(cameraTransform.position.y - 0.9f, 0.0f);

                    var offset = targetPos - center;
                    vrmRoot.transform.position += offset;

                    var lookDir = cameraTransform.position - vrmRoot.transform.position;
                    lookDir.y = 0f;
                    if (lookDir.sqrMagnitude > 0.0001f)
                    {
                        vrmRoot.transform.rotation = Quaternion.LookRotation(-lookDir.normalized, Vector3.up);
                    }

                    Debug.Log("[ArsistVRMLoaderTask] Repositioned VRM in front of camera for visibility");
                }
            }

            // 5) SkinnedMeshがオフスクリーンで消えないように補強
            var skinnedMeshes = vrmRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var skinned in skinnedMeshes)
            {
                if (skinned == null) continue;
                skinned.updateWhenOffscreen = true;
            }

            Debug.Log($"[ArsistVRMLoaderTask] Visibility guard applied: renderers={renderers.Length}, skinned={skinnedMeshes.Length}");
        }

        private static Bounds ComputeBounds(Renderer[] renderers)
        {
            var initialized = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private static Transform ResolveMainCameraTransform()
        {
            if (Camera.main != null) return Camera.main.transform;
            var anyCamera = FindAnyObjectByType<Camera>();
            return anyCamera != null ? anyCamera.transform : null;
        }

        private static void SetActiveRecursively(GameObject root, bool active)
        {
            if (root == null) return;

            var stack = new Stack<Transform>();
            stack.Push(root.transform);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                current.gameObject.SetActive(active);

                for (int i = 0; i < current.childCount; i++)
                {
                    stack.Push(current.GetChild(i));
                }
            }
        }
    }
}
