using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arsist.Runtime.RemoteInput
{
    public class ArsistRemoteInputBehaviour : MonoBehaviour
    {
        [Serializable]
        public class TransportConfig
        {
            public bool enabled;
            public int port;
        }

        [Serializable]
        public class RemoteInputConfig
        {
            public TransportConfig udp = new TransportConfig { enabled = true, port = 19100 };
            public TransportConfig tcp = new TransportConfig { enabled = true, port = 19101 };
            public List<string> allowedEvents = new List<string>();
        }

        public static ArsistRemoteInputBehaviour Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private bool useManifestConfig = true;
        [SerializeField] private RemoteInputConfig config = new RemoteInputConfig();

        private readonly ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();
        private CancellationTokenSource _cts;
        private Thread _udpThread;
        private Thread _tcpAcceptThread;
        private readonly List<Thread> _tcpClientThreads = new List<Thread>();

        public event Action<string, JObject> OnRemoteEvent;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (useManifestConfig)
            {
                TryLoadConfigFromResources();
            }

            StartServers();
            
            // WebSocket リモートコントロールサーバーを自動起動（enableRemoteControl が true の場合）
            TryStartWebSocketServer();
        }

        private void OnDestroy()
        {
            StopServers();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            while (_messageQueue.TryDequeue(out var msg))
            {
                TryDispatch(msg);
            }
        }

        private void TryLoadConfigFromResources()
        {
            try
            {
                var text = Resources.Load<TextAsset>("ArsistManifest");
                if (text == null) return;

                var manifest = JObject.Parse(text.text);
                var remoteInput = manifest["remoteInput"] as JObject;
                if (remoteInput == null) return;

                config.udp.enabled = remoteInput.SelectToken("udp.enabled")?.Value<bool>() ?? config.udp.enabled;
                config.udp.port = remoteInput.SelectToken("udp.port")?.Value<int>() ?? config.udp.port;

                config.tcp.enabled = remoteInput.SelectToken("tcp.enabled")?.Value<bool>() ?? config.tcp.enabled;
                config.tcp.port = remoteInput.SelectToken("tcp.port")?.Value<int>() ?? config.tcp.port;

                var allowed = remoteInput["allowedEvents"] as JArray;
                if (allowed != null)
                {
                    config.allowedEvents.Clear();
                    foreach (var a in allowed)
                    {
                        var s = a?.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) config.allowedEvents.Add(s);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistRemoteInput] Failed to load manifest config: {e.Message}");
            }
        }

        private void TryStartWebSocketServer()
        {
            try
            {
                var text = Resources.Load<TextAsset>("ArsistManifest");
                if (text == null)
                {
                    Debug.Log("[ArsistRemoteInput] ArsistManifest not found, WebSocket server skipped");
                    return;
                }

                var manifest = JObject.Parse(text.text);
                var enableRemoteControl = manifest.SelectToken("arSettings.enableRemoteControl")?.Value<bool>() ?? false;
                
                if (!enableRemoteControl)
                {
                    Debug.Log("[ArsistRemoteInput] enableRemoteControl is false, WebSocket server not started");
                    return;
                }

                // WebSocket サーバーを起動
                var wsPort = manifest.SelectToken("arSettings.remoteControlPort")?.Value<int>() ?? 8765;
                var wsPassword = manifest.SelectToken("arSettings.remoteControlPassword")?.Value<string>() ?? string.Empty;

                var wsServerType = System.Type.GetType("Arsist.Runtime.Network.ArsistWebSocketServer, Assembly-CSharp");
                if (wsServerType == null)
                {
                    Debug.LogWarning("[ArsistRemoteInput] WebSocket server type not found");
                    return;
                }

                var go = GameObject.Find("ArsistWebSocketServer");
                if (go == null) go = new GameObject("ArsistWebSocketServer");

                var server = go.GetComponent(wsServerType);
                if (server == null)
                {
                    server = go.AddComponent(wsServerType);
                }

                // サーバーを設定開始
                var configMethod = wsServerType.GetMethod("Configure", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (configMethod != null)
                {
                    configMethod.Invoke(server, new object[] { wsPort, wsPassword, true });
                    Debug.Log($"[ArsistRemoteInput] WebSocket server configured and started on port {wsPort}");
                }
                else
                {
                    Debug.LogWarning("[ArsistRemoteInput] WebSocket Configure method not found");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistRemoteInput] Failed to start WebSocket server: {e.Message}");
            }
        }

        private bool IsAllowed(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return false;
            if (config.allowedEvents == null || config.allowedEvents.Count == 0) return true;
            return config.allowedEvents.Contains(eventName);
        }

        private void StartServers()
        {
            StopServers();
            _cts = new CancellationTokenSource();

            if (config.udp != null && config.udp.enabled)
            {
                _udpThread = new Thread(() => UdpLoop(_cts.Token)) { IsBackground = true, Name = "ArsistRemoteInput-UDP" };
                _udpThread.Start();
                Debug.Log($"[ArsistRemoteInput] UDP enabled on {config.udp.port}");
            }

            if (config.tcp != null && config.tcp.enabled)
            {
                _tcpAcceptThread = new Thread(() => TcpAcceptLoop(_cts.Token)) { IsBackground = true, Name = "ArsistRemoteInput-TCP" };
                _tcpAcceptThread.Start();
                Debug.Log($"[ArsistRemoteInput] TCP enabled on {config.tcp.port}");
            }
        }

        private void StopServers()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;

            try { _udpThread?.Join(100); } catch { }
            _udpThread = null;

            try { _tcpAcceptThread?.Join(100); } catch { }
            _tcpAcceptThread = null;

            lock (_tcpClientThreads)
            {
                foreach (var t in _tcpClientThreads)
                {
                    try { t?.Join(100); } catch { }
                }
                _tcpClientThreads.Clear();
            }
        }

        private void UdpLoop(CancellationToken token)
        {
            UdpClient client = null;
            try
            {
                client = new UdpClient(config.udp.port);
                client.Client.ReceiveTimeout = 500;
                var endPoint = new IPEndPoint(IPAddress.Any, 0);

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var data = client.Receive(ref endPoint);
                        if (data == null || data.Length == 0) continue;
                        var text = Encoding.UTF8.GetString(data);
                        _messageQueue.Enqueue(text);
                    }
                    catch (SocketException se)
                    {
                        if (se.SocketErrorCode == SocketError.TimedOut) continue;
                    }
                    catch (Exception)
                    {
                        // ignore and continue
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistRemoteInput] UDP loop stopped: {e.Message}");
            }
            finally
            {
                try { client?.Close(); } catch { }
            }
        }

        private void TcpAcceptLoop(CancellationToken token)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Any, config.tcp.port);
                listener.Start();

                while (!token.IsCancellationRequested)
                {
                    if (!listener.Pending())
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    var client = listener.AcceptTcpClient();
                    var t = new Thread(() => TcpClientLoop(client, token))
                    {
                        IsBackground = true,
                        Name = "ArsistRemoteInput-TCP-Client"
                    };
                    lock (_tcpClientThreads) _tcpClientThreads.Add(t);
                    t.Start();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistRemoteInput] TCP accept loop stopped: {e.Message}");
            }
            finally
            {
                try { listener?.Stop(); } catch { }
            }
        }

        private void TcpClientLoop(TcpClient client, CancellationToken token)
        {
            try
            {
                client.ReceiveTimeout = 500;
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (!token.IsCancellationRequested)
                    {
                        string line;
                        try
                        {
                            line = reader.ReadLine();
                        }
                        catch (IOException)
                        {
                            continue;
                        }

                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        _messageQueue.Enqueue(line);
                    }
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        private void TryDispatch(string message)
        {
            try
            {
                // Accept either plain text (eventName) or JSON: {"event":"...","payload":{...}}
                string eventName = null;
                JObject payload = null;

                var trimmed = message?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) return;

                if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                {
                    var obj = JObject.Parse(trimmed);
                    eventName = obj["event"]?.ToString() ?? obj["name"]?.ToString();
                    payload = obj["payload"] as JObject ?? obj;
                }
                else
                {
                    eventName = trimmed;
                    payload = new JObject { ["raw"] = trimmed };
                }

                if (!IsAllowed(eventName)) return;

                OnRemoteEvent?.Invoke(eventName, payload);
                Debug.Log($"[ArsistRemoteInput] Event: {eventName} Payload: {payload?.ToString(Formatting.None)}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArsistRemoteInput] Failed to dispatch message: {e.Message}");
            }
        }
    }
}
