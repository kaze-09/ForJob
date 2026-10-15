using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FrameSync.Protocol
{
    /// <summary>
    /// 传输帧：uint16 bodyLength | uint16 msgId | payload。
    /// bodyLength 含消息号，不含自己。小端。单包 payload 上限 8KB。
    /// </summary>
    public static class PacketCodec
    {
        public const int HeaderSize = 4;
        public const int MaxBodySize = 8192;

        public static byte[] Encode(MsgId id, byte[] payload)
        {
            if (payload == null)
                payload = Array.Empty<byte>();
            if (payload.Length + 2 > MaxBodySize)
                throw new InvalidDataException("packet too large");

            int bodyLength = 2 + payload.Length;
            byte[] packet = new byte[2 + bodyLength];
            packet[0] = (byte)bodyLength;
            packet[1] = (byte)(bodyLength >> 8);
            ushort msg = (ushort)id;
            packet[2] = (byte)msg;
            packet[3] = (byte)(msg >> 8);
            if (payload.Length > 0)
                Buffer.BlockCopy(payload, 0, packet, 4, payload.Length);
            return packet;
        }

        public static byte[] Encode(MsgId id)
        {
            return Encode(id, Array.Empty<byte>());
        }
    }

    public static class PacketIO
    {
        public static async Task ReadExactAsync(System.IO.Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("connection closed");
                offset += read;
            }
        }

        public static void ReadExact(System.IO.Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("connection closed");
                offset += read;
            }
        }
    }
}
