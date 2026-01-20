using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Arsist.Runtime.Events
{
    /// <summary>
    /// イベントを受信して3Dオブジェクトを操作するコンポーネント
    /// 例: ボタンクリック → オブジェクト表示/非表示、移動、回転など
    /// </summary>
    public class ArsistEventReceiver : MonoBehaviour
    {
        [Header("Event Settings")]
        [Tooltip("購読するイベント名")]
        [SerializeField] private string listenEventName = "OnButtonClick";
        
        [Tooltip("フィルタ条件（ペイロードのキー）")]
        [SerializeField] private string filterKey = "buttonId";
        
        [Tooltip("フィルタ値（空なら全て受信）")]
        [SerializeField] private string filterValue = "";

        [Header("Actions")]
        [Tooltip("アクションタイプ")]
        [SerializeField] private ActionType actionType = ActionType.ToggleActive;
        
        [Tooltip("対象オブジェクト（空なら自分自身）")]
        [SerializeField] private GameObject targetObject;

        [Header("Transform Actions")]
        [SerializeField] private Vector3 moveOffset = Vector3.zero;
        [SerializeField] private Vector3 rotateOffset = Vector3.zero;
        [SerializeField] private Vector3 scaleMultiplier = Vector3.one;
        [SerializeField] private float animationDuration = 0.3f;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent onEventReceived;

        private enum ActionType
        {
            ToggleActive,
            SetActive,
            SetInactive,
            Move,
            Rotate,
            Scale,
            Destroy,
            PlayAnimation,
            PlaySound,
            CustomEvent
        }

        private void OnEnable()
        {
            if (ArsistEventBus.Instance != null)
            {
                ArsistEventBus.Instance.Subscribe(listenEventName, HandleEvent);
            }
        }

        private void OnDisable()
        {
            if (ArsistEventBus.Instance != null)
            {
                ArsistEventBus.Instance.Unsubscribe(listenEventName, HandleEvent);
            }
        }

        private void HandleEvent(JObject payload)
        {
            // フィルタチェック
            if (!string.IsNullOrEmpty(filterKey) && !string.IsNullOrEmpty(filterValue))
            {
                var payloadValue = payload[filterKey]?.ToString();
                if (payloadValue != filterValue)
                {
                    return; // フィルタ不一致
                }
            }

            var target = targetObject != null ? targetObject : gameObject;

            switch (actionType)
            {
                case ActionType.ToggleActive:
                    target.SetActive(!target.activeSelf);
                    break;
                    
                case ActionType.SetActive:
                    target.SetActive(true);
                    break;
                    
                case ActionType.SetInactive:
                    target.SetActive(false);
                    break;
                    
                case ActionType.Move:
                    StartCoroutine(AnimatePosition(target.transform, target.transform.position + moveOffset, animationDuration));
                    break;
                    
                case ActionType.Rotate:
                    StartCoroutine(AnimateRotation(target.transform, target.transform.eulerAngles + rotateOffset, animationDuration));
                    break;
                    
                case ActionType.Scale:
                    var newScale = new Vector3(
                        target.transform.localScale.x * scaleMultiplier.x,
                        target.transform.localScale.y * scaleMultiplier.y,
                        target.transform.localScale.z * scaleMultiplier.z
                    );
                    StartCoroutine(AnimateScale(target.transform, newScale, animationDuration));
                    break;
                    
                case ActionType.Destroy:
                    Destroy(target);
                    break;
                    
                case ActionType.PlayAnimation:
                    var animator = target.GetComponent<Animator>();
                    if (animator != null)
                    {
                        var triggerName = payload["trigger"]?.ToString() ?? "Play";
                        animator.SetTrigger(triggerName);
                    }
                    break;
                    
                case ActionType.PlaySound:
                    var audioSource = target.GetComponent<AudioSource>();
                    if (audioSource != null)
                    {
                        audioSource.Play();
                    }
                    break;
                    
                case ActionType.CustomEvent:
                    // UnityEventを発火
                    break;
            }

            onEventReceived?.Invoke();
        }

        private System.Collections.IEnumerator AnimatePosition(Transform t, Vector3 targetPos, float duration)
        {
            var startPos = t.position;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                t.position = Vector3.Lerp(startPos, targetPos, elapsed / duration);
                yield return null;
            }
            t.position = targetPos;
        }

        private System.Collections.IEnumerator AnimateRotation(Transform t, Vector3 targetRot, float duration)
        {
            var startRot = t.eulerAngles;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                t.eulerAngles = Vector3.Lerp(startRot, targetRot, elapsed / duration);
                yield return null;
            }
            t.eulerAngles = targetRot;
        }

        private System.Collections.IEnumerator AnimateScale(Transform t, Vector3 targetScale, float duration)
        {
            var startScale = t.localScale;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                t.localScale = Vector3.Lerp(startScale, targetScale, elapsed / duration);
                yield return null;
            }
            t.localScale = targetScale;
        }
    }
}
