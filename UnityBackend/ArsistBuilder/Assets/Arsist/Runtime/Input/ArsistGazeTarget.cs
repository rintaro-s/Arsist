using UnityEngine;

namespace Arsist.Runtime.Input
{
    /// <summary>
    /// 視線インタラクション可能なオブジェクトに付けるコンポーネント
    /// OnGazeEnter/OnGazeExit/OnGazeDwellSelectを受け取る
    /// </summary>
    public class ArsistGazeTarget : MonoBehaviour
    {
        [Header("Visual Feedback")]
        [Tooltip("視線が当たった時のハイライト色")]
        [SerializeField] private Color highlightColor = new Color(0.3f, 0.6f, 1f, 0.5f);
        
        [Tooltip("選択時のスケール倍率")]
        [SerializeField] private float hoverScaleMultiplier = 1.05f;

        [Header("Events")]
        [Tooltip("視線が入った時に呼ばれるUnityEvent")]
        public UnityEngine.Events.UnityEvent onGazeEnter;
        
        [Tooltip("視線が出た時に呼ばれるUnityEvent")]
        public UnityEngine.Events.UnityEvent onGazeExit;
        
        [Tooltip("Dwell選択時に呼ばれるUnityEvent")]
        public UnityEngine.Events.UnityEvent onGazeSelect;

        private Renderer _renderer;
        private Color _originalColor;
        private Vector3 _originalScale;
        private bool _isGazed;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer != null && _renderer.material != null)
            {
                _originalColor = _renderer.material.color;
            }
            _originalScale = transform.localScale;
        }

        // ArsistGazeInputからSendMessageで呼ばれる
        public void OnGazeEnter(Vector3 hitPoint)
        {
            _isGazed = true;
            
            // ビジュアルフィードバック
            if (_renderer != null && _renderer.material != null)
            {
                _renderer.material.color = Color.Lerp(_originalColor, highlightColor, 0.5f);
            }
            transform.localScale = _originalScale * hoverScaleMultiplier;
            
            onGazeEnter?.Invoke();
        }

        public void OnGazeExit()
        {
            _isGazed = false;
            
            // 元に戻す
            if (_renderer != null && _renderer.material != null)
            {
                _renderer.material.color = _originalColor;
            }
            transform.localScale = _originalScale;
            
            onGazeExit?.Invoke();
        }

        public void OnGazeDwellSelect(Vector3 hitPoint)
        {
            onGazeSelect?.Invoke();
        }
    }
}
