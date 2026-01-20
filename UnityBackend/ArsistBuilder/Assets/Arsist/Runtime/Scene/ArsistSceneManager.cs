using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arsist.Runtime.Scene
{
    /// <summary>
    /// シーン遷移管理
    /// フェード付きシーン切り替え、ローディング画面対応
    /// </summary>
    public class ArsistSceneManager : MonoBehaviour
    {
        public static ArsistSceneManager Instance { get; private set; }

        [Header("Fade Settings")]
        [SerializeField] private float defaultFadeDuration = 0.5f;
        [SerializeField] private Color fadeColor = Color.black;
        [SerializeField] private Canvas fadeCanvas;
        [SerializeField] private CanvasGroup fadeCanvasGroup;

        [Header("Loading Screen")]
        [SerializeField] private GameObject loadingScreenPrefab;

        public bool IsTransitioning { get; private set; }
        public float LoadingProgress { get; private set; }

        public event Action<string> OnSceneLoadStart;
        public event Action<string> OnSceneLoadComplete;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SetupFadeCanvas();
        }

        private void SetupFadeCanvas()
        {
            if (fadeCanvas == null)
            {
                var canvasGO = new GameObject("FadeCanvas");
                canvasGO.transform.SetParent(transform);

                fadeCanvas = canvasGO.AddComponent<Canvas>();
                fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                fadeCanvas.sortingOrder = 9999;

                fadeCanvasGroup = canvasGO.AddComponent<CanvasGroup>();
                fadeCanvasGroup.alpha = 0;
                fadeCanvasGroup.blocksRaycasts = false;

                var image = canvasGO.AddComponent<UnityEngine.UI.Image>();
                image.color = fadeColor;
                image.raycastTarget = false;

                var rectTransform = canvasGO.GetComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
            }
        }

        #region Public API

        /// <summary>
        /// シーンを読み込む（フェード付き）
        /// </summary>
        public void LoadScene(string sceneName, float fadeDuration = -1)
        {
            if (IsTransitioning) return;
            StartCoroutine(LoadSceneRoutine(sceneName, fadeDuration < 0 ? defaultFadeDuration : fadeDuration, false));
        }

        /// <summary>
        /// シーンを読み込む（インデックス指定）
        /// </summary>
        public void LoadScene(int sceneIndex, float fadeDuration = -1)
        {
            if (IsTransitioning) return;
            var sceneName = SceneManager.GetSceneByBuildIndex(sceneIndex).name;
            StartCoroutine(LoadSceneRoutine(sceneName, fadeDuration < 0 ? defaultFadeDuration : fadeDuration, false));
        }

        /// <summary>
        /// シーンを非同期読み込み（ローディング画面付き）
        /// </summary>
        public void LoadSceneAsync(string sceneName, float fadeDuration = -1)
        {
            if (IsTransitioning) return;
            StartCoroutine(LoadSceneRoutine(sceneName, fadeDuration < 0 ? defaultFadeDuration : fadeDuration, true));
        }

        /// <summary>
        /// 現在のシーンをリロード
        /// </summary>
        public void ReloadScene(float fadeDuration = -1)
        {
            var currentScene = SceneManager.GetActiveScene().name;
            LoadScene(currentScene, fadeDuration);
        }

        /// <summary>
        /// アプリを終了
        /// </summary>
        public void QuitApplication(float fadeDuration = -1)
        {
            StartCoroutine(QuitRoutine(fadeDuration < 0 ? defaultFadeDuration : fadeDuration));
        }

        #endregion

        #region Coroutines

        private IEnumerator LoadSceneRoutine(string sceneName, float fadeDuration, bool async)
        {
            IsTransitioning = true;
            OnSceneLoadStart?.Invoke(sceneName);

            // フェードアウト
            yield return StartCoroutine(Fade(1, fadeDuration));
            fadeCanvasGroup.blocksRaycasts = true;

            // ローディング画面表示
            GameObject loadingScreen = null;
            if (async && loadingScreenPrefab != null)
            {
                loadingScreen = Instantiate(loadingScreenPrefab, fadeCanvas.transform);
            }

            // シーン読み込み
            if (async)
            {
                var operation = SceneManager.LoadSceneAsync(sceneName);
                operation.allowSceneActivation = false;

                while (operation.progress < 0.9f)
                {
                    LoadingProgress = operation.progress / 0.9f;
                    yield return null;
                }

                LoadingProgress = 1f;
                yield return new WaitForSeconds(0.2f); // 一瞬見せる

                operation.allowSceneActivation = true;
                yield return operation;
            }
            else
            {
                SceneManager.LoadScene(sceneName);
                yield return null;
            }

            // ローディング画面非表示
            if (loadingScreen != null)
            {
                Destroy(loadingScreen);
            }

            // フェードイン
            yield return StartCoroutine(Fade(0, fadeDuration));
            fadeCanvasGroup.blocksRaycasts = false;

            IsTransitioning = false;
            LoadingProgress = 0;
            OnSceneLoadComplete?.Invoke(sceneName);
        }

        private IEnumerator Fade(float targetAlpha, float duration)
        {
            float startAlpha = fadeCanvasGroup.alpha;
            float elapsed = 0;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
                yield return null;
            }

            fadeCanvasGroup.alpha = targetAlpha;
        }

        private IEnumerator QuitRoutine(float fadeDuration)
        {
            yield return StartCoroutine(Fade(1, fadeDuration));

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        #endregion

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
