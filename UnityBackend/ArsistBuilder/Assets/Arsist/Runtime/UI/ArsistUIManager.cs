using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Arsist.Runtime.UI
{
    /// <summary>
    /// ダイアログ、トースト、ローディングなど汎用UIを表示
    /// </summary>
    public class ArsistUIManager : MonoBehaviour
    {
        public static ArsistUIManager Instance { get; private set; }

        [Header("Prefab References (Optional)")]
        [SerializeField] private GameObject dialogPrefab;
        [SerializeField] private GameObject toastPrefab;
        [SerializeField] private GameObject loadingPrefab;

        private Canvas _canvas;
        private GameObject _currentDialog;
        private GameObject _currentLoading;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureCanvas();
        }

        private void EnsureCanvas()
        {
            var canvasGO = new GameObject("ArsistUICanvas");
            canvasGO.transform.SetParent(transform);
            
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 9999; // 最前面
            
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        #region Dialog

        /// <summary>
        /// OKダイアログを表示
        /// </summary>
        public void ShowDialog(string title, string message, Action onOk = null)
        {
            ShowDialog(title, message, "OK", null, onOk, null);
        }

        /// <summary>
        /// 確認ダイアログを表示
        /// </summary>
        public void ShowConfirmDialog(string title, string message, Action onYes, Action onNo = null)
        {
            ShowDialog(title, message, "はい", "いいえ", onYes, onNo);
        }

        /// <summary>
        /// カスタムダイアログを表示
        /// </summary>
        public void ShowDialog(string title, string message, string okText, string cancelText, Action onOk, Action onCancel)
        {
            if (_currentDialog != null)
            {
                Destroy(_currentDialog);
            }

            // ダイアログをコードで生成
            _currentDialog = CreateDialogUI(title, message, okText, cancelText, onOk, onCancel);
        }

        private GameObject CreateDialogUI(string title, string message, string okText, string cancelText, Action onOk, Action onCancel)
        {
            // 背景
            var dialogRoot = new GameObject("Dialog");
            dialogRoot.transform.SetParent(_canvas.transform, false);
            var rootRect = dialogRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var bgImage = dialogRoot.AddComponent<Image>();
            bgImage.color = new Color(0, 0, 0, 0.5f);

            // パネル
            var panel = new GameObject("Panel");
            panel.transform.SetParent(dialogRoot.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(400, 250);
            
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.15f, 1f);

            // タイトル
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panel.transform, false);
            var titleRect = titleGO.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -20);
            titleRect.sizeDelta = new Vector2(-40, 40);
            
            var titleText = titleGO.AddComponent<TextMeshProUGUI>();
            titleText.text = title;
            titleText.fontSize = 24;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;

            // メッセージ
            var messageGO = new GameObject("Message");
            messageGO.transform.SetParent(panel.transform, false);
            var messageRect = messageGO.AddComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0, 0.3f);
            messageRect.anchorMax = new Vector2(1, 0.8f);
            messageRect.offsetMin = new Vector2(20, 0);
            messageRect.offsetMax = new Vector2(-20, 0);
            
            var messageText = messageGO.AddComponent<TextMeshProUGUI>();
            messageText.text = message;
            messageText.fontSize = 18;
            messageText.alignment = TextAlignmentOptions.Center;
            messageText.color = Color.white;

            // ボタン群
            var buttonsGO = new GameObject("Buttons");
            buttonsGO.transform.SetParent(panel.transform, false);
            var buttonsRect = buttonsGO.AddComponent<RectTransform>();
            buttonsRect.anchorMin = new Vector2(0, 0);
            buttonsRect.anchorMax = new Vector2(1, 0.3f);
            buttonsRect.offsetMin = new Vector2(20, 20);
            buttonsRect.offsetMax = new Vector2(-20, 0);
            
            var hlg = buttonsGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = true;

            // OKボタン
            if (!string.IsNullOrEmpty(okText))
            {
                var okBtn = CreateButton(okText, new Color(0.91f, 0.27f, 0.38f, 1f));
                okBtn.transform.SetParent(buttonsGO.transform, false);
                okBtn.GetComponent<Button>().onClick.AddListener(() =>
                {
                    onOk?.Invoke();
                    Destroy(dialogRoot);
                    _currentDialog = null;
                });
            }

            // Cancelボタン
            if (!string.IsNullOrEmpty(cancelText))
            {
                var cancelBtn = CreateButton(cancelText, new Color(0.3f, 0.3f, 0.35f, 1f));
                cancelBtn.transform.SetParent(buttonsGO.transform, false);
                cancelBtn.GetComponent<Button>().onClick.AddListener(() =>
                {
                    onCancel?.Invoke();
                    Destroy(dialogRoot);
                    _currentDialog = null;
                });
            }

            return dialogRoot;
        }

        private GameObject CreateButton(string text, Color color)
        {
            var btnGO = new GameObject("Button");
            var btnRect = btnGO.AddComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(120, 40);

            var btnImage = btnGO.AddComponent<Image>();
            btnImage.color = color;

            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImage;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmpText = textGO.AddComponent<TextMeshProUGUI>();
            tmpText.text = text;
            tmpText.fontSize = 16;
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.color = Color.white;

            return btnGO;
        }

        public void HideDialog()
        {
            if (_currentDialog != null)
            {
                Destroy(_currentDialog);
                _currentDialog = null;
            }
        }

        #endregion

        #region Toast

        /// <summary>
        /// トーストメッセージを表示
        /// </summary>
        public void ShowToast(string message, float duration = 2f)
        {
            StartCoroutine(ShowToastCoroutine(message, duration));
        }

        private IEnumerator ShowToastCoroutine(string message, float duration)
        {
            var toastGO = new GameObject("Toast");
            toastGO.transform.SetParent(_canvas.transform, false);
            var toastRect = toastGO.AddComponent<RectTransform>();
            toastRect.anchorMin = new Vector2(0.5f, 0);
            toastRect.anchorMax = new Vector2(0.5f, 0);
            toastRect.pivot = new Vector2(0.5f, 0);
            toastRect.anchoredPosition = new Vector2(0, 100);
            toastRect.sizeDelta = new Vector2(400, 60);

            var bg = toastGO.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(toastGO.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 10);
            textRect.offsetMax = new Vector2(-10, -10);

            var tmpText = textGO.AddComponent<TextMeshProUGUI>();
            tmpText.text = message;
            tmpText.fontSize = 16;
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.color = Color.white;

            // フェードイン
            var canvasGroup = toastGO.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0;
            
            float t = 0;
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = t / 0.3f;
                yield return null;
            }
            canvasGroup.alpha = 1;

            yield return new WaitForSeconds(duration);

            // フェードアウト
            t = 0;
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = 1 - (t / 0.3f);
                yield return null;
            }

            Destroy(toastGO);
        }

        #endregion

        #region Loading

        /// <summary>
        /// ローディングインジケータを表示
        /// </summary>
        public void ShowLoading(string message = "Loading...")
        {
            if (_currentLoading != null) return;

            _currentLoading = new GameObject("Loading");
            _currentLoading.transform.SetParent(_canvas.transform, false);
            var loadingRect = _currentLoading.AddComponent<RectTransform>();
            loadingRect.anchorMin = Vector2.zero;
            loadingRect.anchorMax = Vector2.one;
            loadingRect.offsetMin = Vector2.zero;
            loadingRect.offsetMax = Vector2.zero;

            var bg = _currentLoading.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.7f);

            // スピナー（回転する円）
            var spinnerGO = new GameObject("Spinner");
            spinnerGO.transform.SetParent(_currentLoading.transform, false);
            var spinnerRect = spinnerGO.AddComponent<RectTransform>();
            spinnerRect.sizeDelta = new Vector2(64, 64);
            spinnerRect.anchoredPosition = new Vector2(0, 30);

            var spinnerImage = spinnerGO.AddComponent<Image>();
            spinnerImage.color = new Color(0.91f, 0.27f, 0.38f, 1f);

            var spinner = spinnerGO.AddComponent<LoadingSpinner>();

            // メッセージ
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(_currentLoading.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchoredPosition = new Vector2(0, -50);
            textRect.sizeDelta = new Vector2(300, 40);

            var tmpText = textGO.AddComponent<TextMeshProUGUI>();
            tmpText.text = message;
            tmpText.fontSize = 18;
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.color = Color.white;
        }

        /// <summary>
        /// ローディングインジケータを非表示
        /// </summary>
        public void HideLoading()
        {
            if (_currentLoading != null)
            {
                Destroy(_currentLoading);
                _currentLoading = null;
            }
        }

        #endregion
    }

    /// <summary>
    /// ローディングスピナーの回転
    /// </summary>
    public class LoadingSpinner : MonoBehaviour
    {
        public float rotationSpeed = 360f;

        private void Update()
        {
            transform.Rotate(0, 0, -rotationSpeed * Time.deltaTime);
        }
    }
}
