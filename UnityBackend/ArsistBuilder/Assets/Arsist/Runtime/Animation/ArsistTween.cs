using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Arsist.Runtime.Animation
{
    /// <summary>
    /// シンプルなTweenライブラリ
    /// 外部依存なしでアニメーションを実現
    /// </summary>
    public class ArsistTween : MonoBehaviour
    {
        public static ArsistTween Instance { get; private set; }

        private readonly List<TweenData> _activeTweens = new List<TweenData>();
        private readonly List<TweenData> _tweensToRemove = new List<TweenData>();

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

        private void Update()
        {
            _tweensToRemove.Clear();

            foreach (var tween in _activeTweens)
            {
                if (tween.target == null)
                {
                    _tweensToRemove.Add(tween);
                    continue;
                }

                tween.elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(tween.elapsed / tween.duration);
                float easedT = ApplyEasing(t, tween.easing);

                tween.updateAction?.Invoke(easedT);

                if (t >= 1f)
                {
                    tween.onComplete?.Invoke();
                    _tweensToRemove.Add(tween);
                }
            }

            foreach (var tween in _tweensToRemove)
            {
                _activeTweens.Remove(tween);
            }
        }

        #region Public API

        /// <summary>
        /// 位置をアニメーション
        /// </summary>
        public static TweenData MoveTo(Transform target, Vector3 endPosition, float duration, Easing easing = Easing.EaseOutQuad)
        {
            EnsureInstance();
            var startPosition = target.position;
            var tween = new TweenData
            {
                target = target,
                duration = duration,
                easing = easing,
                updateAction = (t) => target.position = Vector3.LerpUnclamped(startPosition, endPosition, t)
            };
            Instance._activeTweens.Add(tween);
            return tween;
        }

        /// <summary>
        /// ローカル位置をアニメーション
        /// </summary>
        public static TweenData MoveLocalTo(Transform target, Vector3 endPosition, float duration, Easing easing = Easing.EaseOutQuad)
        {
            EnsureInstance();
            var startPosition = target.localPosition;
            var tween = new TweenData
            {
                target = target,
                duration = duration,
                easing = easing,
                updateAction = (t) => target.localPosition = Vector3.LerpUnclamped(startPosition, endPosition, t)
            };
            Instance._activeTweens.Add(tween);
            return tween;
        }

        /// <summary>
        /// 回転をアニメーション
        /// </summary>
        public static TweenData RotateTo(Transform target, Quaternion endRotation, float duration, Easing easing = Easing.EaseOutQuad)
        {
            EnsureInstance();
            var startRotation = target.rotation;
            var tween = new TweenData
            {
                target = target,
                duration = duration,
                easing = easing,
                updateAction = (t) => target.rotation = Quaternion.SlerpUnclamped(startRotation, endRotation, t)
            };
            Instance._activeTweens.Add(tween);
            return tween;
        }

        /// <summary>
        /// スケールをアニメーション
        /// </summary>
        public static TweenData ScaleTo(Transform target, Vector3 endScale, float duration, Easing easing = Easing.EaseOutQuad)
        {
            EnsureInstance();
            var startScale = target.localScale;
            var tween = new TweenData
            {
                target = target,
                duration = duration,
                easing = easing,
                updateAction = (t) => target.localScale = Vector3.LerpUnclamped(startScale, endScale, t)
            };
            Instance._activeTweens.Add(tween);
            return tween;
        }

        /// <summary>
        /// 色をアニメーション（Renderer）
        /// </summary>
        public static TweenData ColorTo(Renderer target, Color endColor, float duration, Easing easing = Easing.EaseOutQuad)
        {
            EnsureInstance();
            var startColor = target.material.color;
            var tween = new TweenData
            {
                target = target.transform,
                duration = duration,
                easing = easing,
                updateAction = (t) => target.material.color = Color.LerpUnclamped(startColor, endColor, t)
            };
            Instance._activeTweens.Add(tween);
            return tween;
        }

        /// <summary>
        /// アルファをアニメーション（CanvasGroup）
        /// </summary>
        public static TweenData FadeTo(CanvasGroup target, float endAlpha, float duration, Easing easing = Easing.EaseOutQuad)
        {
            EnsureInstance();
            var startAlpha = target.alpha;
            var tween = new TweenData
            {
                target = target.transform,
                duration = duration,
                easing = easing,
                updateAction = (t) => target.alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, t)
            };
            Instance._activeTweens.Add(tween);
            return tween;
        }

        /// <summary>
        /// float値をアニメーション
        /// </summary>
        public static TweenData Value(float start, float end, float duration, Action<float> onUpdate, Easing easing = Easing.EaseOutQuad)
        {
            EnsureInstance();
            var go = new GameObject("TweenValue");
            go.hideFlags = HideFlags.HideAndDontSave;
            var tween = new TweenData
            {
                target = go.transform,
                duration = duration,
                easing = easing,
                updateAction = (t) => onUpdate?.Invoke(Mathf.LerpUnclamped(start, end, t)),
                onComplete = () => { if (go != null) Destroy(go); }
            };
            Instance._activeTweens.Add(tween);
            return tween;
        }

        /// <summary>
        /// 遅延実行
        /// </summary>
        public static TweenData Delay(float delay, Action onComplete)
        {
            EnsureInstance();
            var go = new GameObject("TweenDelay");
            go.hideFlags = HideFlags.HideAndDontSave;
            var tween = new TweenData
            {
                target = go.transform,
                duration = delay,
                easing = Easing.Linear,
                onComplete = () =>
                {
                    onComplete?.Invoke();
                    if (go != null) Destroy(go);
                }
            };
            Instance._activeTweens.Add(tween);
            return tween;
        }

        /// <summary>
        /// 特定ターゲットのTweenをすべて停止
        /// </summary>
        public static void Kill(Transform target)
        {
            if (Instance == null) return;
            Instance._activeTweens.RemoveAll(t => t.target == target);
        }

        /// <summary>
        /// すべてのTweenを停止
        /// </summary>
        public static void KillAll()
        {
            if (Instance == null) return;
            Instance._activeTweens.Clear();
        }

        #endregion

        private static void EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("ArsistTween");
                Instance = go.AddComponent<ArsistTween>();
            }
        }

        private static float ApplyEasing(float t, Easing easing)
        {
            return easing switch
            {
                Easing.Linear => t,
                Easing.EaseInQuad => t * t,
                Easing.EaseOutQuad => t * (2 - t),
                Easing.EaseInOutQuad => t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t,
                Easing.EaseInCubic => t * t * t,
                Easing.EaseOutCubic => 1 + (--t) * t * t,
                Easing.EaseInOutCubic => t < 0.5f ? 4 * t * t * t : 1 + (--t) * (2 * (--t)) * (2 * t),
                Easing.EaseInBack => t * t * (2.70158f * t - 1.70158f),
                Easing.EaseOutBack => 1 + (--t) * t * (2.70158f * t + 1.70158f),
                Easing.EaseOutElastic => t == 0 ? 0 : t == 1 ? 1 : Mathf.Pow(2, -10 * t) * Mathf.Sin((t * 10 - 0.75f) * (2 * Mathf.PI) / 3) + 1,
                Easing.EaseOutBounce => BounceOut(t),
                _ => t
            };
        }

        private static float BounceOut(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1 / d1) return n1 * t * t;
            if (t < 2 / d1) return n1 * (t -= 1.5f / d1) * t + 0.75f;
            if (t < 2.5 / d1) return n1 * (t -= 2.25f / d1) * t + 0.9375f;
            return n1 * (t -= 2.625f / d1) * t + 0.984375f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public class TweenData
        {
            public Transform target;
            public float duration;
            public float elapsed;
            public Easing easing;
            public Action<float> updateAction;
            public Action onComplete;

            public TweenData OnComplete(Action action)
            {
                onComplete = action;
                return this;
            }
        }

        public enum Easing
        {
            Linear,
            EaseInQuad,
            EaseOutQuad,
            EaseInOutQuad,
            EaseInCubic,
            EaseOutCubic,
            EaseInOutCubic,
            EaseInBack,
            EaseOutBack,
            EaseOutElastic,
            EaseOutBounce
        }
    }
}
