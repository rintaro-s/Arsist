// ==============================================
// Arsist Engine - WebView Manager
// UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/UI/ArsistWebViewManager.cs
// ==============================================
// WebView → RenderTexture → Canvas/Quad パイプライン
// XREAL HUD用に最適化（30fps更新、Head Lock対応）
// ==============================================

using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace Arsist.Runtime.UI
{
    /// <summary>
    /// Android WebViewをRenderTextureにキャプチャし、Unity UIに表示
    /// XREAL視界内HUD表示に最適化
    /// </summary>
    public class ArsistWebViewManager : MonoBehaviour
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject webView;
        private AndroidJavaObject currentActivity;
        private Texture2D captureTexture;
        private RenderTexture renderTexture;
        private RawImage targetImage;
        private bool isInitialized = false;
        private bool isCapturing = false;
        
        [Header("Capture Settings")]
        [SerializeField] private int textureWidth = 1920;
        [SerializeField] private int textureHeight = 1080;
        [SerializeField] private float captureInterval = 0.033f; // 30fps
        
        /// <summary>
        /// WebViewを初期化してRenderTextureパイプラインをセットアップ
        /// </summary>
        public void Initialize(RectTransform rect)
        {
            if (isInitialized)
            {
                Debug.LogWarning("[ArsistWebViewManager] Already initialized");
                return;
            }

            try
            {
                // RenderTexture作成
                renderTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32);
                renderTexture.Create();
                
                // Texture2D作成（WebViewからのキャプチャ用）
                captureTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
                
                // RawImageコンポーネントを追加してRenderTextureを割り当て
                GameObject imageObj = new GameObject("WebViewDisplay");
                imageObj.transform.SetParent(rect, false);
                
                targetImage = imageObj.AddComponent<RawImage>();
                targetImage.texture = renderTexture;
                
                RectTransform imageRect = imageObj.GetComponent<RectTransform>();
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.sizeDelta = Vector2.zero;
                imageRect.anchoredPosition = Vector2.zero;

                // Unity Playerから Activity 取得
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }

                // WebView作成（画面外に配置）
                currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    webView = new AndroidJavaObject("android.webkit.WebView", currentActivity);

                    // WebView設定
                    webView.Call("setBackgroundColor", 0); // Transparent
                    var settings = webView.Call<AndroidJavaObject>("getSettings");
                    settings.Call("setJavaScriptEnabled", true);
                    settings.Call("setDomStorageEnabled", true);
                    settings.Call("setAllowFileAccess", true);
                    settings.Call("setAllowContentAccess", true);
                    
                    // 描画を有効化
                    webView.Call("setDrawingCacheEnabled", true);
                    webView.Call("setLayerType", 2, null); // LAYER_TYPE_HARDWARE for better performance
                    
                    // WebViewを画面外に配置（実際の描画はRenderTextureに）
                    var layoutParams = new AndroidJavaObject(
                        "android.widget.FrameLayout$LayoutParams",
                        textureWidth,
                        textureHeight
                    );
                    layoutParams.Set("leftMargin", -10000); // 画面外
                    currentActivity.Call("addContentView", webView, layoutParams);
                }));

                isInitialized = true;
                
                // キャプチャループ開始（30fps）
                StartCoroutine(CaptureLoop());
                
                Debug.Log("[ArsistWebViewManager] WebView → RenderTexture pipeline initialized");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebViewManager] Failed to initialize: {e.Message}");
            }
        }
        
        /// <summary>
        /// WebViewを30fpsでキャプチャしてRenderTextureに転送
        /// </summary>
        private IEnumerator CaptureLoop()
        {
            var wait = new WaitForSeconds(captureInterval);
            
            while (isInitialized)
            {
                yield return wait;
                
                if (!isCapturing && webView != null)
                {
                    isCapturing = true;
                    CaptureWebViewToTexture();
                    isCapturing = false;
                }
            }
        }
        
        /// <summary>
        /// WebViewの内容をBitmapとしてキャプチャし、Texture2Dに転送
        /// </summary>
        private void CaptureWebViewToTexture()
        {
            try
            {
                currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    // Bitmap作成
                    AndroidJavaObject bitmap = new AndroidJavaObject(
                        "android.graphics.Bitmap",
                        "createBitmap",
                        textureWidth,
                        textureHeight,
                        new AndroidJavaClass("android.graphics.Bitmap$Config").GetStatic<AndroidJavaObject>("ARGB_8888")
                    );
                    
                    // Canvas作成してWebViewを描画
                    AndroidJavaObject canvas = new AndroidJavaObject("android.graphics.Canvas", bitmap);
                    webView.Call("draw", canvas);
                    
                    // BitmapのピクセルデータをUnityに転送
                    int[] pixels = new int[textureWidth * textureHeight];
                    bitmap.Call("getPixels", pixels, 0, textureWidth, 0, 0, textureWidth, textureHeight);
                    
                    // UnityメインスレッドでTexture2Dを更新
                    UnityMainThreadDispatcher.Enqueue(() =>
                    {
                        Color32[] colors = new Color32[textureWidth * textureHeight];
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            int pixel = pixels[i];
                            colors[i] = new Color32(
                                (byte)((pixel >> 16) & 0xFF), // R
                                (byte)((pixel >> 8) & 0xFF),  // G
                                (byte)(pixel & 0xFF),         // B
                                (byte)((pixel >> 24) & 0xFF)  // A
                            );
                        }
                        captureTexture.SetPixels32(colors);
                        captureTexture.Apply();
                        
                        // RenderTextureに転送
                        Graphics.Blit(captureTexture, renderTexture);
                    });
                    
                    bitmap.Call("recycle");
                }));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ArsistWebViewManager] Capture failed: {e.Message}");
            }
        }

        /// <summary>
        /// HTMLコンテンツを文字列としてロード
        /// </summary>
        public void LoadHTMLString(string htmlContent)
        {
            if (!isInitialized || webView == null)
            {
                Debug.LogError("[ArsistWebViewManager] WebView not initialized");
                return;
            }

            currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                webView.Call("loadDataWithBaseURL", "file:///android_asset/", htmlContent, "text/html", "UTF-8", null);
            }));

            Debug.Log("[ArsistWebViewManager] HTML content loaded");
        }

        /// <summary>
        /// URLからHTMLをロード
        /// </summary>
        public void LoadURL(string url)
        {
            if (!isInitialized || webView == null)
            {
                Debug.LogError("[ArsistWebViewManager] WebView not initialized");
                return;
            }

            currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                webView.Call("loadUrl", url);
            }));

            Debug.Log($"[ArsistWebViewManager] URL loaded: {url}");
        }
        
        /// <summary>
        /// RenderTextureを取得（3Dオブジェクトへの適用用）
        /// </summary>
        public RenderTexture GetRenderTexture()
        {
            return renderTexture;
        }

        private void OnDestroy()
        {
            isInitialized = false;
            
            if (webView != null)
            {
                currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    webView.Call("destroy");
                }));
            }
            
            if (captureTexture != null)
            {
                Destroy(captureTexture);
            }
            
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }
#else
        // Editor / Non-Android
        private RenderTexture renderTexture;
        
        public void Initialize(RectTransform rect)
        {
            // Editor用のダミーRenderTexture
            renderTexture = new RenderTexture(1920, 1080, 0);
            renderTexture.Create();
            
            GameObject imageObj = new GameObject("WebViewDisplay_Editor");
            imageObj.transform.SetParent(rect, false);
            
            var rawImage = imageObj.AddComponent<RawImage>();
            rawImage.texture = renderTexture;
            rawImage.color = new Color(0.2f, 0.2f, 0.2f, 1f); // グレー表示
            
            RectTransform imageRect = imageObj.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.sizeDelta = Vector2.zero;
            imageRect.anchoredPosition = Vector2.zero;
            
            // テキスト追加
            GameObject textObj = new GameObject("EditorWarning");
            textObj.transform.SetParent(imageObj.transform, false);
            var text = textObj.AddComponent<Text>();
            text.text = "WebView (Android Only)\nEditor Preview";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 32;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            
            Debug.LogWarning("[ArsistWebViewManager] Editor mode - WebView preview only");
        }

        public void LoadHTMLString(string htmlContent)
        {
            Debug.LogWarning("[ArsistWebViewManager] WebView only available on Android");
        }

        public void LoadURL(string url)
        {
            Debug.LogWarning("[ArsistWebViewManager] WebView only available on Android");
        }
        
        public RenderTexture GetRenderTexture()
        {
            return renderTexture;
        }
#endif
    }
    
    /// <summary>
    /// Androidスレッドからメインスレッドへのアクション実行用ヘルパー
    /// </summary>
    public static class UnityMainThreadDispatcher
    {
        private static readonly System.Collections.Generic.Queue<System.Action> _actions = new System.Collections.Generic.Queue<System.Action>();
        
        public static void Enqueue(System.Action action)
        {
            lock (_actions)
            {
                _actions.Enqueue(action);
            }
        }
        
        [RuntimeInitializeOnLoadMethod]
        private static void Initialize()
        {
            var go = new GameObject("MainThreadDispatcher");
            go.AddComponent<MainThreadDispatcherBehaviour>();
            Object.DontDestroyOnLoad(go);
        }
        
        private class MainThreadDispatcherBehaviour : MonoBehaviour
        {
            private void Update()
            {
                lock (_actions)
                {
                    while (_actions.Count > 0)
                    {
                        _actions.Dequeue()?.Invoke();
                    }
                }
            }
        }
    }
}
