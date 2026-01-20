using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arsist.Runtime.Audio
{
    /// <summary>
    /// サウンド管理システム
    /// BGM、SE、3Dサウンドを統一的に管理
    /// </summary>
    public class ArsistAudioManager : MonoBehaviour
    {
        public static ArsistAudioManager Instance { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource bgmSource;
        [SerializeField] private AudioSource seSource;
        [SerializeField] private int sePoolSize = 8;

        [Header("Settings")]
        [SerializeField] [Range(0, 1)] private float masterVolume = 1f;
        [SerializeField] [Range(0, 1)] private float bgmVolume = 0.7f;
        [SerializeField] [Range(0, 1)] private float seVolume = 1f;

        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();
        private readonly List<AudioSource> _sePool = new List<AudioSource>();
        private int _sePoolIndex;

        public float MasterVolume
        {
            get => masterVolume;
            set
            {
                masterVolume = Mathf.Clamp01(value);
                UpdateVolumes();
            }
        }

        public float BGMVolume
        {
            get => bgmVolume;
            set
            {
                bgmVolume = Mathf.Clamp01(value);
                UpdateVolumes();
            }
        }

        public float SEVolume
        {
            get => seVolume;
            set
            {
                seVolume = Mathf.Clamp01(value);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeAudioSources();
        }

        private void InitializeAudioSources()
        {
            // BGMソース
            if (bgmSource == null)
            {
                var bgmGO = new GameObject("BGM");
                bgmGO.transform.SetParent(transform);
                bgmSource = bgmGO.AddComponent<AudioSource>();
                bgmSource.loop = true;
                bgmSource.playOnAwake = false;
            }

            // SEソース
            if (seSource == null)
            {
                var seGO = new GameObject("SE");
                seGO.transform.SetParent(transform);
                seSource = seGO.AddComponent<AudioSource>();
                seSource.playOnAwake = false;
            }

            // SEプール
            for (int i = 0; i < sePoolSize; i++)
            {
                var sePoolGO = new GameObject($"SE_Pool_{i}");
                sePoolGO.transform.SetParent(transform);
                var source = sePoolGO.AddComponent<AudioSource>();
                source.playOnAwake = false;
                _sePool.Add(source);
            }
        }

        #region BGM

        /// <summary>
        /// BGMを再生
        /// </summary>
        public void PlayBGM(AudioClip clip, float fadeTime = 1f)
        {
            if (clip == null) return;

            if (fadeTime > 0 && bgmSource.isPlaying)
            {
                StartCoroutine(CrossFadeBGM(clip, fadeTime));
            }
            else
            {
                bgmSource.clip = clip;
                bgmSource.volume = bgmVolume * masterVolume;
                bgmSource.Play();
            }
        }

        /// <summary>
        /// BGMを再生（Resources.Loadで）
        /// </summary>
        public void PlayBGM(string resourcePath, float fadeTime = 1f)
        {
            var clip = LoadClip(resourcePath);
            if (clip != null) PlayBGM(clip, fadeTime);
        }

        /// <summary>
        /// BGMを停止
        /// </summary>
        public void StopBGM(float fadeTime = 1f)
        {
            if (fadeTime > 0 && bgmSource.isPlaying)
            {
                StartCoroutine(FadeOutBGM(fadeTime));
            }
            else
            {
                bgmSource.Stop();
            }
        }

        private System.Collections.IEnumerator CrossFadeBGM(AudioClip newClip, float duration)
        {
            float startVolume = bgmSource.volume;
            float elapsed = 0;

            // フェードアウト
            while (elapsed < duration / 2)
            {
                elapsed += Time.deltaTime;
                bgmSource.volume = Mathf.Lerp(startVolume, 0, elapsed / (duration / 2));
                yield return null;
            }

            // クリップ切り替え
            bgmSource.clip = newClip;
            bgmSource.Play();

            // フェードイン
            elapsed = 0;
            while (elapsed < duration / 2)
            {
                elapsed += Time.deltaTime;
                bgmSource.volume = Mathf.Lerp(0, bgmVolume * masterVolume, elapsed / (duration / 2));
                yield return null;
            }
        }

        private System.Collections.IEnumerator FadeOutBGM(float duration)
        {
            float startVolume = bgmSource.volume;
            float elapsed = 0;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                bgmSource.volume = Mathf.Lerp(startVolume, 0, elapsed / duration);
                yield return null;
            }

            bgmSource.Stop();
        }

        #endregion

        #region SE

        /// <summary>
        /// SEを再生
        /// </summary>
        public void PlaySE(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;
            seSource.PlayOneShot(clip, seVolume * masterVolume * volumeScale);
        }

        /// <summary>
        /// SEを再生（Resources.Loadで）
        /// </summary>
        public void PlaySE(string resourcePath, float volumeScale = 1f)
        {
            var clip = LoadClip(resourcePath);
            if (clip != null) PlaySE(clip, volumeScale);
        }

        /// <summary>
        /// SEを再生（プールから）
        /// 同時に複数の同じSEを鳴らす場合に使用
        /// </summary>
        public AudioSource PlaySEFromPool(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return null;

            var source = _sePool[_sePoolIndex];
            _sePoolIndex = (_sePoolIndex + 1) % _sePool.Count;

            source.clip = clip;
            source.volume = seVolume * masterVolume * volumeScale;
            source.Play();
            return source;
        }

        #endregion

        #region 3D Audio

        /// <summary>
        /// 3D空間でSEを再生
        /// </summary>
        public void PlaySE3D(AudioClip clip, Vector3 position, float volumeScale = 1f, float maxDistance = 20f)
        {
            if (clip == null) return;

            var source = PlaySEFromPool(clip, volumeScale);
            if (source != null)
            {
                source.transform.position = position;
                source.spatialBlend = 1f;
                source.maxDistance = maxDistance;
                source.rolloffMode = AudioRolloffMode.Linear;
            }
        }

        /// <summary>
        /// オブジェクトに追従する3Dサウンド
        /// </summary>
        public AudioSource PlaySE3DAttached(AudioClip clip, Transform parent, float volumeScale = 1f)
        {
            if (clip == null || parent == null) return null;

            var source = PlaySEFromPool(clip, volumeScale);
            if (source != null)
            {
                source.transform.SetParent(parent);
                source.transform.localPosition = Vector3.zero;
                source.spatialBlend = 1f;
            }
            return source;
        }

        #endregion

        #region Utilities

        private AudioClip LoadClip(string resourcePath)
        {
            if (_clipCache.TryGetValue(resourcePath, out var cached))
            {
                return cached;
            }

            var clip = Resources.Load<AudioClip>(resourcePath);
            if (clip != null)
            {
                _clipCache[resourcePath] = clip;
            }
            else
            {
                Debug.LogWarning($"[ArsistAudioManager] Clip not found: {resourcePath}");
            }
            return clip;
        }

        private void UpdateVolumes()
        {
            if (bgmSource != null && bgmSource.isPlaying)
            {
                bgmSource.volume = bgmVolume * masterVolume;
            }
        }

        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public void ClearCache()
        {
            _clipCache.Clear();
        }

        #endregion

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
