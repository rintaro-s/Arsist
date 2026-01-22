using System.Threading.Tasks;
using UnityEngine;
#if GLTFAST
using GLTFast;
using GLTFast.Materials;
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
                // StreamingAssetsから読む場合のパス構築
                string fullPath = modelPath;
                if (!modelPath.StartsWith("http") && !System.IO.Path.IsPathRooted(modelPath))
                {
                    var basePath = Application.streamingAssetsPath;
                    // Android: jar:file:///path/to.apk!/assets/
                    // パスの正規化
                    if (!basePath.EndsWith("/")) basePath += "/";
                    fullPath = basePath + modelPath;
                }

                Debug.Log($"[ArsistModelLoader] Loading model: {modelPath}");
                Debug.Log($"[ArsistModelLoader] Full path: {fullPath}");
                Debug.Log($"[ArsistModelLoader] StreamingAssets base: {Application.streamingAssetsPath}");

#if !UNITY_ANDROID || UNITY_EDITOR
                // エディタ/PCではローカルファイルとして存在チェック
                if (!fullPath.StartsWith("http") && !fullPath.StartsWith("jar:") && System.IO.Path.IsPathRooted(fullPath) && !System.IO.File.Exists(fullPath))
                {
                    Debug.LogError($"[ArsistModelLoader] File not found: {fullPath} (modelPath={modelPath})");
                    return;
                }
#endif

                // デフォルトのMaterialGeneratorは環境によりShaderGraph系を選び、
                // AndroidビルドでシェーダーがストリップされるとInstantiate中にNREになることがある。
                // BuiltInMaterialGeneratorを明示して安定側に寄せる。
                var gltf = new GltfImport(materialGenerator: new BuiltInMaterialGenerator());
                bool loaded = await gltf.Load(fullPath);
                
                if (loaded)
                {
                    Debug.Log($"[ArsistModelLoader] GLB file loaded successfully");
                    Debug.Log($"[ArsistModelLoader] SceneCount: {gltf.SceneCount}");
                    Debug.Log($"[ArsistModelLoader] DefaultSceneIndex: {gltf.DefaultSceneIndex}");
                    
                    if (gltf.SceneCount == 0)
                    {
                        Debug.LogError($"[ArsistModelLoader] GLB has no scenes: {modelPath}");
                        return;
                    }
                    
                    if (transform == null)
                    {
                        Debug.LogError($"[ArsistModelLoader] transform is null! Cannot instantiate model: {modelPath}");
                        return;
                    }
                    
                    Debug.Log($"[ArsistModelLoader] Instantiating scene to transform: {transform.name} at position {transform.position}");
                    var instantiated = await gltf.InstantiateMainSceneAsync(transform);
                    if (instantiated)
                    {
                        Debug.Log($"[ArsistModelLoader] Model instantiated successfully: {modelPath}");
                        Debug.Log($"[ArsistModelLoader] Instantiated at world position: {transform.position}");
                    }
                    else
                    {
                        Debug.LogError($"[ArsistModelLoader] InstantiateMainSceneAsync returned false: {modelPath}");
                    }
                }
                else
                {
                    Debug.LogError($"[ArsistModelLoader] gltf.Load() failed: {modelPath} (fullPath={fullPath})");
                }

                if (destroyAfterLoad)
                {
                    Destroy(this);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistModelLoader] Exception while loading: {modelPath}");
                Debug.LogError($"[ArsistModelLoader] Exception type: {e.GetType().Name}");
                Debug.LogError($"[ArsistModelLoader] Exception message: {e.Message}");
                Debug.LogException(e);
            }
#else
            Debug.LogWarning("[ArsistModelLoader] glTFast package not installed. Model will not load.");
            await Task.CompletedTask;
#endif
        }
    }
}
