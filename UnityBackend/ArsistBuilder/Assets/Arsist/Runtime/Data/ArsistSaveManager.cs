using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arsist.Runtime.Data
{
    /// <summary>
    /// ゲーム進行状態を保存/復元するためのシリアライズ可能なクラス
    /// </summary>
    [Serializable]
    public class ArsistGameState
    {
        public string currentScene;
        public float playTime;
        public int score;
        public Dictionary<string, bool> flags = new Dictionary<string, bool>();
        public Dictionary<string, int> counters = new Dictionary<string, int>();
        public Dictionary<string, float> values = new Dictionary<string, float>();
        public Dictionary<string, string> strings = new Dictionary<string, string>();
        public List<string> unlockedItems = new List<string>();
        public Vector3 playerPosition;
        public Quaternion playerRotation;
        public DateTime saveTime;

        public ArsistGameState()
        {
            saveTime = DateTime.Now;
        }

        // フラグ操作
        public void SetFlag(string key, bool value) => flags[key] = value;
        public bool GetFlag(string key, bool defaultValue = false) 
            => flags.TryGetValue(key, out var v) ? v : defaultValue;

        // カウンター操作
        public void SetCounter(string key, int value) => counters[key] = value;
        public int GetCounter(string key, int defaultValue = 0) 
            => counters.TryGetValue(key, out var v) ? v : defaultValue;
        public int IncrementCounter(string key)
        {
            var current = GetCounter(key);
            counters[key] = current + 1;
            return current + 1;
        }

        // 数値操作
        public void SetValue(string key, float value) => values[key] = value;
        public float GetValue(string key, float defaultValue = 0f) 
            => values.TryGetValue(key, out var v) ? v : defaultValue;

        // 文字列操作
        public void SetString(string key, string value) => strings[key] = value;
        public string GetString(string key, string defaultValue = "") 
            => strings.TryGetValue(key, out var v) ? v : defaultValue;

        // アイテム操作
        public void UnlockItem(string itemId)
        {
            if (!unlockedItems.Contains(itemId))
            {
                unlockedItems.Add(itemId);
            }
        }
        public bool IsItemUnlocked(string itemId) => unlockedItems.Contains(itemId);
    }

    /// <summary>
    /// セーブスロット管理
    /// </summary>
    public class ArsistSaveSlot
    {
        public int slotIndex;
        public string slotName;
        public ArsistGameState state;
        public DateTime lastSaveTime;
        public string thumbnailPath; // スクリーンショット保存先（オプション）

        public bool HasData => state != null;
    }

    /// <summary>
    /// 複数セーブスロットを管理するマネージャー
    /// </summary>
    public class ArsistSaveManager : MonoBehaviour
    {
        public static ArsistSaveManager Instance { get; private set; }

        [SerializeField] private int maxSlots = 3;

        public ArsistGameState CurrentState { get; private set; } = new ArsistGameState();
        public int CurrentSlot { get; private set; } = 0;

        public event Action<int> OnSaveCompleted;
        public event Action<int> OnLoadCompleted;

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
        /// 指定スロットに保存
        /// </summary>
        public void SaveToSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= maxSlots) return;
            
            CurrentState.saveTime = DateTime.Now;
            CurrentState.currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            
            var dataManager = ArsistDataManager.Instance;
            if (dataManager != null)
            {
                dataManager.Set($"save_slot_{slotIndex}", CurrentState);
                dataManager.Set($"save_slot_{slotIndex}_time", CurrentState.saveTime);
                dataManager.Save();
            }

            CurrentSlot = slotIndex;
            OnSaveCompleted?.Invoke(slotIndex);
            Debug.Log($"[ArsistSaveManager] Saved to slot {slotIndex}");
        }

        /// <summary>
        /// 指定スロットから読み込み
        /// </summary>
        public bool LoadFromSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= maxSlots) return false;
            
            var dataManager = ArsistDataManager.Instance;
            if (dataManager == null) return false;

            var state = dataManager.Get<ArsistGameState>($"save_slot_{slotIndex}");
            if (state == null) return false;

            CurrentState = state;
            CurrentSlot = slotIndex;
            OnLoadCompleted?.Invoke(slotIndex);
            Debug.Log($"[ArsistSaveManager] Loaded from slot {slotIndex}");
            return true;
        }

        /// <summary>
        /// スロット情報を取得
        /// </summary>
        public ArsistSaveSlot GetSlotInfo(int slotIndex)
        {
            var slot = new ArsistSaveSlot { slotIndex = slotIndex };
            
            var dataManager = ArsistDataManager.Instance;
            if (dataManager != null)
            {
                slot.state = dataManager.Get<ArsistGameState>($"save_slot_{slotIndex}");
                slot.lastSaveTime = dataManager.Get<DateTime>($"save_slot_{slotIndex}_time");
                slot.slotName = slot.state?.currentScene ?? $"Slot {slotIndex + 1}";
            }

            return slot;
        }

        /// <summary>
        /// 全スロット情報を取得
        /// </summary>
        public List<ArsistSaveSlot> GetAllSlots()
        {
            var slots = new List<ArsistSaveSlot>();
            for (int i = 0; i < maxSlots; i++)
            {
                slots.Add(GetSlotInfo(i));
            }
            return slots;
        }

        /// <summary>
        /// スロットを削除
        /// </summary>
        public void DeleteSlot(int slotIndex)
        {
            var dataManager = ArsistDataManager.Instance;
            if (dataManager != null)
            {
                dataManager.DeleteKey($"save_slot_{slotIndex}");
                dataManager.DeleteKey($"save_slot_{slotIndex}_time");
                dataManager.Save();
            }
        }

        /// <summary>
        /// クイックセーブ（スロット0に保存）
        /// </summary>
        public void QuickSave() => SaveToSlot(0);

        /// <summary>
        /// クイックロード（スロット0から読み込み）
        /// </summary>
        public bool QuickLoad() => LoadFromSlot(0);
    }
}
