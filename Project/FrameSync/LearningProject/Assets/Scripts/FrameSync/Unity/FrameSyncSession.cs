using System;
using System.Collections.Concurrent;
using FrameSync.Protocol;
using UnityEngine;

namespace FrameSync
{
    /// <summary>
    /// 挂到场景物体上。网络回调会转到主线程再抛出。
    /// </summary>
    public sealed class FrameSyncSession : MonoBehaviour
    {
        public string host = "127.0.0.1";
        public int port = FrameSyncDefaults.Port;
        public string playerName = "Player";
        public bool showDebugGui = true;
        public int requestRoomId;

        public event Action<JoinResultMsg> Joined;
        public event Action<RoomInfoMsg> RoomUpdated;
        public event Action<GameStartMsg> GameStarted;
        public event Action<FrameMsg> FrameReceived;
        public event Action<FrameBatchMsg> FrameBatchReceived;
        public event Action<GameOverMsg> GameOver;
        public event Action<DesyncMsg> Desync;
        public event Action<string> Disconnected;

        public int RoomId { get; private set; }
        public int LocalPlayerId { get; private set; }
        public byte LocalSeat { get; private set; }
        public bool IsPlaying { get; private set; }
        public GameStartMsg StartInfo { get; private set; }

        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private string _status = "未连接";
        private FrameSyncConnection _connection;

        public void ConnectAndJoin(int roomId)
        {
            ShutdownConnection();
            _connection = new FrameSyncConnection();
            _connection.Joined += message => Enqueue(() =>
            {
                if (message.Code == JoinResultCode.Ok)
                {
                    RoomId = message.RoomId;
                    LocalPlayerId = message.PlayerId;
                    LocalSeat = message.Seat;
                    _status = "房间 " + RoomId + " 座位 " + LocalSeat;
                }
                else
                {
                    _status = "加入失败 " + message.Code;
                }
                Action<JoinResultMsg> handler = Joined;
                if (handler != null)
                    handler(message);
            });
            _connection.RoomUpdated += message => Enqueue(() =>
            {
                Action<RoomInfoMsg> handler = RoomUpdated;
                if (handler != null)
                    handler(message);
            });
            _connection.GameStarted += message => Enqueue(() =>
            {
                IsPlaying = true;
                StartInfo = message;
                _status = "对局中 seed " + message.RandomSeed;
                Action<GameStartMsg> handler = GameStarted;
                if (handler != null)
                    handler(message);
            });
            _connection.FrameReceived += message => Enqueue(() =>
            {
                Action<FrameMsg> handler = FrameReceived;
                if (handler != null)
                    handler(message);
            });
            _connection.FrameBatchReceived += message => Enqueue(() =>
            {
                if (message.Frames != null)
                {
                    for (int i = 0; i < message.Frames.Length; i++)
                    {
                        Action<FrameMsg> frameHandler = FrameReceived;
                        if (frameHandler != null)
                            frameHandler(message.Frames[i]);
                    }
                }
                Action<FrameBatchMsg> handler = FrameBatchReceived;
                if (handler != null)
                    handler(message);
            });
            _connection.GameOver += message => Enqueue(() =>
            {
                IsPlaying = false;
                _status = "结束 " + message.Reason;
                Action<GameOverMsg> handler = GameOver;
                if (handler != null)
                    handler(message);
            });
            _connection.Desync += message => Enqueue(() =>
            {
                _status = "不同步 帧 " + message.Frame;
                Action<DesyncMsg> handler = Desync;
                if (handler != null)
                    handler(message);
            });
            _connection.Disconnected += reason => Enqueue(() =>
            {
                IsPlaying = false;
                _status = "断开 " + reason;
                Action<string> handler = Disconnected;
                if (handler != null)
                    handler(reason);
            });

            string connectHost = host;
            int connectPort = port;
            string name = playerName;
            _connection.ConnectAsync(connectHost, connectPort).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Exception error = task.Exception != null ? task.Exception.GetBaseException() : null;
                    string message = error != null ? error.Message : "connect failed";
                    Enqueue(() =>
                    {
                        _status = "连接失败 " + message;
                        Action<string> handler = Disconnected;
                        if (handler != null)
                            handler(message);
                    });
                    return;
                }

                try
                {
                    _connection.Join(roomId, name);
                }
                catch (Exception exception)
                {
                    Enqueue(() =>
                    {
                        _status = "连接失败 " + exception.Message;
                        Action<string> handler = Disconnected;
                        if (handler != null)
                            handler(exception.Message);
                    });
                }
            });
        }

        public void Ready()
        {
            if (_connection != null)
                _connection.Ready();
        }

        public void SendInput(sbyte moveX, sbyte moveY, ushort buttons)
        {
            if (_connection != null && IsPlaying)
                _connection.SendInput(moveX, moveY, buttons);
        }

        public void RequestChase(uint fromFrame)
        {
            if (_connection != null)
                _connection.RequestChase(fromFrame);
        }

        /// <summary>
        /// 逻辑帧模拟结束后上报状态哈希。两边不一致时服务器会发 Desync。
        /// </summary>
        public void SendChecksum(uint frame, uint hash)
        {
            if (_connection != null && IsPlaying)
                _connection.SendChecksum(frame, hash);
        }

        private void Update()
        {
            Action action;
            int budget = 64;
            while (budget-- > 0 && _mainThread.TryDequeue(out action))
                action();
        }

        private void OnGUI()
        {
            if (!showDebugGui)
                return;

            GUILayout.BeginArea(new Rect(12f, 12f, 280f, 210f), GUI.skin.box);
            GUILayout.Label("帧同步");
            host = GUILayout.TextField(host);
            GUILayout.BeginHorizontal();
            GUILayout.Label("房间", GUILayout.Width(36f));
            string roomText = GUILayout.TextField(requestRoomId.ToString());
            int parsedRoom;
            if (int.TryParse(roomText, out parsedRoom))
                requestRoomId = parsedRoom;
            GUILayout.EndHorizontal();
            playerName = GUILayout.TextField(playerName);
            if (GUILayout.Button("连接并加入"))
                ConnectAndJoin(requestRoomId);
            if (GUILayout.Button("准备"))
                Ready();
            GUILayout.Label(_status);
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            ShutdownConnection();
        }

        private void Enqueue(Action action)
        {
            _mainThread.Enqueue(action);
        }

        private void ShutdownConnection()
        {
            if (_connection == null)
                return;
            _connection.Dispose();
            _connection = null;
            IsPlaying = false;
        }
    }
}
