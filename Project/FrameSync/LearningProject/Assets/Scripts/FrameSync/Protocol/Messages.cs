using System;

namespace FrameSync.Protocol
{
    public struct PlayerInput
    {
        public sbyte MoveX;
        public sbyte MoveY;
        public ushort Buttons;

        public void Write(ByteWriter writer)
        {
            writer.WriteSByte(MoveX);
            writer.WriteSByte(MoveY);
            writer.WriteUInt16(Buttons);
        }

        public static PlayerInput Read(ByteReader reader)
        {
            PlayerInput input;
            input.MoveX = reader.ReadSByte();
            input.MoveY = reader.ReadSByte();
            input.Buttons = reader.ReadUInt16();
            return input;
        }
    }

    public sealed class PlayerSlot
    {
        public int PlayerId;
        public byte Seat;
        public bool Ready;
        public bool Connected;
        public string Name = "";

        public void Write(ByteWriter writer)
        {
            writer.WriteInt32(PlayerId);
            writer.WriteByte(Seat);
            writer.WriteBool(Ready);
            writer.WriteBool(Connected);
            writer.WriteString(Name);
        }

        public static PlayerSlot Read(ByteReader reader)
        {
            return new PlayerSlot
            {
                PlayerId = reader.ReadInt32(),
                Seat = reader.ReadByte(),
                Ready = reader.ReadBool(),
                Connected = reader.ReadBool(),
                Name = reader.ReadString(),
            };
        }
    }

    public sealed class JoinRequest
    {
        public int RoomId;
        public string Name = "";

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(32);
            writer.WriteInt32(RoomId);
            writer.WriteString(Name);
            return PacketCodec.Encode(MsgId.C2S_Join, writer.ToArray());
        }

        public static JoinRequest Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            return new JoinRequest
            {
                RoomId = reader.ReadInt32(),
                Name = reader.ReadString(),
            };
        }
    }

    public sealed class JoinResultMsg
    {
        public JoinResultCode Code;
        public int RoomId;
        public int PlayerId;
        public byte Seat;

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(16);
            writer.WriteByte((byte)Code);
            writer.WriteInt32(RoomId);
            writer.WriteInt32(PlayerId);
            writer.WriteByte(Seat);
            return PacketCodec.Encode(MsgId.S2C_JoinResult, writer.ToArray());
        }

        public static JoinResultMsg Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            return new JoinResultMsg
            {
                Code = (JoinResultCode)reader.ReadByte(),
                RoomId = reader.ReadInt32(),
                PlayerId = reader.ReadInt32(),
                Seat = reader.ReadByte(),
            };
        }
    }

    public sealed class RoomInfoMsg
    {
        public int RoomId;
        public RoomState State;
        public PlayerSlot[] Players = Array.Empty<PlayerSlot>();

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(64);
            writer.WriteInt32(RoomId);
            writer.WriteByte((byte)State);
            writer.WriteByte((byte)Players.Length);
            for (int i = 0; i < Players.Length; i++)
                Players[i].Write(writer);
            return PacketCodec.Encode(MsgId.S2C_RoomInfo, writer.ToArray());
        }

        public static RoomInfoMsg Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            RoomInfoMsg msg = new RoomInfoMsg
            {
                RoomId = reader.ReadInt32(),
                State = (RoomState)reader.ReadByte(),
            };
            int count = reader.ReadByte();
            msg.Players = new PlayerSlot[count];
            for (int i = 0; i < count; i++)
                msg.Players[i] = PlayerSlot.Read(reader);
            return msg;
        }
    }

    public sealed class GameStartMsg
    {
        public int RoomId;
        public uint RandomSeed;
        public ushort LogicFps;
        public uint StartFrame;
        public PlayerSlot[] Players = Array.Empty<PlayerSlot>();

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(64);
            writer.WriteInt32(RoomId);
            writer.WriteUInt32(RandomSeed);
            writer.WriteUInt16(LogicFps);
            writer.WriteUInt32(StartFrame);
            writer.WriteByte((byte)Players.Length);
            for (int i = 0; i < Players.Length; i++)
                Players[i].Write(writer);
            return PacketCodec.Encode(MsgId.S2C_GameStart, writer.ToArray());
        }

        public static GameStartMsg Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            GameStartMsg msg = new GameStartMsg
            {
                RoomId = reader.ReadInt32(),
                RandomSeed = reader.ReadUInt32(),
                LogicFps = reader.ReadUInt16(),
                StartFrame = reader.ReadUInt32(),
            };
            int count = reader.ReadByte();
            msg.Players = new PlayerSlot[count];
            for (int i = 0; i < count; i++)
                msg.Players[i] = PlayerSlot.Read(reader);
            return msg;
        }
    }

    public sealed class InputRequest
    {
        public PlayerInput Input;

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(8);
            Input.Write(writer);
            return PacketCodec.Encode(MsgId.C2S_Input, writer.ToArray());
        }

        public static InputRequest Read(byte[] payload)
        {
            return new InputRequest { Input = PlayerInput.Read(new ByteReader(payload)) };
        }
    }

    public sealed class FrameMsg
    {
        public uint Frame;
        /// <summary>下标就是座位号，和 GameStart.Players 里的 Seat 对应。</summary>
        public PlayerInput[] Inputs = Array.Empty<PlayerInput>();

        public void Write(ByteWriter writer)
        {
            writer.WriteUInt32(Frame);
            writer.WriteByte((byte)Inputs.Length);
            for (int i = 0; i < Inputs.Length; i++)
                Inputs[i].Write(writer);
        }

        public static FrameMsg Read(ByteReader reader)
        {
            FrameMsg msg = new FrameMsg { Frame = reader.ReadUInt32() };
            int count = reader.ReadByte();
            msg.Inputs = new PlayerInput[count];
            for (int i = 0; i < count; i++)
                msg.Inputs[i] = PlayerInput.Read(reader);
            return msg;
        }

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(32);
            Write(writer);
            return PacketCodec.Encode(MsgId.S2C_Frame, writer.ToArray());
        }

        public static FrameMsg Read(byte[] payload)
        {
            return Read(new ByteReader(payload));
        }
    }

    public sealed class ChaseRequest
    {
        public uint FromFrame;

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(8);
            writer.WriteUInt32(FromFrame);
            return PacketCodec.Encode(MsgId.C2S_ChaseFrames, writer.ToArray());
        }

        public static ChaseRequest Read(byte[] payload)
        {
            return new ChaseRequest { FromFrame = new ByteReader(payload).ReadUInt32() };
        }
    }

    public sealed class FrameBatchMsg
    {
        public bool Missed;
        public uint OldestFrame;
        public FrameMsg[] Frames = Array.Empty<FrameMsg>();

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(128);
            writer.WriteBool(Missed);
            writer.WriteUInt32(OldestFrame);
            writer.WriteUInt16((ushort)Frames.Length);
            for (int i = 0; i < Frames.Length; i++)
                Frames[i].Write(writer);
            return PacketCodec.Encode(MsgId.S2C_FrameBatch, writer.ToArray());
        }

        public static FrameBatchMsg Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            FrameBatchMsg msg = new FrameBatchMsg
            {
                Missed = reader.ReadBool(),
                OldestFrame = reader.ReadUInt32(),
            };
            int count = reader.ReadUInt16();
            msg.Frames = new FrameMsg[count];
            for (int i = 0; i < count; i++)
                msg.Frames[i] = FrameMsg.Read(reader);
            return msg;
        }
    }

    public sealed class PingRequest
    {
        public long ClientTime;

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(12);
            writer.WriteInt64(ClientTime);
            return PacketCodec.Encode(MsgId.C2S_Ping, writer.ToArray());
        }

        public static PingRequest Read(byte[] payload)
        {
            return new PingRequest { ClientTime = new ByteReader(payload).ReadInt64() };
        }
    }

    public sealed class PongMsg
    {
        public long ClientTime;
        public long ServerTime;

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(20);
            writer.WriteInt64(ClientTime);
            writer.WriteInt64(ServerTime);
            return PacketCodec.Encode(MsgId.S2C_Pong, writer.ToArray());
        }

        public static PongMsg Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            return new PongMsg
            {
                ClientTime = reader.ReadInt64(),
                ServerTime = reader.ReadInt64(),
            };
        }
    }

    public sealed class GameOverMsg
    {
        public GameOverReason Reason;
        public uint LastFrame;

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(8);
            writer.WriteByte((byte)Reason);
            writer.WriteUInt32(LastFrame);
            return PacketCodec.Encode(MsgId.S2C_GameOver, writer.ToArray());
        }

        public static GameOverMsg Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            return new GameOverMsg
            {
                Reason = (GameOverReason)reader.ReadByte(),
                LastFrame = reader.ReadUInt32(),
            };
        }
    }

    public sealed class ChecksumRequest
    {
        public uint Frame;
        public uint Hash;

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(12);
            writer.WriteUInt32(Frame);
            writer.WriteUInt32(Hash);
            return PacketCodec.Encode(MsgId.C2S_Checksum, writer.ToArray());
        }

        public static ChecksumRequest Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            return new ChecksumRequest
            {
                Frame = reader.ReadUInt32(),
                Hash = reader.ReadUInt32(),
            };
        }
    }

    public sealed class DesyncMsg
    {
        public uint Frame;
        public int PlayerIdA;
        public uint HashA;
        public int PlayerIdB;
        public uint HashB;

        public byte[] ToPacket()
        {
            ByteWriter writer = new ByteWriter(24);
            writer.WriteUInt32(Frame);
            writer.WriteInt32(PlayerIdA);
            writer.WriteUInt32(HashA);
            writer.WriteInt32(PlayerIdB);
            writer.WriteUInt32(HashB);
            return PacketCodec.Encode(MsgId.S2C_Desync, writer.ToArray());
        }

        public static DesyncMsg Read(byte[] payload)
        {
            ByteReader reader = new ByteReader(payload);
            return new DesyncMsg
            {
                Frame = reader.ReadUInt32(),
                PlayerIdA = reader.ReadInt32(),
                HashA = reader.ReadUInt32(),
                PlayerIdB = reader.ReadInt32(),
                HashB = reader.ReadUInt32(),
            };
        }
    }
}
