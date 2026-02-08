using UnityEngine;

namespace Arsist.Runtime
{
    /// <summary>
    /// GLB/GLTFモデルの読み込み後、指定された回転を適用するコンポーネント
    /// ArsistModelRuntimeLoaderと組み合わせて使用される
    /// </summary>
    public class ArsistModelRotationApplier : MonoBehaviour
    {
        [Tooltip("適用する目標回転（Euler角度・度数法）")]
        public Vector3 targetRotation = Vector3.zero;

        [Tooltip("モデル読み込み後、この秒数後に回転を適用")]
        public float applyDelay = 0.5f;

        private float _delayTimer = 0f;
        private bool _rotationApplied = false;
        private ArsistModelRuntimeLoader _modelLoader;

        private void Start()
        {
            // 同じGameObjectに ArsistModelRuntimeLoader がアタッチされている場合、そのloadCompleteまで待つ
            _modelLoader = GetComponent<ArsistModelRuntimeLoader>();
            _delayTimer = 0f;
            _rotationApplied = false;
        }

        private void Update()
        {
            if (_rotationApplied)
                return;

            _delayTimer += Time.deltaTime;

            // 遅延時間経過後、回転を適用
            if (_delayTimer >= applyDelay)
            {
                ApplyTargetRotation();
                _rotationApplied = true;
            }
        }

        /// <summary>
        /// 目標回転をこのGameObjectに適用
        /// </summary>
        private void ApplyTargetRotation()
        {
            // 親がある場合は個別に適用
            if (transform.parent != null)
            {
                // ローカル回転として適用
                transform.localRotation = Quaternion.Euler(targetRotation);
                Debug.Log($"[ArsistModelRotationApplier] Applied local rotation: {targetRotation} to {gameObject.name}");
            }
            else
            {
                // 親がない場合、グローバル回転として適用
                transform.rotation = Quaternion.Euler(targetRotation);
                Debug.Log($"[ArsistModelRotationApplier] Applied global rotation: {targetRotation} to {gameObject.name}");
            }

            // ログ出力：実際に適用された回転を確認
            var appliedEuler = transform.localRotation.eulerAngles;
            if ((appliedEuler - targetRotation).magnitude > 0.01f)
            {
                Debug.LogWarning($"[ArsistModelRotationApplier] Rotation mismatch detected. Target: {targetRotation}, Applied: {appliedEuler}");
            }
        }

        /// <summary>
        /// 外部から回転を設定して即座に適用（遅延なし）
        /// </summary>
        public void SetRotationImmediate(Vector3 newRotation)
        {
            targetRotation = newRotation;
            ApplyTargetRotation();
            _rotationApplied = true;
        }
    }
}
