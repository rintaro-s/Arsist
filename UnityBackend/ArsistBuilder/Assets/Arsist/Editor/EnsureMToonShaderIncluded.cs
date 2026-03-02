using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

namespace Arsist.Editor
{
    /// <summary>
    /// ビルド前にMToonシェーダーをGraphicsSettings.alwaysIncludedShadersに確実に含める
    /// </summary>
    [InitializeOnLoad]
    public static class EnsureMToonShaderIncluded
    {
        static EnsureMToonShaderIncluded()
        {
            // エディタ起動時とスクリプトリロード時に実行
            EditorApplication.delayCall += EnsureShaders;
        }

        [MenuItem("Arsist/Ensure MToon Shaders Included")]
        public static void EnsureShaders()
        {
            // MToonシェーダーの検索パターン
            string[] mtoonShaderPaths = new string[]
            {
                "VRM/MToon",
                "VRM10/MToon10",
                "UniGLTF/UniUnlit",
                "Hidden/UniGLTF/NormalMapExporter"
            };

            // GraphicsSettingsをSerializedObjectとして取得
            var graphicsSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettingsAssets == null || graphicsSettingsAssets.Length == 0)
            {
                Debug.LogError("[Arsist] Could not load GraphicsSettings.asset");
                return;
            }

            var serializedObject = new SerializedObject(graphicsSettingsAssets[0]);
            var arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");

            if (arrayProp == null || !arrayProp.isArray)
            {
                Debug.LogError("[Arsist] Could not access m_AlwaysIncludedShaders in GraphicsSettings");
                return;
            }

            // 既存のシェーダーリストを取得
            var existingShaders = new HashSet<Shader>();
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var shaderProp = arrayProp.GetArrayElementAtIndex(i);
                var shader = shaderProp.objectReferenceValue as Shader;
                if (shader != null)
                {
                    existingShaders.Add(shader);
                }
            }

            bool modified = false;

            foreach (var shaderPath in mtoonShaderPaths)
            {
                var shader = Shader.Find(shaderPath);
                if (shader != null)
                {
                    if (!existingShaders.Contains(shader))
                    {
                        Debug.Log($"[Arsist] Adding shader to Always Included Shaders: {shaderPath}");
                        arrayProp.arraySize++;
                        var newElement = arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1);
                        newElement.objectReferenceValue = shader;
                        modified = true;
                    }
                }
                else
                {
                    // シェーダーが見つからない場合は警告（UniVRMインポート前は正常）
                    Debug.LogWarning($"[Arsist] Shader not found (will retry after UniVRM import): {shaderPath}");
                }
            }

            if (modified)
            {
                serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[Arsist] GraphicsSettings updated: {arrayProp.arraySize} shaders in Always Included Shaders");
            }
        }
    }

    /// <summary>
    /// ビルド直前にも再度確認
    /// </summary>
    public class PreBuildMToonEnsurer : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000; // 最初に実行

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            Debug.Log("[Arsist] Pre-build: Ensuring MToon shaders are included...");
            EnsureMToonShaderIncluded.EnsureShaders();

            // 現在のシェーダー数を確認
            var graphicsSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettingsAssets != null && graphicsSettingsAssets.Length > 0)
            {
                var serializedObject = new SerializedObject(graphicsSettingsAssets[0]);
                var arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");

                if (arrayProp != null && arrayProp.isArray)
                {
                    int mtoonCount = 0;
                    for (int i = 0; i < arrayProp.arraySize; i++)
                    {
                        var shader = arrayProp.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                        if (shader != null && (shader.name.Contains("MToon") || shader.name.Contains("UniGLTF")))
                        {
                            mtoonCount++;
                        }
                    }

                    if (mtoonCount > 0)
                    {
                        Debug.Log($"[Arsist] ✓ Build will include {mtoonCount} VRM shaders");
                    }
                    else
                    {
                        Debug.LogError("[Arsist] ✗ No VRM shaders found in Always Included Shaders! VRM materials may fail at runtime.");
                    }
                }
            }
        }
    }
}
