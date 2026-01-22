// ==============================================
// Arsist Engine - WebView UI for HTML
// UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/UI/ArsistWebViewUI.cs
// ==============================================

using UnityEngine;
using System.IO;

namespace Arsist.Runtime.UI
{
    /// <summary>
    /// StreamingAssets内のHTML UIを表示するコンポーネント
    /// Androidでは透明背景のWebViewとして動作
    /// </summary>
    public class ArsistWebViewUI : MonoBehaviour
    {
        [Header("HTML Settings")]
        [SerializeField] private string _htmlPath = "ArsistUI/index.html";
        [SerializeField] private bool _autoLoad = true;
        
        [Header("WebView Placement")]
        [SerializeField] private Canvas _targetCanvas;
        [SerializeField] private Vector2 _size = new Vector2(1920, 1080);
        [SerializeField] private Vector3 _worldPosition = new Vector3(0, 1.5f, 2f);

        private bool _isLoaded = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _webView;
        private AndroidJavaObject _currentActivity;
#endif

        private void Start()
        {
            if (_autoLoad)
            {
                LoadHtmlUI();
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
            // まずは本来のHTML UI（Android WebView）を試す。
            // XR環境で表示されない端末があるため、その場合はfallbackに落とす。
            try
            {
                LoadHtmlUIAndroid(_htmlPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ArsistWebView] WebView init failed, falling back to World Space Canvas: {e.Message}");
                LoadHtmlUIFallback(htmlFullPath);
            }
#else
            // エディタ/非Android環境: World Space Canvasで表示
            LoadHtmlUIFallback(htmlFullPath);
#endif

            _isLoaded = true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void LoadHtmlUIAndroid(string htmlRelativePath)
        {
            try
            {
                // Android WebView を使って透明なオーバーレイUIを表示
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    _currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    
                    _currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        SetupWebView(htmlRelativePath);
                    }));
                }

                Debug.Log($"[ArsistWebView] HTML UI loaded (Android): {htmlRelativePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebView] Failed to load HTML UI on Android: {e.Message}");
            }
        }

        private void SetupWebView(string htmlRelativePath)
        {
            try
            {
                var webViewClass = new AndroidJavaClass("android.webkit.WebView");
                _webView = new AndroidJavaObject("android.webkit.WebView", _currentActivity);
                
                // 透明背景設定
                _webView.Call("setBackgroundColor", 0); // Transparent
                
                var settings = _webView.Call<AndroidJavaObject>("getSettings");
                settings.Call("setJavaScriptEnabled", true);
                settings.Call("setDomStorageEnabled", true);
                settings.Call("setAllowFileAccess", true);
                settings.Call("setAllowContentAccess", true);

                // UHD向け: 初期スケールを100%に固定
                _webView.Call("setInitialScale", 100);
                
                // StreamingAssets内のHTMLをロード
                var rel = (htmlRelativePath ?? "").TrimStart('/');
                // StreamingAssets は APK の assets 配下に格納されるため、android_asset 経由で読む
                var url = rel.StartsWith("file://") ? rel : $"file:///android_asset/{rel}";
                _webView.Call("loadUrl", url);
                
                // ViewをActivityに追加
                var layoutParams = new AndroidJavaObject(
                    "android.view.ViewGroup$LayoutParams",
                    -1, // MATCH_PARENT
                    -1  // MATCH_PARENT
                );
                
                _currentActivity.Call("addContentView", _webView, layoutParams);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebView] Failed to setup WebView: {e.Message}");
            }
        }
#endif

        private void LoadHtmlUIFallback(string htmlPath)
        {
            // エディタ/非Android環境: 3D Canvas上にテキストで表示
            if (_targetCanvas == null)
            {
                // XREALはワールド座標で直接表示できるため、親を設定せずワールド座標で配置
                var canvasGO = new GameObject("HtmlUI_WorldCanvas");
                canvasGO.transform.position = _worldPosition;
                
                _targetCanvas = canvasGO.AddComponent<Canvas>();
                _targetCanvas.renderMode = RenderMode.WorldSpace;
                
                var rectTransform = canvasGO.GetComponent<RectTransform>();
                rectTransform.sizeDelta = _size;
                // XR空間用にスケールを調整（1m = 1000Unity units の想定）
                rectTransform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                
                canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                
                Debug.Log($"[ArsistWebView] World Space Canvas created at {_worldPosition}");
            }

            // HTMLファイルの存在確認
            if (!File.Exists(htmlPath))
            {
                Debug.LogWarning($"[ArsistWebView] HTML file not found: {htmlPath}");
                CreateFallbackUI("HTML UI not found in StreamingAssets");
                return;
            }

            try
            {
                var htmlContent = File.ReadAllText(htmlPath);
                CreateFallbackUI($"HTML UI Preview:\n\n{htmlContent.Substring(0, Mathf.Min(500, htmlContent.Length))}...");
                Debug.Log($"[ArsistWebView] HTML UI loaded (Fallback): {htmlPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebView] Failed to read HTML: {e.Message}");
                CreateFallbackUI($"Error loading HTML: {e.Message}");
            }
        }

        private void CreateFallbackUI(string message)
        {
            if (_targetCanvas == null) return;

            var textGO = new GameObject("HTML_Preview");
            textGO.transform.SetParent(_targetCanvas.transform, false);
            
            var text = textGO.AddComponent<TMPro.TextMeshProUGUI>();
            text.text = message;
            text.fontSize = 16;
            text.color = Color.white;
            text.alignment = TMPro.TextAlignmentOptions.TopLeft;
            
            var rectTransform = textGO.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(50, 50);
            rectTransform.offsetMax = new Vector2(-50, -50);
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
