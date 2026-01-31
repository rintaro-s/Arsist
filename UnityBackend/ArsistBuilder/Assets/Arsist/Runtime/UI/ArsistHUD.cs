// ==============================================
// Arsist Engine - XR HUD System
// UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/UI/ArsistHUD.cs
// ==============================================
// XREAL SDK 3.x + XR Interaction Toolkit 準拠の常時表示HUDシステム
// 
// 機能:
// 1. カメラに追従する常時表示HUD（World Space Canvas）
// 2. 3D空間上に配置可能なHTMLキャンバス
// 3. Android WebView / エディタ両対応
// 4. 透明背景（XREAL: 黒=透過）
// ==============================================

using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;

namespace Arsist.Runtime.UI
{
    /// <summary>
    /// XR空間で常時表示されるHUDシステム
    /// カメラに追従し、最前面に描画される
    /// </summary>
    public class ArsistHUD : MonoBehaviour
    {
        [Header("HUD Settings")]
        [SerializeField] private string _htmlPath = "ArsistUI/index.html";
        [SerializeField] private bool _autoLoad = true;
        
        [Header("Canvas Settings")]
        [Tooltip("HUDのカメラからの距離（メートル）")]
        [SerializeField] private float _hudDistance = 1.5f;
        
        [Tooltip("HUDのサイズ（ピクセル）")]
        [SerializeField] private Vector2 _hudSize = new Vector2(1920, 1080);
        
        [Tooltip("HUDの3D空間でのスケール")]
        [SerializeField] private float _hudScale = 0.001f;
        
        [Tooltip("追従の滑らかさ（0=即座に追従, 1=ゆっくり追従）")]
        [Range(0f, 0.99f)]
        [SerializeField] private float _followSmoothness = 0.1f;

        [Header("Rendering")]
        [Tooltip("最前面描画用のSorting Order")]
        [SerializeField] private int _sortingOrder = 32767;

        // Internal
        private Canvas _hudCanvas;
        private RectTransform _hudRectTransform;
        private Camera _mainCamera;
        private Transform _cameraTransform;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private bool _isInitialized;
        private RawImage _webViewDisplay;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _webView;
        private AndroidJavaObject _currentActivity;
        private bool _webViewReady;
#endif

        private void Awake()
        {
            // シーンロード後に確実にカメラを取得するため、Awakeで初期化を開始
            // ただし、XR Originが後から生成される場合があるため、Startでも再試行
        }

        private void Start()
        {
            // 少し遅延させてカメラ検索（XR Originの初期化を待つ）
            StartCoroutine(InitializeWithDelay());
        }

        private System.Collections.IEnumerator InitializeWithDelay()
        {
            // 最大2秒間、カメラが見つかるまで待機
            float timeout = 2f;
            float elapsed = 0f;

            while (_mainCamera == null && elapsed < timeout)
            {
                TryFindMainCamera();
                if (_mainCamera != null) break;
                
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (_mainCamera == null)
            {
                Debug.LogError("[ArsistHUD] Main Camera not found after waiting! HUD will not display.");
                yield break;
            }

            _cameraTransform = _mainCamera.transform;
            Debug.Log($"[ArsistHUD] Main Camera found: {_mainCamera.name} (after {elapsed:F1}s)");

            // HUD Canvas を作成
            CreateHUDCanvas();

            _isInitialized = true;
            Debug.Log("[ArsistHUD] Initialized successfully");

            if (_autoLoad && !string.IsNullOrEmpty(_htmlPath))
            {
                LoadHtmlHUD(_htmlPath);
            }
        }

        private void TryFindMainCamera()
        {
            // 方法1: Camera.main
            _mainCamera = Camera.main;
            if (_mainCamera != null) return;

            // 方法2: "MainCamera"タグで検索
            var cameras = FindObjectsOfType<Camera>();
            foreach (var cam in cameras)
            {
                if (cam.CompareTag("MainCamera"))
                {
                    _mainCamera = cam;
                    return;
                }
            }

            // 方法3: XR Origin配下のカメラを検索
            var xrOrigins = FindObjectsOfType<Transform>();
            foreach (var t in xrOrigins)
            {
                if (t.name.Contains("XR Origin") || t.name.Contains("XREAL"))
                {
                    var cam = t.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        _mainCamera = cam;
                        return;
                    }
                }
            }

            // 方法4: Camera Offset配下を検索
            foreach (var t in xrOrigins)
            {
                if (t.name.Contains("Camera Offset"))
                {
                    var cam = t.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        _mainCamera = cam;
                        return;
                    }
                }
            }

            // 方法5: シーン内の任意のカメラ（最終手段）
            if (cameras.Length > 0)
            {
                _mainCamera = cameras[0];
            }
        }

        private void InitializeHUD()
        {
            // 旧メソッド - 互換性のため残す
            if (_isInitialized) return;
            TryFindMainCamera();
            if (_mainCamera != null)
            {
                _cameraTransform = _mainCamera.transform;
                CreateHUDCanvas();
                _isInitialized = true;
            }
        }

        private void CreateHUDCanvas()
        {
            // HUD用のGameObjectを作成
            var hudGO = new GameObject("ArsistHUD_Canvas");
            hudGO.transform.SetParent(transform);

            // Canvas設定（World Space）
            _hudCanvas = hudGO.AddComponent<Canvas>();
            _hudCanvas.renderMode = RenderMode.WorldSpace;
            _hudCanvas.worldCamera = _mainCamera;
            _hudCanvas.sortingOrder = _sortingOrder;

            // Sorting Layer を最前面に
            _hudCanvas.overrideSorting = true;

            // RectTransform設定
            _hudRectTransform = hudGO.GetComponent<RectTransform>();
            _hudRectTransform.sizeDelta = _hudSize;
            _hudRectTransform.localScale = Vector3.one * _hudScale;

            // CanvasScaler
            var scaler = hudGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            // GraphicRaycaster（XRI対応）
            hudGO.AddComponent<GraphicRaycaster>();

            // TrackedDeviceGraphicRaycaster（XR Interaction Toolkit用）
            TryAddComponent(hudGO, "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster");

            // 初期位置をカメラ前方に設定
            UpdateHUDPosition(true);

            Debug.Log($"[ArsistHUD] HUD Canvas created (size: {_hudSize}, distance: {_hudDistance}m, sortingOrder: {_sortingOrder})");
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _cameraTransform == null || _hudRectTransform == null)
                return;

            UpdateHUDPosition(false);
        }

        private void UpdateHUDPosition(bool immediate)
        {
            // カメラの前方にHUDを配置
            var camForward = _cameraTransform.forward;
            var camPos = _cameraTransform.position;
            
            _targetPosition = camPos + camForward * _hudDistance;
            _targetRotation = Quaternion.LookRotation(camForward);

            if (immediate || _followSmoothness <= 0f)
            {
                _hudRectTransform.position = _targetPosition;
                _hudRectTransform.rotation = _targetRotation;
            }
            else
            {
                // 滑らかな追従
                var t = 1f - _followSmoothness;
                _hudRectTransform.position = Vector3.Lerp(_hudRectTransform.position, _targetPosition, t);
                _hudRectTransform.rotation = Quaternion.Slerp(_hudRectTransform.rotation, _targetRotation, t);
            }
        }

        /// <summary>
        /// HTMLファイルをHUDとして読み込む
        /// </summary>
        public void LoadHtmlHUD(string htmlRelativePath)
        {
            if (!_isInitialized)
            {
                Debug.LogError("[ArsistHUD] Not initialized. Cannot load HTML.");
                return;
            }

            var fullPath = Path.Combine(Application.streamingAssetsPath, htmlRelativePath);

#if UNITY_ANDROID && !UNITY_EDITOR
            // Android: WebView をオーバーレイとして表示
            LoadAndroidWebView(htmlRelativePath);
#else
            // エディタ/その他: フォールバック表示
            LoadEditorFallback(fullPath);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void LoadAndroidWebView(string htmlRelativePath)
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    _currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    
                    _currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        SetupAndroidWebView(htmlRelativePath);
                    }));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistHUD] Failed to initialize Android WebView: {e.Message}");
            }
        }

        private void SetupAndroidWebView(string htmlRelativePath)
        {
            try
            {
                // WebView作成
                _webView = new AndroidJavaObject("android.webkit.WebView", _currentActivity);

                // 透明背景（XREAL: 黒=透過）
                _webView.Call("setBackgroundColor", 0x00000000);

                // レイヤータイプをSOFTWAREに（透過のため）
                try { _webView.Call("setLayerType", 1, null); } catch { }

                // WebView設定
                var settings = _webView.Call<AndroidJavaObject>("getSettings");
                settings.Call("setJavaScriptEnabled", true);
                settings.Call("setDomStorageEnabled", true);
                settings.Call("setAllowFileAccess", true);
                settings.Call("setAllowContentAccess", true);
                
                try { settings.Call("setAllowFileAccessFromFileURLs", true); } catch { }
                try { settings.Call("setAllowUniversalAccessFromFileURLs", true); } catch { }
                try { settings.Call("setUseWideViewPort", true); } catch { }
                try { settings.Call("setLoadWithOverviewMode", true); } catch { }

                // HTMLをロード
                var rel = (htmlRelativePath ?? "").TrimStart('/');
                if (!rel.StartsWith("bin/", System.StringComparison.Ordinal))
                {
                    rel = $"bin/Data/StreamingAssets/{rel}";
                }
                var url = $"file:///android_asset/{rel}";
                _webView.Call("loadUrl", url);

                // 全画面オーバーレイとして追加
                var layoutParams = new AndroidJavaObject(
                    "android.view.ViewGroup$LayoutParams",
                    -1, // MATCH_PARENT
                    -1  // MATCH_PARENT
                );
                _currentActivity.Call("addContentView", _webView, layoutParams);

                _webViewReady = true;
                Debug.Log($"[ArsistHUD] Android WebView loaded: {url}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistHUD] WebView setup failed: {e.Message}");
            }
        }
#endif

        private void LoadEditorFallback(string htmlFullPath)
        {
            // エディタ用のフォールバック表示
            if (_hudCanvas == null) return;

            // 背景パネル（半透明 - XR環境では黒=透過なので見える）
            var bgGO = new GameObject("HUD_Background");
            bgGO.transform.SetParent(_hudCanvas.transform, false);
            
            var bgRect = bgGO.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var bgImage = bgGO.AddComponent<RawImage>();
            // 透明度を持つ背景（完全な黒は透過されるので少し明るく）
            bgImage.color = new Color(0.05f, 0.05f, 0.1f, 0.7f);

            // 枠線（HUDの境界を示す）
            var borderGO = new GameObject("HUD_Border");
            borderGO.transform.SetParent(_hudCanvas.transform, false);
            
            var borderRect = borderGO.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(5, 5);
            borderRect.offsetMax = new Vector2(-5, -5);

            var borderOutline = borderGO.AddComponent<UnityEngine.UI.Outline>();

            // HTMLプレビューテキスト
            var textGO = new GameObject("HUD_Content");
            textGO.transform.SetParent(_hudCanvas.transform, false);

            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.02f, 0.02f);
            textRect.anchorMax = new Vector2(0.98f, 0.98f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGO.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.fontSize = 28;
            tmp.color = Color.white;
            tmp.alignment = TMPro.TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TMPro.TextOverflowModes.Truncate;

            // ステータスバー（上部）
            var statusGO = new GameObject("HUD_Status");
            statusGO.transform.SetParent(_hudCanvas.transform, false);

            var statusRect = statusGO.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 0.92f);
            statusRect.anchorMax = new Vector2(1, 1);
            statusRect.offsetMin = new Vector2(10, 0);
            statusRect.offsetMax = new Vector2(-10, -5);

            var statusText = statusGO.AddComponent<TMPro.TextMeshProUGUI>();
            statusText.fontSize = 20;
            statusText.color = new Color(0.3f, 1f, 0.5f, 1f); // 緑色
            statusText.alignment = TMPro.TextAlignmentOptions.TopLeft;
            statusText.text = "[Arsist HUD - Editor Preview Mode]";

            if (File.Exists(htmlFullPath))
            {
                try
                {
                    var htmlContent = File.ReadAllText(htmlFullPath);
                    
                    // HTMLタグを簡易的に除去してプレビュー
                    var cleanText = System.Text.RegularExpressions.Regex.Replace(htmlContent, "<[^>]+>", " ");
                    cleanText = System.Text.RegularExpressions.Regex.Replace(cleanText, @"\s+", " ");
                    
                    var preview = cleanText.Length > 800 
                        ? cleanText.Substring(0, 800) + "\n\n... (content truncated for preview)" 
                        : cleanText;
                    
                    tmp.text = $"HTML Content:\n\n{preview}";
                    statusText.text = $"[Arsist HUD] Loaded: {_htmlPath}";
                    
                    Debug.Log($"[ArsistHUD] Editor preview loaded: {htmlFullPath}");
                }
                catch (System.Exception e)
                {
                    tmp.text = $"Error reading HTML:\n{e.Message}";
                    statusText.text = "[Arsist HUD] Error loading HTML";
                    statusText.color = Color.red;
                    Debug.LogError($"[ArsistHUD] Failed to read HTML: {e.Message}");
                }
            }
            else
            {
                tmp.text = $"HTML file not found.\n\nExpected path:\n{htmlFullPath}\n\n" +
                           "Make sure to place your HTML files in:\n" +
                           "StreamingAssets/ArsistUI/index.html";
                statusText.text = "[Arsist HUD] HTML not found";
                statusText.color = Color.yellow;
                Debug.LogWarning($"[ArsistHUD] HTML file not found: {htmlFullPath}");
            }
        }

        private bool TryAddComponent(GameObject go, string typeName)
        {
            var type = System.Type.GetType(typeName);
            if (type == null)
            {
                // アセンブリを指定して検索
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(typeName);
                    if (type != null) break;
                }
            }

            if (type != null && go.GetComponent(type) == null)
            {
                go.AddComponent(type);
                return true;
            }
            return false;
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_webView != null)
            {
                try
                {
                    _currentActivity?.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try
                        {
                            var parent = _webView.Call<AndroidJavaObject>("getParent");
                            parent?.Call("removeView", _webView);
                        }
                        catch { }
                        _webView?.Call("destroy");
                    }));
                }
                catch { }
            }
#endif
        }
    }

    /// <summary>
    /// 3D空間上に配置可能なHTMLキャンバス
    /// 6DoF/3DoF環境で空間上の特定位置に固定表示
    /// </summary>
    public class ArsistWorldCanvas : MonoBehaviour
    {
        [Header("Canvas Settings")]
        [SerializeField] private Vector2 _canvasSize = new Vector2(1920, 1080);
        [SerializeField] private float _canvasScale = 0.001f;
        [SerializeField] private int _sortingOrder = 100;
        
        [Header("Content")]
        [SerializeField] private string _htmlPath;

        private Canvas _canvas;
        private RectTransform _rectTransform;

        /// <summary>
        /// 3D空間の指定位置にHTMLキャンバスを作成
        /// </summary>
        public static ArsistWorldCanvas Create(Vector3 worldPosition, Quaternion rotation, string htmlPath = null, Vector2? size = null)
        {
            var go = new GameObject("ArsistWorldCanvas");
            go.transform.position = worldPosition;
            go.transform.rotation = rotation;

            var worldCanvas = go.AddComponent<ArsistWorldCanvas>();
            if (size.HasValue) worldCanvas._canvasSize = size.Value;
            if (!string.IsNullOrEmpty(htmlPath)) worldCanvas._htmlPath = htmlPath;

            worldCanvas.Initialize();
            return worldCanvas;
        }

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_canvas != null) return;

            // Canvas設定（World Space）
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = Camera.main;
            _canvas.sortingOrder = _sortingOrder;

            // RectTransform
            _rectTransform = GetComponent<RectTransform>();
            _rectTransform.sizeDelta = _canvasSize;
            _rectTransform.localScale = Vector3.one * _canvasScale;

            // CanvasScaler
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            // GraphicRaycaster
            gameObject.AddComponent<GraphicRaycaster>();

            // XRI対応
            var trackedRaycasterType = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster");
            if (trackedRaycasterType != null)
            {
                gameObject.AddComponent(trackedRaycasterType);
            }

            Debug.Log($"[ArsistWorldCanvas] Created at {transform.position} (size: {_canvasSize})");
        }

        /// <summary>
        /// HTMLコンテンツを読み込む（エディタ用フォールバック）
        /// </summary>
        public void LoadHtml(string htmlRelativePath)
        {
            _htmlPath = htmlRelativePath;
            var fullPath = Path.Combine(Application.streamingAssetsPath, htmlRelativePath);

            // テキスト表示（エディタ用）
            var textGO = new GameObject("HTML_Content");
            textGO.transform.SetParent(transform, false);

            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20, 20);
            textRect.offsetMax = new Vector2(-20, -20);

            var tmp = textGO.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.fontSize = 24;
            tmp.color = Color.white;
            tmp.alignment = TMPro.TextAlignmentOptions.TopLeft;

            if (File.Exists(fullPath))
            {
                var content = File.ReadAllText(fullPath);
                tmp.text = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
            }
            else
            {
                tmp.text = $"HTML not found: {fullPath}";
            }
        }

        /// <summary>
        /// UIエレメントを追加
        /// </summary>
        public T AddUIElement<T>(string name = null) where T : Component
        {
            var go = new GameObject(name ?? typeof(T).Name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
