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
            LoadHtmlUIAndroid(htmlFullPath);
#else
            LoadHtmlUIFallback(htmlFullPath);
#endif

            _isLoaded = true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void LoadHtmlUIAndroid(string htmlPath)
        {
            try
            {
                // Android WebView を使って透明なオーバーレイUIを表示
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    _currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    
                    _currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        SetupWebView(htmlPath);
                    }));
                }
                
                Debug.Log($"[ArsistWebView] HTML UI loaded (Android): {htmlPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebView] Failed to load HTML UI on Android: {e.Message}");
            }
        }

        private void SetupWebView(string htmlPath)
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
                
                // StreamingAssets内のHTMLをロード
                var url = htmlPath.StartsWith("file://") ? htmlPath : $"file://{htmlPath}";
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
                var canvasGO = new GameObject("HtmlUI_Fallback_Canvas");
                canvasGO.transform.position = _worldPosition;
                
                _targetCanvas = canvasGO.AddComponent<Canvas>();
                _targetCanvas.renderMode = RenderMode.WorldSpace;
                
                var rectTransform = canvasGO.GetComponent<RectTransform>();
                rectTransform.sizeDelta = _size;
                rectTransform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                
                canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
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
                        _webView?.Call("destroy");
                    }));
                }
                catch { }
            }
#endif
        }
    }
}
