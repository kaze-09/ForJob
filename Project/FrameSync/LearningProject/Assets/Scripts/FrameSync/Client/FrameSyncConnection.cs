using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FrameSync.Protocol;

namespace FrameSync
{
    /// <summary>
    /// 不依赖 Unity 的 TCP 客户端。回调发生在接收线程，Unity 侧需要切回主线程。
    /// </summary>
    public sealed class FrameSyncConnection : IDisposable
    {
        public event Action Connected;
        public event Action<JoinResultMsg> Joined;
        public event Action<RoomInfoMsg> RoomUpdated;
        public event Action<GameStartMsg> GameStarted;
        public event Action<FrameMsg> FrameReceived;
        public event Action<FrameBatchMsg> FrameBatchReceived;
        public event Action<GameOverMsg> GameOver;
        public event Action<PongMsg> PongReceived;
        public event Action<DesyncMsg> Desync;
        public event Action<string> Disconnected;

        private readonly object _sendLock = new object();
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _readThread;
        private int _disposed;

        public bool IsConnected
        {
            get { return _client != null && _client.Connected && _disposed == 0; }
        }

        public Task ConnectAsync(string host, int port)
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(FrameSyncConnection));

            return Task.Run(() =>
            {
                TcpClient client = new TcpClient();
                client.NoDelay = true;
                client.Connect(host, port);
                _client = client;
                _stream = client.GetStream();
                _readThread = new Thread(ReadLoop);
                _readThread.IsBackground = true;
                _readThread.Name = "FrameSyncRecv";
                _readThread.Start();
                Action connected = Connected;
                if (connected != null)
                    connected();
            });
        }

        public void Join(int roomId, string playerName)
        {
            Send(new JoinRequest { RoomId = roomId, Name = playerName }.ToPacket());
        }

        public void Ready()
        {
            Send(PacketCodec.Encode(MsgId.C2S_Ready));
        }

        public void SendInput(sbyte moveX, sbyte moveY, ushort buttons)
        {
            PlayerInput input;
            input.MoveX = moveX;
            input.MoveY = moveY;
            input.Buttons = buttons;
            Send(new InputRequest { Input = input }.ToPacket());
        }

        public void RequestChase(uint fromFrame)
        {
            Send(new ChaseRequest { FromFrame = fromFrame }.ToPacket());
        }

        public void SendChecksum(uint frame, uint hash)
        {
            Send(new ChecksumRequest { Frame = frame, Hash = hash }.ToPacket());
        }

        public void Ping(long clientTime)
        {
            Send(new PingRequest { ClientTime = clientTime }.ToPacket());
        }

        public void Leave()
        {
            Send(PacketCodec.Encode(MsgId.C2S_Leave));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            CloseSocket();
        }

        private void Send(byte[] packet)
        {
            if (_disposed != 0 || _stream == null)
                throw new InvalidOperationException("not connected");

            lock (_sendLock)
            {
                _stream.Write(packet, 0, packet.Length);
            }
        }

        private void ReadLoop()
        {
            string reason = "closed";
            try
            {
                byte[] header = new byte[PacketCodec.HeaderSize];
                while (_disposed == 0)
                {
                    PacketIO.ReadExact(_stream, header);
                    int bodyLength = header[0] | (header[1] << 8);
                    if (bodyLength < 2 || bodyLength > PacketCodec.MaxBodySize)
                        throw new InvalidDataException("bad packet length");

                    int msgId = header[2] | (header[3] << 8);
                    byte[] payload = bodyLength == 2 ? Array.Empty<byte>() : new byte[bodyLength - 2];
                    if (payload.Length > 0)
                        PacketIO.ReadExact(_stream, payload);
                    Dispatch((MsgId)msgId, payload);
                }
            }
            catch (Exception exception)
            {
                if (_disposed == 0)
                    reason = exception.Message;
            }

            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                CloseSocket();

            Action<string> disconnected = Disconnected;
            if (disconnected != null)
                disconnected(reason);
        }

        private void Dispatch(MsgId msgId, byte[] payload)
        {
            switch (msgId)
            {
                case MsgId.S2C_JoinResult:
                    Raise(Joined, JoinResultMsg.Read(payload));
                    break;
                case MsgId.S2C_RoomInfo:
                    Raise(RoomUpdated, RoomInfoMsg.Read(payload));
                    break;
                case MsgId.S2C_GameStart:
                    Raise(GameStarted, GameStartMsg.Read(payload));
                    break;
                case MsgId.S2C_Frame:
                    Raise(FrameReceived, FrameMsg.Read(payload));
                    break;
                case MsgId.S2C_FrameBatch:
                    Raise(FrameBatchReceived, FrameBatchMsg.Read(payload));
                    break;
                case MsgId.S2C_GameOver:
                    Raise(GameOver, GameOverMsg.Read(payload));
                    break;
                case MsgId.S2C_Pong:
                    Raise(PongReceived, PongMsg.Read(payload));
                    break;
                case MsgId.S2C_Desync:
                    Raise(Desync, DesyncMsg.Read(payload));
                    break;
                default:
                    throw new InvalidDataException("unknown message " + msgId);
            }
        }

        private static void Raise<T>(Action<T> handler, T message)
        {
            if (handler != null)
                handler(message);
        }

        private void CloseSocket()
        {
            try
            {
                if (_client != null)
                    _client.Close();
            }
            catch (Exception)
            {
            }
        }
    }
}
