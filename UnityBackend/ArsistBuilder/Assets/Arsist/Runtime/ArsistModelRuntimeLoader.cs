using System.Threading.Tasks;
using UnityEngine;
#if GLTFAST
using GLTFast;
#endif

namespace Arsist.Runtime
{
    /// <summary>
    /// ランタイムでGLB/GLTFモデルを読み込むコンポーネント
    /// glTFastパッケージが必要
    /// </summary>
    public class ArsistModelRuntimeLoader : MonoBehaviour
    {
        [Tooltip("モデルファイルのパス（StreamingAssets相対またはURL）")]
        public string modelPath;
        
        [Tooltip("読み込み完了後に自動でこのコンポーネントを削除")]
        public bool destroyAfterLoad = true;

        private async void Start()
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                Debug.LogWarning("[ArsistModelLoader] modelPath is empty");
                return;
            }

            await LoadModelAsync();
        }

        private async Task LoadModelAsync()
        {
#if GLTFAST
            try
            {
                var gltf = new GltfImport();
                
                // StreamingAssetsから読む場合
                string fullPath = modelPath;
                if (!modelPath.StartsWith("http") && !System.IO.Path.IsPathRooted(modelPath))
                {
                    fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, modelPath);
                }

                bool success = await gltf.Load(fullPath);
                
                if (success)
                {
                    await gltf.InstantiateMainSceneAsync(transform);
                    Debug.Log($"[ArsistModelLoader] Loaded: {modelPath}");
                }
                else
                {
                    Debug.LogError($"[ArsistModelLoader] Failed to load: {modelPath}");
                }

                if (destroyAfterLoad)
                {
                    Destroy(this);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistModelLoader] Exception: {e.Message}");
            }
#else
            Debug.LogWarning("[ArsistModelLoader] glTFast package not installed. Model will not load.");
            await Task.CompletedTask;
#endif
        }
    }
}
