using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace FpsGame.Network
{
    public struct LanServerInfo
    {
        public string Ip;
        public int Port;
        public string Name;
        public int Players;
    }

    /// <summary>
    /// UDP 局域网发现：主机在 47777 端口应答查询，客户端广播查询并收集主机列表。
    /// 网络收发在后台线程执行，结果经 ConcurrentQueue 回到主线程。
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        public const int GamePort = 7777;
        public const int DiscoveryPort = 47777;

        private const string QueryToken = "LANFPS_QUERY_V1";
        private const string ReplyPrefix = "LANFPS_HOST_V1|";
        private const float ClientResendInterval = 1.2f;

        public static LanDiscovery Instance { get; private set; }

        /// <summary>客户端发现新主机时在主线程触发。</summary>
        public event Action<LanServerInfo> ServerFound;

        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;
        private volatile bool _hostMode;
        private volatile string _hostName = "FPS 主机";
        private volatile int _cachedPlayerCount = 1;
        private readonly ConcurrentQueue<LanServerInfo> _found = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            Stop();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // 主线程缓存在线人数，后台线程应答时只读缓存（避免跨线程访问 NGO 集合）
            if (_hostMode && _running && NetworkManagerRef())
                _cachedPlayerCount = PlayerCount();

            while (_found.TryDequeue(out LanServerInfo info))
                ServerFound?.Invoke(info);
        }

        private static bool NetworkManagerRef()
        {
            var nm = NetworkManagerSingleton();
            return nm != null && nm.IsListening;
        }

        private static NetworkManager NetworkManagerSingleton() => NetworkManager.Singleton;

        private static int PlayerCount() => NetworkManager.Singleton.ConnectedClientsList.Count;

        /// <summary>以主机身份应答局域网查询。</summary>
        public void StartHost(string roomName)
        {
            Stop();
            _hostMode = true;
            _hostName = string.IsNullOrEmpty(roomName) ? "FPS 主机" : roomName;
            StartSocket($"绑定发现端口 {DiscoveryPort} 失败（可能被占用或防火墙阻止）");
        }

        /// <summary>以客户端身份扫描局域网主机（持续到 Stop）。</summary>
        public void StartScan()
        {
            Stop();
            _hostMode = false;
            StartSocket(null);
        }

        public void Stop()
        {
            _running = false;
            Thread thread = _thread;
            _thread = null;
            UdpClient udp = _udp;
            _udp = null;
            try { udp?.Close(); } catch { /* 忽略关闭异常 */ }
            try { thread?.Join(400); } catch { /* 线程可能已退出 */ }
        }

        private void StartSocket(string bindErrorHint)
        {
            try
            {
                _running = true;
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _udp.Client.ReceiveTimeout = 300;
                _thread = new Thread(ThreadLoop) { IsBackground = true };
                _thread.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LanDiscovery] 启动失败: {e.Message}" + (bindErrorHint != null ? $" — {bindErrorHint}" : ""));
                _running = false;
                _udp = null;
            }
        }

        private void ThreadLoop()
        {
            byte[] queryBytes = Encoding.UTF8.GetBytes(QueryToken);
            int lastSend = 0;
            var queryTarget = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            var loopbackTarget = new IPEndPoint(IPAddress.Loopback, DiscoveryPort);
            var anyEndpoint = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                try
                {
                    // 收包
                    byte[] data = _udp.Receive(ref anyEndpoint);
                    HandlePacket(Encoding.UTF8.GetString(data), anyEndpoint);

                    // 客户端模式：周期性广播查询（含回环，便于同机双开测试）
                    int now = Environment.TickCount & int.MaxValue;
                    if (!_hostMode && unchecked(now - lastSend) > (int)(ClientResendInterval * 1000))
                    {
                        lastSend = now;
                        try { _udp.Send(queryBytes, queryBytes.Length, queryTarget); } catch { /* 广播可能被禁 */ }
                        try { _udp.Send(queryBytes, queryBytes.Length, loopbackTarget); } catch { }
                    }
                }
                catch (SocketException) { /* ReceiveTimeout 正常路径 */ }
                catch (ObjectDisposedException) { break; }
                catch (ThreadInterruptedException) { break; }
                catch { /* 网络抖动，继续 */ }
            }
        }

        private void HandlePacket(string message, IPEndPoint from)
        {
            if (_hostMode)
            {
                if (message != QueryToken) return;
                string reply = $"{ReplyPrefix}{_hostName}|{GamePort}|{Mathf.Max(1, _cachedPlayerCount)}";
                byte[] payload = Encoding.UTF8.GetBytes(reply);
                try { _udp.Send(payload, payload.Length, from); } catch { }
                return;
            }

            // 客户端模式：解析主机应答
            if (!message.StartsWith(ReplyPrefix, StringComparison.Ordinal)) return;
            string[] parts = message.Substring(ReplyPrefix.Length).Split('|');
            if (parts.Length < 3) return;
            if (!int.TryParse(parts[1], out int port)) return;
            if (!int.TryParse(parts[2], out int playerCount)) return;

            _found.Enqueue(new LanServerInfo
            {
                Ip = from.Address.ToString(),
                Port = port,
                Name = parts[0],
                Players = playerCount
            });
        }
    }
}
