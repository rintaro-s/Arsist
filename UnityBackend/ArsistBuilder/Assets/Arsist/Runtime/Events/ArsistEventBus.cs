using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Arsist.Runtime.Events
{
    /// <summary>
    /// Arsistアプリ内のイベントバス
    /// UI→3D、3D→UI、外部入力→アプリ間の疎結合なイベント通信を実現
    /// </summary>
    public class ArsistEventBus : MonoBehaviour
    {
        public static ArsistEventBus Instance { get; private set; }

        // イベント購読者の辞書
        private readonly Dictionary<string, List<Action<JObject>>> _subscribers = new Dictionary<string, List<Action<JObject>>>();
        
        // イベントログ（デバッグ用）
        private readonly List<EventLogEntry> _eventLog = new List<EventLogEntry>();
        private const int MaxLogEntries = 100;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// イベントを購読する
        /// </summary>
        public void Subscribe(string eventName, Action<JObject> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null) return;

            if (!_subscribers.ContainsKey(eventName))
            {
                _subscribers[eventName] = new List<Action<JObject>>();
            }
            _subscribers[eventName].Add(handler);
        }

        /// <summary>
        /// イベント購読を解除する
        /// </summary>
        public void Unsubscribe(string eventName, Action<JObject> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null) return;

            if (_subscribers.ContainsKey(eventName))
            {
                _subscribers[eventName].Remove(handler);
            }
        }

        /// <summary>
        /// イベントを発火する
        /// </summary>
        public void Publish(string eventName, JObject payload = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            payload = payload ?? new JObject();

            if (logEvents)
            {
                LogEvent(eventName, payload);
            }

            if (_subscribers.ContainsKey(eventName))
            {
                // コピーを作ってイテレート（購読者が購読解除する可能性があるため）
                var handlers = new List<Action<JObject>>(_subscribers[eventName]);
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler(payload);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ArsistEventBus] Error in handler for '{eventName}': {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 便利メソッド: 文字列ペイロードでイベント発火
        /// </summary>
        public void Publish(string eventName, string key, string value)
        {
            Publish(eventName, new JObject { [key] = value });
        }

        /// <summary>
        /// 便利メソッド: 数値ペイロードでイベント発火
        /// </summary>
        public void Publish(string eventName, string key, float value)
        {
            Publish(eventName, new JObject { [key] = value });
        }

        private void LogEvent(string eventName, JObject payload)
        {
            var entry = new EventLogEntry
            {
                timestamp = DateTime.Now,
                eventName = eventName,
                payload = payload.ToString(Newtonsoft.Json.Formatting.None)
            };
            _eventLog.Add(entry);

            if (_eventLog.Count > MaxLogEntries)
            {
                _eventLog.RemoveAt(0);
            }

            Debug.Log($"[ArsistEventBus] {eventName}: {entry.payload}");
        }

        /// <summary>
        /// イベントログを取得（デバッグUI用）
        /// </summary>
        public IReadOnlyList<EventLogEntry> GetEventLog() => _eventLog;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [Serializable]
        public struct EventLogEntry
        {
            public DateTime timestamp;
            public string eventName;
            public string payload;
        }
    }
}
