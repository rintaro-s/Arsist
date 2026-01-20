using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;

namespace Arsist.Runtime.Events
{
    /// <summary>
    /// UIボタンにアタッチして、クリック時にイベントを発火する
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ArsistButtonEvent : MonoBehaviour
    {
        [Header("Event Settings")]
        [Tooltip("発火するイベント名")]
        [SerializeField] private string eventName = "OnButtonClick";
        
        [Tooltip("ペイロードに含めるID（オプション）")]
        [SerializeField] private string buttonId = "";
        
        [Tooltip("追加のペイロードデータ（JSON形式）")]
        [SerializeField] [TextArea] private string extraPayloadJson = "";

        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            if (ArsistEventBus.Instance == null)
            {
                Debug.LogWarning("[ArsistButtonEvent] EventBus not found");
                return;
            }

            var payload = new JObject
            {
                ["buttonName"] = gameObject.name,
                ["buttonId"] = string.IsNullOrEmpty(buttonId) ? gameObject.name : buttonId
            };

            // 追加ペイロードをマージ
            if (!string.IsNullOrEmpty(extraPayloadJson))
            {
                try
                {
                    var extra = JObject.Parse(extraPayloadJson);
                    foreach (var prop in extra.Properties())
                    {
                        payload[prop.Name] = prop.Value;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[ArsistButtonEvent] Invalid extra payload JSON: {e.Message}");
                }
            }

            ArsistEventBus.Instance.Publish(eventName, payload);
        }

        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClick);
            }
        }
    }
}
