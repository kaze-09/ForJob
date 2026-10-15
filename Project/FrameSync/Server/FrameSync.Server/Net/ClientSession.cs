using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using FrameSync.Protocol;

namespace FrameSync.Server
{
    internal sealed class ClientSession
    {
        private readonly TcpClient _client;
        private readonly RoomManager _rooms;
        private readonly Action<ClientSession> _onClosed;
        private readonly object _sendLock = new object();
        private int _closed;

        public ClientSession(int sessionId, TcpClient client, RoomManager rooms, Action<ClientSession> onClosed)
        {
            SessionId = sessionId;
            _client = client;
            _rooms = rooms;
            _onClosed = onClosed;
        }

        public int SessionId { get; }
        public volatile GameRoom? Room;

        public void Start()
        {
            Thread thread = new Thread(ReadLoop);
            thread.IsBackground = true;
            thread.Name = "Session-" + SessionId;
            thread.Start();
        }

        public void Send(byte[] packet)
        {
            if (_closed != 0)
                return;

            try
            {
                NetworkStream stream = _client.GetStream();
                lock (_sendLock)
                {
                    stream.Write(packet, 0, packet.Length);
                }
            }
            catch (Exception)
            {
                Close();
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            try
            {
                _client.Close();
            }
            catch (Exception)
            {
            }
        }

        private void ReadLoop()
        {
            try
            {
                NetworkStream stream = _client.GetStream();
                byte[] header = new byte[PacketCodec.HeaderSize];
                while (_closed == 0)
                {
                    PacketIO.ReadExact(stream, header);
                    int bodyLength = header[0] | (header[1] << 8);
                    if (bodyLength < 2 || bodyLength > PacketCodec.MaxBodySize)
                        throw new InvalidDataException("bad packet length");

                    MsgId msgId = (MsgId)(header[2] | (header[3] << 8));
                    byte[] payload = bodyLength == 2 ? Array.Empty<byte>() : new byte[bodyLength - 2];
                    if (payload.Length > 0)
                        PacketIO.ReadExact(stream, payload);
                    Dispatch(msgId, payload);
                }
            }
            catch (Exception exception)
            {
                if (_closed == 0)
                    Log.Info("连接 #" + SessionId + " 断开: " + exception.Message);
            }
            finally
            {
                Close();
                _rooms.OnDisconnect(this);
                _onClosed(this);
            }
        }

        private void Dispatch(MsgId msgId, byte[] payload)
        {
            switch (msgId)
            {
                case MsgId.C2S_Join:
                    JoinRequest join = JoinRequest.Read(payload);
                    _rooms.OnJoin(this, join.RoomId, join.Name);
                    break;
                case MsgId.C2S_Ready:
                    _rooms.OnReady(this);
                    break;
                case MsgId.C2S_Input:
                    _rooms.OnInput(this, InputRequest.Read(payload).Input);
                    break;
                case MsgId.C2S_Ping:
                    PingRequest ping = PingRequest.Read(payload);
                    Send(new PongMsg
                    {
                        ClientTime = ping.ClientTime,
                        ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    }.ToPacket());
                    break;
                case MsgId.C2S_ChaseFrames:
                    _rooms.OnChase(this, ChaseRequest.Read(payload).FromFrame);
                    break;
                case MsgId.C2S_Leave:
                    _rooms.OnLeave(this);
                    break;
                case MsgId.C2S_Checksum:
                    ChecksumRequest checksum = ChecksumRequest.Read(payload);
                    _rooms.OnChecksum(this, checksum.Frame, checksum.Hash);
                    break;
                default:
                    throw new InvalidDataException("unknown message " + msgId);
            }
        }
    }
}
