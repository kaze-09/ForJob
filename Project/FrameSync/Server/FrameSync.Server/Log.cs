using System;

namespace FrameSync.Server
{
    internal static class Log
    {
        public static void Info(string message)
        {
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
        }
    }
}
