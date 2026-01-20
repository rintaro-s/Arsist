using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Arsist.Runtime.Network
{
    /// <summary>
    /// HTTPリクエストのシンプルなラッパー
    /// REST API呼び出し、画像ダウンロードなどに使用
    /// </summary>
    public class ArsistHttp : MonoBehaviour
    {
        public static ArsistHttp Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private float defaultTimeout = 30f;
        [SerializeField] private int maxRetries = 3;

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

        #region GET

        /// <summary>
        /// GETリクエスト
        /// </summary>
        public void Get(string url, Action<string> onSuccess, Action<string> onError = null, Dictionary<string, string> headers = null)
        {
            StartCoroutine(GetCoroutine(url, onSuccess, onError, headers));
        }

        private IEnumerator GetCoroutine(string url, Action<string> onSuccess, Action<string> onError, Dictionary<string, string> headers)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = (int)defaultTimeout;
                ApplyHeaders(request, headers);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(request.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        /// <summary>
        /// GETリクエスト（JSON自動パース）
        /// </summary>
        public void GetJson<T>(string url, Action<T> onSuccess, Action<string> onError = null, Dictionary<string, string> headers = null)
        {
            Get(url, 
                json => {
                    try
                    {
                        var result = JsonUtility.FromJson<T>(json);
                        onSuccess?.Invoke(result);
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"JSON parse error: {e.Message}");
                    }
                }, 
                onError, 
                headers
            );
        }

        #endregion

        #region POST

        /// <summary>
        /// POSTリクエスト（JSON）
        /// </summary>
        public void PostJson(string url, object data, Action<string> onSuccess, Action<string> onError = null, Dictionary<string, string> headers = null)
        {
            StartCoroutine(PostJsonCoroutine(url, data, onSuccess, onError, headers));
        }

        private IEnumerator PostJsonCoroutine(string url, object data, Action<string> onSuccess, Action<string> onError, Dictionary<string, string> headers)
        {
            var json = JsonUtility.ToJson(data);
            var bodyRaw = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)defaultTimeout;
                ApplyHeaders(request, headers);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(request.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        /// <summary>
        /// POSTリクエスト（Form）
        /// </summary>
        public void PostForm(string url, Dictionary<string, string> formData, Action<string> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(PostFormCoroutine(url, formData, onSuccess, onError));
        }

        private IEnumerator PostFormCoroutine(string url, Dictionary<string, string> formData, Action<string> onSuccess, Action<string> onError)
        {
            var form = new WWWForm();
            foreach (var kvp in formData)
            {
                form.AddField(kvp.Key, kvp.Value);
            }

            using (var request = UnityWebRequest.Post(url, form))
            {
                request.timeout = (int)defaultTimeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(request.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        #endregion

        #region Download

        /// <summary>
        /// テクスチャをダウンロード
        /// </summary>
        public void DownloadTexture(string url, Action<Texture2D> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(DownloadTextureCoroutine(url, onSuccess, onError));
        }

        private IEnumerator DownloadTextureCoroutine(string url, Action<Texture2D> onSuccess, Action<string> onError)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                request.timeout = (int)defaultTimeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(request);
                    onSuccess?.Invoke(texture);
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        /// <summary>
        /// オーディオをダウンロード
        /// </summary>
        public void DownloadAudio(string url, AudioType audioType, Action<AudioClip> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(DownloadAudioCoroutine(url, audioType, onSuccess, onError));
        }

        private IEnumerator DownloadAudioCoroutine(string url, AudioType audioType, Action<AudioClip> onSuccess, Action<string> onError)
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                request.timeout = (int)defaultTimeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(request);
                    onSuccess?.Invoke(clip);
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        /// <summary>
        /// AssetBundleをダウンロード
        /// </summary>
        public void DownloadAssetBundle(string url, Action<AssetBundle> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(DownloadAssetBundleCoroutine(url, onSuccess, onError));
        }

        private IEnumerator DownloadAssetBundleCoroutine(string url, Action<AssetBundle> onSuccess, Action<string> onError)
        {
            using (var request = UnityWebRequestAssetBundle.GetAssetBundle(url))
            {
                request.timeout = (int)defaultTimeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var bundle = DownloadHandlerAssetBundle.GetContent(request);
                    onSuccess?.Invoke(bundle);
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        #endregion

        #region Utility

        private void ApplyHeaders(UnityWebRequest request, Dictionary<string, string> headers)
        {
            if (headers == null) return;
            foreach (var kvp in headers)
            {
                request.SetRequestHeader(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// リトライ付きGETリクエスト
        /// </summary>
        public void GetWithRetry(string url, Action<string> onSuccess, Action<string> onError = null, int retries = -1)
        {
            if (retries < 0) retries = maxRetries;
            StartCoroutine(GetWithRetryCoroutine(url, onSuccess, onError, retries));
        }

        private IEnumerator GetWithRetryCoroutine(string url, Action<string> onSuccess, Action<string> onError, int retriesLeft)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = (int)defaultTimeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(request.downloadHandler.text);
                }
                else if (retriesLeft > 0)
                {
                    Debug.LogWarning($"[ArsistHttp] Retry {maxRetries - retriesLeft + 1}/{maxRetries}: {url}");
                    yield return new WaitForSeconds(1f);
                    yield return GetWithRetryCoroutine(url, onSuccess, onError, retriesLeft - 1);
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        #endregion
    }
}
