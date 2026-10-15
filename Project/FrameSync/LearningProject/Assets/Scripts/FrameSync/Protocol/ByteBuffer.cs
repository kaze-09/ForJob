using System;
using System.IO;
using System.Text;

namespace FrameSync.Protocol
{
    public sealed class ByteWriter
    {
        private byte[] _buffer;
        private int _count;

        public ByteWriter(int capacity)
        {
            _buffer = new byte[capacity < 16 ? 16 : capacity];
        }

        public ByteWriter() : this(64)
        {
        }

        public int Length
        {
            get { return _count; }
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_count++] = value;
        }

        public void WriteSByte(sbyte value)
        {
            WriteByte((byte)value);
        }

        public void WriteBool(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteUInt16(ushort value)
        {
            Ensure(2);
            _buffer[_count++] = (byte)value;
            _buffer[_count++] = (byte)(value >> 8);
        }

        public void WriteInt32(int value)
        {
            WriteUInt32((uint)value);
        }

        public void WriteUInt32(uint value)
        {
            Ensure(4);
            _buffer[_count++] = (byte)value;
            _buffer[_count++] = (byte)(value >> 8);
            _buffer[_count++] = (byte)(value >> 16);
            _buffer[_count++] = (byte)(value >> 24);
        }

        public void WriteInt64(long value)
        {
            Ensure(8);
            ulong bits = (ulong)value;
            for (int i = 0; i < 8; i++)
            {
                _buffer[_count++] = (byte)bits;
                bits >>= 8;
            }
        }

        public void WriteString(string value)
        {
            if (value == null)
                value = "";

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > 64)
                throw new InvalidDataException("string too long");

            WriteUInt16((ushort)bytes.Length);
            Ensure(bytes.Length);
            Buffer.BlockCopy(bytes, 0, _buffer, _count, bytes.Length);
            _count += bytes.Length;
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[_count];
            Buffer.BlockCopy(_buffer, 0, result, 0, _count);
            return result;
        }

        private void Ensure(int more)
        {
            if (_count + more <= _buffer.Length)
                return;

            int size = _buffer.Length * 2;
            if (size < _count + more)
                size = _count + more;
            Array.Resize(ref _buffer, size);
        }
    }

    public sealed class ByteReader
    {
        private readonly byte[] _buffer;
        private int _offset;

        public ByteReader(byte[] buffer)
        {
            _buffer = buffer ?? Array.Empty<byte>();
        }

        public int Remaining
        {
            get { return _buffer.Length - _offset; }
        }

        public byte ReadByte()
        {
            Need(1);
            return _buffer[_offset++];
        }

        public sbyte ReadSByte()
        {
            return (sbyte)ReadByte();
        }

        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        public ushort ReadUInt16()
        {
            Need(2);
            ushort value = (ushort)(_buffer[_offset] | (_buffer[_offset + 1] << 8));
            _offset += 2;
            return value;
        }

        public int ReadInt32()
        {
            return (int)ReadUInt32();
        }

        public uint ReadUInt32()
        {
            Need(4);
            uint value = (uint)(_buffer[_offset]
                | (_buffer[_offset + 1] << 8)
                | (_buffer[_offset + 2] << 16)
                | (_buffer[_offset + 3] << 24));
            _offset += 4;
            return value;
        }

        public long ReadInt64()
        {
            Need(8);
            ulong value = 0;
            for (int i = 0; i < 8; i++)
                value |= (ulong)_buffer[_offset++] << (8 * i);
            return (long)value;
        }

        public string ReadString()
        {
            int length = ReadUInt16();
            if (length > 64)
                throw new InvalidDataException("string too long");
            Need(length);
            string value = Encoding.UTF8.GetString(_buffer, _offset, length);
            _offset += length;
            return value;
        }

        private void Need(int count)
        {
            if (Remaining < count)
                throw new InvalidDataException("packet truncated");
        }
    }
}
