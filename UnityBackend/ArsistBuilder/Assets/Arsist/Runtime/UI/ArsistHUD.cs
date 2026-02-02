// ==============================================
// Arsist Engine - XREAL HUD System
// UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/UI/ArsistHUD.cs
// ==============================================
// WebView → RenderTexture → Canvas パイプライン
// Head Lock + 30fps更新でXREAL HUD最適化
// 3D空間配置対応（Cube等に貼り付け可能）
// ==============================================

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.XR.XREAL;
#endif

namespace Arsist.Runtime.UI
{
    /// <summary>
    /// XREAL One用のHead Lock HUDシステム
    /// WebViewをRenderTextureにキャプチャして表示
    /// Beam Proコントローラー/マウス入力対応
    /// </summary>
    public class ArsistHUD : MonoBehaviour
    {
        [Header("HUD Settings")]
        [SerializeField] private float hudDistance = 0.9f; // 0.7-1.2m推奨
        [SerializeField] private float canvasWidth = 1.6f;  // メートル単位
        [SerializeField] private float canvasHeight = 0.9f;
        [SerializeField] private Vector3 hudOffset = new Vector3(0f, -0.15f, 0f); // 視野中央やや下
        [SerializeField] private int targetFrameRate = 72;
        
        [Header("Head Lock Settings")]
        [SerializeField] private bool enableHeadLock = true;
        [SerializeField] private float followSmoothness = 0.15f; // 0=即座, 1=追従なし

        private Canvas hudCanvas;
        private RectTransform canvasRectTransform;
        private Camera mainCamera;
        private ArsistWebViewManager webViewManager;
        
        private Vector3 targetPosition;
        private Quaternion targetRotation;

        private void Awake()
        {
            Application.targetFrameRate = targetFrameRate;
            
#if UNITY_ANDROID && !UNITY_EDITOR
            // XREALPlugin イベント登録
            XREALPlugin.OnTrackingTypeChanged += OnXREALTrackingTypeChanged;
            
            var currentMode = XREALPlugin.GetTrackingType();
            Debug.Log($"[ArsistHUD] XREAL Tracking Mode: {currentMode}");
            
            bool supports6DOF = XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_PERCEPTION_HEAD_TRACKING_POSITION);
            Debug.Log($"[ArsistHUD] 6DOF Support: {supports6DOF}");
#endif
        }

        private void Start()
        {
            StartCoroutine(InitializeWithDelay());
        }

        private IEnumerator InitializeWithDelay()
        {
            // XR Originのカメラ待機
            float waitTime = 0f;
            const float maxWaitTime = 2f;
            
            while (mainCamera == null && waitTime < maxWaitTime)
            {
                mainCamera = TryFindMainCamera();
                if (mainCamera == null)
                {
                    yield return new WaitForSeconds(0.1f);
                    waitTime += 0.1f;
                }
            }

            if (mainCamera == null)
            {
                Debug.LogError("[ArsistHUD] Main Camera not found");
                yield break;
            }

            Debug.Log($"[ArsistHUD] Camera found: {mainCamera.name}");
            CreateHUDCanvas();
            
            // WebViewManager初期化
            webViewManager = gameObject.AddComponent<ArsistWebViewManager>();
            webViewManager.Initialize(canvasRectTransform);
            
            Debug.Log("[ArsistHUD] Head Lock HUD initialized");
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            XREALPlugin.OnTrackingTypeChanged -= OnXREALTrackingTypeChanged;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void OnXREALTrackingTypeChanged(bool success, TrackingType targetType)
        {
            var currentType = XREALPlugin.GetTrackingType();
            Debug.Log($"[ArsistHUD] Tracking changed: {currentType}");
            
            // 3DOF/6DOFに応じた調整可能
            if (currentType == TrackingType.MODE_3DOF)
            {
                // 3DOFモード: より近い距離
                hudDistance = 0.8f;
            }
            else if (currentType == TrackingType.MODE_6DOF)
            {
                // 6DOFモード: 標準距離
                hudDistance = 1.0f;
            }
        }
#endif

        private Camera TryFindMainCamera()
        {
            Camera cam = Camera.main;
            if (cam != null) return cam;

            cam = FindObjectOfType<Camera>();
            if (cam != null) return cam;

            GameObject camObj = GameObject.FindGameObjectWithTag("MainCamera");
            if (camObj != null)
            {
                cam = camObj.GetComponent<Camera>();
                if (cam != null) return cam;
            }

            var xrOrigin = GameObject.Find("XR Origin");
            if (xrOrigin != null)
            {
                cam = xrOrigin.GetComponentInChildren<Camera>();
                if (cam != null) return cam;
            }

            return null;
        }

        private void CreateHUDCanvas()
        {
            GameObject canvasObj = new GameObject("ArsistHUD_Canvas");
            canvasObj.transform.SetParent(transform);

            hudCanvas = canvasObj.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.WorldSpace;
            
            var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 100f;

            // XRコントローラー/マウス入力対応
            var raycaster = canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();

            canvasRectTransform = canvasObj.GetComponent<RectTransform>();
            canvasRectTransform.sizeDelta = new Vector2(canvasWidth * 1000f, canvasHeight * 1000f);

            UpdateCanvasPosition();

            Debug.Log("[ArsistHUD] World Space Canvas created");
        }

        private void LateUpdate()
        {
            if (mainCamera != null && canvasRectTransform != null && enableHeadLock)
            {
                UpdateCanvasPosition();
            }
        }

        private void UpdateCanvasPosition()
        {
            // Head Lock: カメラ前方の固定位置
            Vector3 forward = mainCamera.transform.forward;
            Vector3 right = mainCamera.transform.right;
            Vector3 up = mainCamera.transform.up;
            
            targetPosition = mainCamera.transform.position 
                + forward * hudDistance 
                + right * hudOffset.x 
                + up * hudOffset.y;
            
            targetRotation = Quaternion.LookRotation(forward, up);
            
            // スムーズ追従
            if (followSmoothness > 0.01f)
            {
                canvasRectTransform.position = Vector3.Lerp(
                    canvasRectTransform.position, 
                    targetPosition, 
                    1f - followSmoothness
                );
                canvasRectTransform.rotation = Quaternion.Slerp(
                    canvasRectTransform.rotation, 
                    targetRotation, 
                    1f - followSmoothness
                );
            }
            else
            {
                canvasRectTransform.position = targetPosition;
                canvasRectTransform.rotation = targetRotation;
            }
        }

        public void LoadHTML(string htmlContent)
        {
            if (webViewManager != null)
            {
                webViewManager.LoadHTMLString(htmlContent);
            }
        }
        
        public void LoadURL(string url)
        {
            if (webViewManager != null)
            {
                webViewManager.LoadURL(url);
            }
        }
    }

    /// <summary>
    /// 3D空間に固定配置できるHTML Canvas
    /// Cube, Plane, Quadなどの3Dオブジェクトに貼り付け可能
    /// Beam Proコントローラー/マウスで操作可能
    /// </summary>
    public class ArsistWorldCanvas : MonoBehaviour
    {
        private RenderTexture renderTexture;
        private ArsistWebViewManager webViewManager;
        private MeshRenderer meshRenderer;
        private Material material;

        /// <summary>
        /// 既存の3DオブジェクトにHTML Canvasを設定
        /// </summary>
        public static ArsistWorldCanvas AttachTo3DObject(GameObject targetObject, Vector2 textureSize)
        {
            var worldCanvas = targetObject.AddComponent<ArsistWorldCanvas>();
            worldCanvas.Initialize(textureSize);
            return worldCanvas;
        }

        /// <summary>
        /// 新しいQuadを作成してHTML Canvasを配置
        /// </summary>
        public static ArsistWorldCanvas CreateQuad(Vector3 position, Quaternion rotation, Vector2 size, Vector2 textureSize)
        {
            GameObject quadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadObj.name = "ArsistWorldCanvas_Quad";
            quadObj.transform.position = position;
            quadObj.transform.rotation = rotation;
            quadObj.transform.localScale = new Vector3(size.x, size.y, 1f);

            // Collider追加（Beam Pro入力用）
            if (quadObj.GetComponent<Collider>() == null)
            {
                quadObj.AddComponent<MeshCollider>();
            }

            var worldCanvas = quadObj.AddComponent<ArsistWorldCanvas>();
            worldCanvas.Initialize(textureSize);

            return worldCanvas;
        }

        /// <summary>
        /// 新しいCubeを作成してHTML Canvasを配置
        /// </summary>
        public static ArsistWorldCanvas CreateCube(Vector3 position, Quaternion rotation, Vector3 size, Vector2 textureSize)
        {
            GameObject cubeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeObj.name = "ArsistWorldCanvas_Cube";
            cubeObj.transform.position = position;
            cubeObj.transform.rotation = rotation;
            cubeObj.transform.localScale = size;

            var worldCanvas = cubeObj.AddComponent<ArsistWorldCanvas>();
            worldCanvas.Initialize(textureSize);

            return worldCanvas;
        }

        private void Initialize(Vector2 textureSize)
        {
            // WebViewManager初期化
            webViewManager = gameObject.AddComponent<ArsistWebViewManager>();
            
            // ダミーRectTransform作成（WebViewManager用）
            GameObject dummyCanvas = new GameObject("DummyCanvas");
            dummyCanvas.transform.SetParent(transform);
            var canvas = dummyCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rectTransform = dummyCanvas.GetComponent<RectTransform>();
            rectTransform.sizeDelta = textureSize;
            
            webViewManager.Initialize(rectTransform);
            
            // RenderTextureを取得してMaterialに適用
            StartCoroutine(ApplyRenderTextureWhenReady());
            
            Debug.Log($"[ArsistWorldCanvas] Initialized on {gameObject.name}");
        }

        private IEnumerator ApplyRenderTextureWhenReady()
        {
            // WebViewManagerの初期化を待つ
            yield return new WaitForSeconds(0.5f);
            
            renderTexture = webViewManager.GetRenderTexture();
            
            if (renderTexture == null)
            {
                Debug.LogError("[ArsistWorldCanvas] RenderTexture not available");
                yield break;
            }

            // MeshRendererを取得または作成
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                Debug.LogError("[ArsistWorldCanvas] No MeshRenderer found on object");
                yield break;
            }

            // 新しいマテリアル作成
            material = new Material(Shader.Find("Unlit/Texture"));
            material.mainTexture = renderTexture;
            meshRenderer.material = material;

            Debug.Log($"[ArsistWorldCanvas] RenderTexture applied to {gameObject.name}");
        }

        public void LoadHTML(string htmlContent)
        {
            if (webViewManager != null)
            {
                webViewManager.LoadHTMLString(htmlContent);
            }
        }

        public void LoadURL(string url)
        {
            if (webViewManager != null)
            {
                webViewManager.LoadURL(url);
            }
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
