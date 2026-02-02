// ==============================================
// Arsist Engine - 3D HTML Canvas Manager
// UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/UI/ArsistHtmlCanvas3D.cs
// ==============================================
// Arsist Engine API経由で3D空間にHTML Canvasを配置
// scene.createHtmlCanvas3D() で使用
// ==============================================

using UnityEngine;
using Arsist.Runtime.UI;

namespace Arsist.Runtime.UI
{
    /// <summary>
    /// Arsist EngineからのAPI呼び出しで3D HTML Canvasを管理
    /// </summary>
    public class ArsistHtmlCanvas3DManager : MonoBehaviour
    {
        private static ArsistHtmlCanvas3DManager _instance;
        
        public static ArsistHtmlCanvas3DManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ArsistHtmlCanvas3DManager");
                    _instance = go.AddComponent<ArsistHtmlCanvas3DManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>
        /// 3D空間にHTML Canvasを持つQuadを作成
        /// </summary>
        public GameObject CreateHtmlQuad(string id, Vector3 position, Vector3 rotation, Vector2 size, string htmlContent)
        {
            var quad = ArsistWorldCanvas.CreateQuad(
                position,
                Quaternion.Euler(rotation),
                size,
                new Vector2(1920, 1080)
            );
            
            quad.gameObject.name = $"HtmlCanvas3D_{id}";
            quad.LoadHTML(htmlContent);
            
            Debug.Log($"[ArsistHtmlCanvas3D] Created Quad: {id} at {position}");
            return quad.gameObject;
        }

        /// <summary>
        /// 3D空間にHTML Canvasを持つCubeを作成
        /// </summary>
        public GameObject CreateHtmlCube(string id, Vector3 position, Vector3 rotation, Vector3 size, string htmlContent)
        {
            var cube = ArsistWorldCanvas.CreateCube(
                position,
                Quaternion.Euler(rotation),
                size,
                new Vector2(1920, 1080)
            );
            
            cube.gameObject.name = $"HtmlCanvas3D_{id}";
            cube.LoadHTML(htmlContent);
            
            Debug.Log($"[ArsistHtmlCanvas3D] Created Cube: {id} at {position}");
            return cube.gameObject;
        }

        /// <summary>
        /// 既存の3DオブジェクトにHTML Canvasを追加
        /// </summary>
        public ArsistWorldCanvas AttachHtmlToObject(GameObject targetObject, string htmlContent)
        {
            var worldCanvas = ArsistWorldCanvas.AttachTo3DObject(targetObject, new Vector2(1920, 1080));
            worldCanvas.LoadHTML(htmlContent);
            
            Debug.Log($"[ArsistHtmlCanvas3D] Attached to: {targetObject.name}");
            return worldCanvas;
        }

        /// <summary>
        /// IDでHTML Canvasオブジェクトを検索
        /// </summary>
        public GameObject FindHtmlCanvas(string id)
        {
            return GameObject.Find($"HtmlCanvas3D_{id}");
        }

        /// <summary>
        /// HTML Canvasオブジェクトを削除
        /// </summary>
        public void DestroyHtmlCanvas(string id)
        {
            var obj = FindHtmlCanvas(id);
            if (obj != null)
            {
                Destroy(obj);
                Debug.Log($"[ArsistHtmlCanvas3D] Destroyed: {id}");
            }
        }
    }
}
