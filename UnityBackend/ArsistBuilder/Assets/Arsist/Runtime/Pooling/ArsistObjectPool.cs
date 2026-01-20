using System.Collections.Generic;
using UnityEngine;

namespace Arsist.Runtime.Pooling
{
    /// <summary>
    /// 汎用オブジェクトプール
    /// 頻繁に生成/破棄されるオブジェクトのパフォーマンス最適化に使用
    /// </summary>
    public class ArsistObjectPool : MonoBehaviour
    {
        public static ArsistObjectPool Instance { get; private set; }

        [System.Serializable]
        public class PoolConfig
        {
            public string poolId;
            public GameObject prefab;
            public int initialSize = 10;
            public int maxSize = 100;
        }

        [SerializeField] private List<PoolConfig> poolConfigs = new List<PoolConfig>();

        private Dictionary<string, Queue<GameObject>> _pools = new Dictionary<string, Queue<GameObject>>();
        private Dictionary<string, PoolConfig> _configMap = new Dictionary<string, PoolConfig>();
        private Dictionary<string, Transform> _poolParents = new Dictionary<string, Transform>();
        private Dictionary<GameObject, string> _activeObjects = new Dictionary<GameObject, string>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializePools();
        }

        private void InitializePools()
        {
            foreach (var config in poolConfigs)
            {
                CreatePool(config.poolId, config.prefab, config.initialSize, config.maxSize);
            }
        }

        /// <summary>
        /// 新しいプールを作成
        /// </summary>
        public void CreatePool(string poolId, GameObject prefab, int initialSize = 10, int maxSize = 100)
        {
            if (_pools.ContainsKey(poolId))
            {
                Debug.LogWarning($"[ArsistObjectPool] Pool '{poolId}' already exists");
                return;
            }

            var config = new PoolConfig
            {
                poolId = poolId,
                prefab = prefab,
                initialSize = initialSize,
                maxSize = maxSize
            };
            _configMap[poolId] = config;

            // プール用の親オブジェクトを作成
            var poolParent = new GameObject($"Pool_{poolId}");
            poolParent.transform.SetParent(transform);
            _poolParents[poolId] = poolParent.transform;

            // プールを初期化
            var pool = new Queue<GameObject>();
            for (int i = 0; i < initialSize; i++)
            {
                var obj = CreatePooledObject(poolId, prefab, poolParent.transform);
                pool.Enqueue(obj);
            }
            _pools[poolId] = pool;

            Debug.Log($"[ArsistObjectPool] Created pool '{poolId}' with {initialSize} objects");
        }

        private GameObject CreatePooledObject(string poolId, GameObject prefab, Transform parent)
        {
            var obj = Instantiate(prefab, parent);
            obj.SetActive(false);

            // IPoolable コンポーネントがあれば初期化
            var poolable = obj.GetComponent<IPoolable>();
            poolable?.OnPoolCreated(poolId);

            return obj;
        }

        /// <summary>
        /// プールからオブジェクトを取得
        /// </summary>
        public GameObject Get(string poolId, Vector3 position = default, Quaternion rotation = default)
        {
            if (!_pools.TryGetValue(poolId, out var pool))
            {
                Debug.LogError($"[ArsistObjectPool] Pool '{poolId}' not found");
                return null;
            }

            GameObject obj;
            if (pool.Count > 0)
            {
                obj = pool.Dequeue();
            }
            else
            {
                // プールが空の場合は新しく生成（maxSize以下なら）
                var config = _configMap[poolId];
                if (_activeObjects.Count < config.maxSize)
                {
                    obj = CreatePooledObject(poolId, config.prefab, _poolParents[poolId]);
                }
                else
                {
                    Debug.LogWarning($"[ArsistObjectPool] Pool '{poolId}' reached max size");
                    return null;
                }
            }

            obj.transform.position = position;
            obj.transform.rotation = rotation;
            obj.SetActive(true);
            _activeObjects[obj] = poolId;

            // IPoolable コンポーネントがあれば通知
            var poolable = obj.GetComponent<IPoolable>();
            poolable?.OnGetFromPool();

            return obj;
        }

        /// <summary>
        /// オブジェクトをプールに返却
        /// </summary>
        public void Return(GameObject obj)
        {
            if (obj == null) return;

            if (!_activeObjects.TryGetValue(obj, out var poolId))
            {
                Debug.LogWarning($"[ArsistObjectPool] Object '{obj.name}' is not from a pool");
                Destroy(obj);
                return;
            }

            _activeObjects.Remove(obj);

            // IPoolable コンポーネントがあれば通知
            var poolable = obj.GetComponent<IPoolable>();
            poolable?.OnReturnToPool();

            obj.SetActive(false);
            obj.transform.SetParent(_poolParents[poolId]);
            _pools[poolId].Enqueue(obj);
        }

        /// <summary>
        /// 指定時間後にプールに返却
        /// </summary>
        public void ReturnDelayed(GameObject obj, float delay)
        {
            StartCoroutine(ReturnDelayedCoroutine(obj, delay));
        }

        private System.Collections.IEnumerator ReturnDelayedCoroutine(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            Return(obj);
        }

        /// <summary>
        /// プールを破棄
        /// </summary>
        public void DestroyPool(string poolId)
        {
            if (!_pools.TryGetValue(poolId, out var pool))
            {
                return;
            }

            // プール内のオブジェクトを破棄
            while (pool.Count > 0)
            {
                var obj = pool.Dequeue();
                if (obj != null) Destroy(obj);
            }

            // アクティブなオブジェクトも破棄
            var toRemove = new List<GameObject>();
            foreach (var kvp in _activeObjects)
            {
                if (kvp.Value == poolId)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var obj in toRemove)
            {
                _activeObjects.Remove(obj);
                if (obj != null) Destroy(obj);
            }

            // 親オブジェクトを破棄
            if (_poolParents.TryGetValue(poolId, out var parent))
            {
                Destroy(parent.gameObject);
                _poolParents.Remove(poolId);
            }

            _pools.Remove(poolId);
            _configMap.Remove(poolId);

            Debug.Log($"[ArsistObjectPool] Destroyed pool '{poolId}'");
        }

        /// <summary>
        /// すべてのプールをクリア
        /// </summary>
        public void ClearAllPools()
        {
            var poolIds = new List<string>(_pools.Keys);
            foreach (var poolId in poolIds)
            {
                DestroyPool(poolId);
            }
        }

        /// <summary>
        /// プールの統計情報を取得
        /// </summary>
        public PoolStats GetPoolStats(string poolId)
        {
            if (!_pools.TryGetValue(poolId, out var pool))
            {
                return new PoolStats();
            }

            var activeCount = 0;
            foreach (var kvp in _activeObjects)
            {
                if (kvp.Value == poolId) activeCount++;
            }

            return new PoolStats
            {
                poolId = poolId,
                pooledCount = pool.Count,
                activeCount = activeCount,
                totalCount = pool.Count + activeCount
            };
        }

        public struct PoolStats
        {
            public string poolId;
            public int pooledCount;
            public int activeCount;
            public int totalCount;
        }
    }

    /// <summary>
    /// プール対象オブジェクトが実装するインターフェース
    /// </summary>
    public interface IPoolable
    {
        /// <summary>
        /// プールに作成された時に呼ばれる
        /// </summary>
        void OnPoolCreated(string poolId);

        /// <summary>
        /// プールから取得された時に呼ばれる
        /// </summary>
        void OnGetFromPool();

        /// <summary>
        /// プールに返却された時に呼ばれる
        /// </summary>
        void OnReturnToPool();
    }
}
