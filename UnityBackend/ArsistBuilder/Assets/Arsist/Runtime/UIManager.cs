// ==============================================
// Arsist Engine - UI Manager
// UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/UIManager.cs
// ==============================================

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;

namespace Arsist.Runtime
{
    /// <summary>
    /// ArsistアプリのUI管理コンポーネント
    /// World Space UIの表示・非表示・アニメーションを制御
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("UI Panels")]
        [SerializeField] private List<UIPanel> _panels = new List<UIPanel>();
        
        [Header("Animation Settings")]
        [SerializeField] private float _fadeInDuration = 0.3f;
        [SerializeField] private float _fadeOutDuration = 0.2f;
        [SerializeField] private Ease _showEase = Ease.OutQuad;
        [SerializeField] private Ease _hideEase = Ease.InQuad;
        
        [Header("Positioning")]
        [SerializeField] private Transform _headFollowTarget;
        [SerializeField] private float _followDistance = 2f;
        [SerializeField] private float _followSmoothness = 5f;
        [SerializeField] private bool _headLocked = false;
        
        private Dictionary<string, UIPanel> _panelMap = new Dictionary<string, UIPanel>();
        private Canvas _activeCanvas;

        private void Awake()
        {
            // パネルマップを構築
            foreach (var panel in _panels)
            {
                if (!string.IsNullOrEmpty(panel.PanelId))
                {
                    _panelMap[panel.PanelId] = panel;
                }
            }
            
            // 自動検出
            if (_panels.Count == 0)
            {
                AutoDetectPanels();
            }
        }

        private void Start()
        {
            // メインカメラをフォローターゲットに
            if (_headFollowTarget == null)
            {
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    _headFollowTarget = mainCam.transform;
                }
            }
        }

        private void Update()
        {
            if (_headLocked && _headFollowTarget != null)
            {
                UpdateHeadLockedUI();
            }
        }

        private void AutoDetectPanels()
        {
            var canvases = FindObjectsOfType<Canvas>();
            foreach (var canvas in canvases)
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    var panel = canvas.GetComponent<UIPanel>();
                    if (panel == null)
                    {
                        panel = canvas.gameObject.AddComponent<UIPanel>();
                        panel.AutoSetup();
                    }
                    _panels.Add(panel);
                    
                    if (!string.IsNullOrEmpty(panel.PanelId))
                    {
                        _panelMap[panel.PanelId] = panel;
                    }
                }
            }
        }

        private void UpdateHeadLockedUI()
        {
            foreach (var panel in _panels)
            {
                if (panel.IsHeadLocked && panel.IsVisible)
                {
                    // 頭に追従
                    var targetPosition = _headFollowTarget.position + _headFollowTarget.forward * _followDistance;
                    panel.transform.position = Vector3.Lerp(
                        panel.transform.position, 
                        targetPosition, 
                        Time.deltaTime * _followSmoothness
                    );
                    
                    // カメラに向ける
                    panel.transform.rotation = Quaternion.Lerp(
                        panel.transform.rotation,
                        Quaternion.LookRotation(panel.transform.position - _headFollowTarget.position),
                        Time.deltaTime * _followSmoothness
                    );
                }
            }
        }

        /// <summary>
        /// パネルを表示
        /// </summary>
        public void ShowPanel(string panelId)
        {
            if (_panelMap.TryGetValue(panelId, out UIPanel panel))
            {
                panel.Show(_fadeInDuration, _showEase);
            }
            else
            {
                Debug.LogWarning($"[Arsist UI] Panel not found: {panelId}");
            }
        }

        /// <summary>
        /// パネルを非表示
        /// </summary>
        public void HidePanel(string panelId)
        {
            if (_panelMap.TryGetValue(panelId, out UIPanel panel))
            {
                panel.Hide(_fadeOutDuration, _hideEase);
            }
        }

        /// <summary>
        /// パネルの表示/非表示を切り替え
        /// </summary>
        public void TogglePanel(string panelId)
        {
            if (_panelMap.TryGetValue(panelId, out UIPanel panel))
            {
                if (panel.IsVisible)
                    panel.Hide(_fadeOutDuration, _hideEase);
                else
                    panel.Show(_fadeInDuration, _showEase);
            }
        }

        /// <summary>
        /// 全パネルを非表示
        /// </summary>
        public void HideAllPanels()
        {
            foreach (var panel in _panels)
            {
                panel.Hide(_fadeOutDuration, _hideEase);
            }
        }

        /// <summary>
        /// パネルを指定位置に配置
        /// </summary>
        public void PositionPanel(string panelId, Vector3 worldPosition, Quaternion rotation)
        {
            if (_panelMap.TryGetValue(panelId, out UIPanel panel))
            {
                panel.transform.position = worldPosition;
                panel.transform.rotation = rotation;
            }
        }

        /// <summary>
        /// パネルをカメラ前方に配置
        /// </summary>
        public void PositionPanelInFront(string panelId, float distance = 2f)
        {
            if (_panelMap.TryGetValue(panelId, out UIPanel panel) && _headFollowTarget != null)
            {
                var pos = _headFollowTarget.position + _headFollowTarget.forward * distance;
                panel.transform.position = pos;
                panel.transform.LookAt(_headFollowTarget);
                panel.transform.Rotate(0, 180, 0); // 正面を向ける
            }
        }

        /// <summary>
        /// パネルを取得
        /// </summary>
        public UIPanel GetPanel(string panelId)
        {
            return _panelMap.TryGetValue(panelId, out UIPanel panel) ? panel : null;
        }

        /// <summary>
        /// 新しいパネルを登録
        /// </summary>
        public void RegisterPanel(UIPanel panel)
        {
            if (!_panels.Contains(panel))
            {
                _panels.Add(panel);
                if (!string.IsNullOrEmpty(panel.PanelId))
                {
                    _panelMap[panel.PanelId] = panel;
                }
            }
        }
    }

    /// <summary>
    /// 個別のUIパネルコンポーネント
    /// </summary>
    public class UIPanel : MonoBehaviour
    {
        [SerializeField] private string _panelId;
        [SerializeField] private bool _isHeadLocked = false;
        [SerializeField] private bool _startVisible = false;
        
        private CanvasGroup _canvasGroup;
        private Canvas _canvas;
        private bool _isVisible;

        public string PanelId => _panelId;
        public bool IsHeadLocked => _isHeadLocked;
        public bool IsVisible => _isVisible;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvasGroup = GetComponent<CanvasGroup>();
            
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            
            _isVisible = _startVisible;
            _canvasGroup.alpha = _startVisible ? 1f : 0f;
            _canvasGroup.interactable = _startVisible;
            _canvasGroup.blocksRaycasts = _startVisible;
        }

        public void AutoSetup()
        {
            _panelId = gameObject.name;
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        public void Show(float duration = 0.3f, Ease ease = Ease.OutQuad)
        {
            _isVisible = true;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            
            _canvasGroup.DOKill();
            _canvasGroup.DOFade(1f, duration).SetEase(ease);
            
            // スケールアニメーション
            transform.localScale = Vector3.one * 0.9f;
            transform.DOScale(Vector3.one, duration).SetEase(ease);
        }

        public void Hide(float duration = 0.2f, Ease ease = Ease.InQuad)
        {
            _isVisible = false;
            
            _canvasGroup.DOKill();
            _canvasGroup.DOFade(0f, duration).SetEase(ease).OnComplete(() =>
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            });
            
            transform.DOScale(Vector3.one * 0.9f, duration).SetEase(ease);
        }

        public void SetHeadLocked(bool locked)
        {
            _isHeadLocked = locked;
        }
    }
}
