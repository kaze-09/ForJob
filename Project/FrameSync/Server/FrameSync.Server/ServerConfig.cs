namespace FrameSync.Server
{
    internal static class ServerConfig
    {
        public const int Port = Protocol.FrameSyncDefaults.Port;
        public const int MaxPlayers = 2;
        public const int MinPlayersToStart = 2;
        public const int LogicFps = 30;
        public const int FrameHistory = 900;
        public const int ChaseBatchSize = 30;
        public const int MaxNameChars = Protocol.FrameSyncDefaults.MaxNameChars;
    }
}
