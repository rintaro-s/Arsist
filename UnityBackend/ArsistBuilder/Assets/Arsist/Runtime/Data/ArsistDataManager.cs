using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

namespace Arsist.Runtime.Data
{
    /// <summary>
    /// データ永続化マネージャー
    /// PlayerPrefsよりも柔軟なJSON形式でのデータ保存
    /// </summary>
    public class ArsistDataManager : MonoBehaviour
    {
        public static ArsistDataManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private string saveFileName = "arsist_save.json";
        [SerializeField] private bool autoSave = true;
        [SerializeField] private float autoSaveInterval = 60f;

        private Dictionary<string, object> _data = new Dictionary<string, object>();
        private float _lastSaveTime;
        private string SaveFilePath => Path.Combine(Application.persistentDataPath, saveFileName);

        public event Action OnDataLoaded;
        public event Action OnDataSaved;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Load();
        }

        private void Update()
        {
            if (autoSave && Time.time - _lastSaveTime > autoSaveInterval)
            {
                Save();
                _lastSaveTime = Time.time;
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) Save();
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        #region Public API

        /// <summary>
        /// 値を設定
        /// </summary>
        public void Set<T>(string key, T value)
        {
            _data[key] = value;
        }

        /// <summary>
        /// 値を取得
        /// </summary>
        public T Get<T>(string key, T defaultValue = default)
        {
            if (_data.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is T typedValue)
                    {
                        return typedValue;
                    }
                    // JSONからの復元時は型が違う場合があるので変換を試みる
                    return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// キーが存在するか
        /// </summary>
        public bool HasKey(string key) => _data.ContainsKey(key);

        /// <summary>
        /// キーを削除
        /// </summary>
        public void DeleteKey(string key)
        {
            _data.Remove(key);
        }

        /// <summary>
        /// すべてのデータを削除
        /// </summary>
        public void DeleteAll()
        {
            _data.Clear();
            if (File.Exists(SaveFilePath))
            {
                File.Delete(SaveFilePath);
            }
        }

        /// <summary>
        /// int値を加算
        /// </summary>
        public int AddInt(string key, int amount)
        {
            var current = Get<int>(key, 0);
            var newValue = current + amount;
            Set(key, newValue);
            return newValue;
        }

        /// <summary>
        /// float値を加算
        /// </summary>
        public float AddFloat(string key, float amount)
        {
            var current = Get<float>(key, 0f);
            var newValue = current + amount;
            Set(key, newValue);
            return newValue;
        }

        /// <summary>
        /// 手動で保存
        /// </summary>
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(SaveFilePath, json);
                _lastSaveTime = Time.time;
                OnDataSaved?.Invoke();
                Debug.Log($"[ArsistDataManager] Saved to {SaveFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArsistDataManager] Save failed: {e.Message}");
            }
        }

        /// <summary>
        /// 手動で読み込み
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    var json = File.ReadAllText(SaveFilePath);
                    _data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json) 
                            ?? new Dictionary<string, object>();
                    Debug.Log($"[ArsistDataManager] Loaded from {SaveFilePath}");
                }
                else
                {
                    _data = new Dictionary<string, object>();
                    Debug.Log("[ArsistDataManager] No save file found, starting fresh");
                }
                OnDataLoaded?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArsistDataManager] Load failed: {e.Message}");
                _data = new Dictionary<string, object>();
            }
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// ハイスコアを更新（現在より高い場合のみ）
        /// </summary>
        public bool TrySetHighScore(string key, int score)
        {
            var current = Get<int>(key, 0);
            if (score > current)
            {
                Set(key, score);
                return true;
            }
            return false;
        }

        /// <summary>
        /// プレイ回数をインクリメント
        /// </summary>
        public int IncrementPlayCount(string key = "playCount")
        {
            return AddInt(key, 1);
        }

        /// <summary>
        /// 累計プレイ時間を更新
        /// </summary>
        public float AddPlayTime(string key = "totalPlayTime")
        {
            return AddFloat(key, Time.deltaTime);
        }

        /// <summary>
        /// 設定値を取得/設定（型安全）
        /// </summary>
        public float GetVolume(string key = "volume") => Get<float>(key, 1f);
        public void SetVolume(float value, string key = "volume") => Set(key, Mathf.Clamp01(value));

        #endregion

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Save();
                Instance = null;
            }
        }
    }
}
