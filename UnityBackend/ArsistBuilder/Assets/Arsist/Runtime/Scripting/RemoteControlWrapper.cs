// ==============================================
// Arsist Engine - Remote Control Wrapper
// Assets/Arsist/Runtime/Scripting/RemoteControlWrapper.cs
// ==============================================
using UnityEngine;
using Arsist.Runtime.Network;

namespace Arsist.Runtime.Scripting
{
    /// <summary>
    /// Jint に "remote" として公開されるラッパー。
    /// スクリプトから WebSocket リモート制御サーバーを手動起動/停止できる。
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class RemoteControlWrapper
    {
        /// <summary>
        /// remote.startServer(port, password) — サーバーを手動起動
        /// password は空文字で認証なし。
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        public void startServer(int port, string password)
        {
            var go = GameObject.Find("ArsistWebSocketServer");
            if (go == null)
            {
                go = new GameObject("ArsistWebSocketServer");
            }

            var server = go.GetComponent<ArsistWebSocketServer>();
            if (server == null)
            {
                server = go.AddComponent<ArsistWebSocketServer>();
            }

            server.Configure(port, password ?? string.Empty, true);
            server.StartServer();
        }

        /// <summary>
        /// remote.stopServer() — サーバーを停止
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        public void stopServer()
        {
            var server = ArsistWebSocketServer.Instance;
            if (server != null)
            {
                server.StopServer();
            }
        }

        /// <summary>
        /// remote.isRunning() — サーバー起動状態（存在判定ベース）
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        public bool isRunning()
        {
            return ArsistWebSocketServer.Instance != null && ArsistWebSocketServer.Instance.IsRunning;
        }
    }
}
