namespace FrameSync.Protocol
{
    public enum MsgId : ushort
    {
        C2S_Join = 1,
        C2S_Ready = 2,
        C2S_Input = 3,
        C2S_Ping = 4,
        C2S_ChaseFrames = 5,
        C2S_Leave = 6,
        C2S_Checksum = 7,

        S2C_JoinResult = 101,
        S2C_RoomInfo = 102,
        S2C_GameStart = 103,
        S2C_Frame = 104,
        S2C_FrameBatch = 105,
        S2C_GameOver = 106,
        S2C_Pong = 107,
        S2C_Desync = 108,
    }

    public enum JoinResultCode : byte
    {
        Ok = 0,
        RoomFull = 1,
        RoomNotFound = 2,
        AlreadyStarted = 3,
        AlreadyInRoom = 4,
    }

    public enum RoomState : byte
    {
        Waiting = 0,
        Playing = 1,
        Over = 2,
    }

    public enum GameOverReason : byte
    {
        PlayerLeft = 1,
        RoomClosed = 2,
    }

    /// <summary>
    /// 按键是“按住状态”，不是边沿。服务端没收到新包时会沿用上一帧，所以松手必须发 0。
    /// </summary>
    public static class InputButtons
    {
        public const ushort Attack = 1 << 0;
        public const ushort Skill1 = 1 << 1;
        public const ushort Skill2 = 1 << 2;
        public const ushort Jump = 1 << 3;
    }
}
