namespace FrameSync.Protocol
{
    /// <summary>
    /// 客户端和服务器共用的默认值。真正开局后以 GameStart 里的逻辑帧率为准。
    /// </summary>
    public static class FrameSyncDefaults
    {
        public const int Port = 29000;
        public const int MaxNameChars = 16;
    }
}
