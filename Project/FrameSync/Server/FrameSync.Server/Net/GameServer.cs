using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FrameSync.Server
{
    public sealed class GameServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly RoomManager _rooms = new RoomManager();
        private readonly ConcurrentDictionary<int, ClientSession> _sessions = new ConcurrentDictionary<int, ClientSession>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _nextSessionId;
        private int _started;

        public GameServer(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        }

        public int BoundPort { get; private set; }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
                return;

            _listener.Start();
            BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(() => AcceptLoop(_cts.Token));
            Log.Info("帧同步服务器已监听 0.0.0.0:" + BoundPort
                + "，逻辑帧 " + ServerConfig.LogicFps + "fps，每房 "
                + ServerConfig.MaxPlayers + " 人。roomId=0 表示创建房间。");
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
            }
            catch (Exception)
            {
            }

            foreach (ClientSession session in _sessions.Values)
                session.Close();
            _rooms.Dispose();
        }

        private async Task AcceptLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    break;
                }

                client.NoDelay = true;
                int sessionId = Interlocked.Increment(ref _nextSessionId);
                ClientSession session = new ClientSession(sessionId, client, _rooms, Forget);
                _sessions[sessionId] = session;
                session.Start();
                Log.Info("连接 #" + sessionId + " " + client.Client.RemoteEndPoint);
            }
        }

        private void Forget(ClientSession session)
        {
            _sessions.TryRemove(session.SessionId, out _);
        }
    }
}
