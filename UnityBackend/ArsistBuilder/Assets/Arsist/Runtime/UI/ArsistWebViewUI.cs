// ==============================================
// Arsist Engine - WebView UI for HTML HUD
// UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/UI/ArsistWebViewUI.cs
// ==============================================

using UnityEngine;
using System.IO;

namespace Arsist.Runtime.UI
{
    /// <summary>
    /// StreamingAssets内のHTML UIをXR空間のHUDとして表示するコンポーネント
    /// 
    /// Android実機: 透明背景の全画面WebViewを画面オーバーレイ
    /// エディタ/XR環境: World Space Canvasでカメラの前方に追従配置
    /// 
    /// 透明部分は3Dモデルが見える（XREALの黒=透過仕様に対応）
    /// </summary>
    public class ArsistWebViewUI : MonoBehaviour
    {
        [Header("HTML Settings")]
        [SerializeField] private string _htmlPath = "ArsistUI/index.html";
        [SerializeField] private bool _autoLoad = true;
        
        [Header("HUD Display Settings")]
        [SerializeField] private Vector2 _canvasSize = new Vector2(3840, 2160); // UHD解像度
        [SerializeField] private float _distanceFromCamera = 2.0f; // カメラからの距離（メートル）
        [SerializeField] private float _canvasScale = 0.001f; // 3D空間でのスケール（1m = 1000Unity units）
        [SerializeField] private int _sortingOrder = 9999; // 最前面描画

        private bool _isLoaded = false;
        private Canvas _hudCanvas;
        private Camera _mainCamera;
        private Transform _cameraTransform;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _webView;
        private AndroidJavaObject _currentActivity;
#endif

        private void Start()
        {
            // Main Cameraを取得（XR Origin > Camera Offset > Main Camera）
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                Debug.LogError("[ArsistWebView] Main Camera not found! HUD cannot be displayed.");
                return;
            }
            _cameraTransform = _mainCamera.transform;

            if (_autoLoad)
            {
                LoadHtmlUI();
            }
        }

        private void Update()
        {
            // HUD Canvasを常にカメラの前方に配置（ユーザーの視界に追従）
            if (_hudCanvas != null && _cameraTransform != null)
            {
                var forward = _cameraTransform.forward;
                var targetPos = _cameraTransform.position + forward * _distanceFromCamera;
                _hudCanvas.transform.position = targetPos;
                _hudCanvas.transform.rotation = Quaternion.LookRotation(forward);
            }
        }

        public void LoadHtmlUI()
        {
            if (_isLoaded)
            {
                Debug.LogWarning("[ArsistWebView] HTML UI already loaded");
                return;
            }

            var htmlFullPath = Path.Combine(Application.streamingAssetsPath, _htmlPath);

#if UNITY_ANDROID && !UNITY_EDITOR
            // Android実機: 画面全体に透明WebViewをオーバーレイ
            try
            {
                LoadHtmlUIAndroid(_htmlPath);
                Debug.Log($"[ArsistWebView] HTML UI loaded as full-screen WebView overlay: {_htmlPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebView] WebView init failed: {e.Message}");
                LoadHtmlUIFallback(htmlFullPath);
            }
#else
            // エディタ/非Android環境: Screen Space Overlay Canvas で画面に直接表示
            LoadHtmlUIFallback(htmlFullPath);
            Debug.Log($"[ArsistWebView] HTML UI loaded as Screen Space Overlay (editor/fallback): {_htmlPath}");
#endif

            _isLoaded = true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void LoadHtmlUIAndroid(string htmlRelativePath)
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                
                _currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    SetupWebView(htmlRelativePath);
                }));
            }
        }

        private void SetupWebView(string htmlRelativePath)
        {
            try
            {
                // WebView を作成（画面全体に透明オーバーレイ）
                _webView = new AndroidJavaObject("android.webkit.WebView", _currentActivity);
                
                // 透明背景設定（黒=透過のXREAL仕様に対応）
                _webView.Call("setBackgroundColor", 0); // Transparent (0x00000000)
                
                // レイヤータイプを SOFTWARE に設定（ハードウェアアクセラレーションでの透過問題を回避）
                try 
                { 
                    _webView.Call("setLayerType", 1, null); // 1 = LAYER_TYPE_SOFTWARE
                    Debug.Log("[ArsistWebView] WebView layer type set to SOFTWARE for transparency");
                } 
                catch (System.Exception e) 
                { 
                    Debug.LogWarning($"[ArsistWebView] Failed to set layer type: {e.Message}");
                }
                
                // WebView設定
                var settings = _webView.Call<AndroidJavaObject>("getSettings");
                settings.Call("setJavaScriptEnabled", true);
                settings.Call("setDomStorageEnabled", true);
                settings.Call("setAllowFileAccess", true);
                settings.Call("setAllowContentAccess", true);

                // file:// URL からの参照を許可（CSS/JS/画像の読み込み）
                try { settings.Call("setAllowFileAccessFromFileURLs", true); } catch { }
                try { settings.Call("setAllowUniversalAccessFromFileURLs", true); } catch { }

                // ビューポート設定（UHD HTMLの表示最適化）
                try { settings.Call("setUseWideViewPort", true); } catch { }
                try { settings.Call("setLoadWithOverviewMode", true); } catch { }
                
                // 拡大縮小を許可
                try { settings.Call("setSupportZoom", true); } catch { }
                try { settings.Call("setBuiltInZoomControls", false); } catch { }

                // UHD向け: 端末解像度に合わせて初期スケールを計算
                // scale[%] = deviceWidth / designWidth * 100
                var initialScale = 100;
                try
                {
                    var resources = _currentActivity.Call<AndroidJavaObject>("getResources");
                    var metrics = resources.Call<AndroidJavaObject>("getDisplayMetrics");
                    var widthPx = metrics.Get<int>("widthPixels");
                    var designW = Mathf.Max(1f, _canvasSize.x);
                    var s = Mathf.RoundToInt((widthPx / designW) * 100f);
                    initialScale = Mathf.Clamp(s, 10, 200);
                    Debug.Log($"[ArsistWebView] Calculated initial scale: {initialScale}% (device: {widthPx}px, design: {designW}px)");
                }
                catch (System.Exception e)
                { 
                    Debug.LogWarning($"[ArsistWebView] Failed to calculate initial scale: {e.Message}");
                }
                _webView.Call("setInitialScale", initialScale);
                
                // StreamingAssets 内の HTML をロード
                // Unity APK: assets/bin/Data/StreamingAssets/...
                var rel = (htmlRelativePath ?? "").TrimStart('/');
                if (!rel.StartsWith("bin/", System.StringComparison.Ordinal))
                {
                    rel = $"bin/Data/StreamingAssets/{rel}";
                }
                var url = $"file:///android_asset/{rel}";
                _webView.Call("loadUrl", url);
                Debug.Log($"[ArsistWebView] WebView loading: {url} (scale={initialScale}%)");
                
                // 画面全体に配置（MATCH_PARENT = -1）
                var layoutParams = new AndroidJavaObject(
                    "android.view.ViewGroup$LayoutParams",
                    -1, // MATCH_PARENT (width)
                    -1  // MATCH_PARENT (height)
                );
                
                _currentActivity.Call("addContentView", _webView, layoutParams);
                Debug.Log("[ArsistWebView] WebView added as full-screen transparent overlay");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebView] Failed to setup WebView: {e.Message}");
                throw;
            }
        }
#endif

        private void LoadHtmlUIFallback(string htmlPath)
        {
            // エディタ/XR環境: World Space Canvasでカメラ追従HUDを作成
            if (_hudCanvas == null)
            {
                var canvasGO = new GameObject("HtmlUI_WorldSpaceHUD");
                canvasGO.transform.SetParent(transform);
                
                _hudCanvas = canvasGO.AddComponent<Canvas>();
                _hudCanvas.renderMode = RenderMode.WorldSpace; // XR空間に配置
                _hudCanvas.worldCamera = _mainCamera; // XR Cameraを参照
                _hudCanvas.sortingOrder = _sortingOrder; // 最前面描画
                
                var rectTransform = canvasGO.GetComponent<RectTransform>();
                rectTransform.sizeDelta = _canvasSize;
                rectTransform.localScale = new Vector3(_canvasScale, _canvasScale, _canvasScale);
                
                // 透明背景（黒=透過のXREAL仕様に対応）
                var canvasGroup = canvasGO.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 1.0f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
                
                var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1.0f;
                
                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                
                Debug.Log($"[ArsistWebView] World Space HUD Canvas created (size: {_canvasSize}, distance: {_distanceFromCamera}m, sortingOrder: {_sortingOrder})");
            }

            // HTMLファイルの存在確認と内容表示
            if (!File.Exists(htmlPath))
            {
                Debug.LogWarning($"[ArsistWebView] HTML file not found: {htmlPath}");
                CreateHudText("HTML UI not found in StreamingAssets:\n" + htmlPath, Color.yellow);
                return;
            }

            try
            {
                var htmlContent = File.ReadAllText(htmlPath);
                var preview = htmlContent.Length > 500 
                    ? htmlContent.Substring(0, 500) + "..." 
                    : htmlContent;
                CreateHudText($"HTML HUD Preview (Editor/Fallback)\n\n{preview}", Color.white);
                Debug.Log($"[ArsistWebView] HTML HUD loaded (Fallback): {htmlPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebView] Failed to read HTML: {e.Message}");
                CreateHudText($"Error loading HTML:\n{e.Message}", Color.red);
            }
        }

        private void CreateHudText(string message, Color color)
        {
            if (_hudCanvas == null) return;

            var textGO = new GameObject("HTML_HUD_Text");
            textGO.transform.SetParent(_hudCanvas.transform, false);
            
            var text = textGO.AddComponent<TMPro.TextMeshProUGUI>();
            text.text = message;
            text.fontSize = 48; // World Spaceなので大きめに
            text.color = color;
            text.alignment = TMPro.TextAlignmentOptions.TopLeft;
            
            // 透明背景（黒=透過）
            text.enableWordWrapping = true;
            
            var rectTransform = textGO.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(100, 100);
            rectTransform.offsetMax = new Vector2(-100, -100);
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
}
