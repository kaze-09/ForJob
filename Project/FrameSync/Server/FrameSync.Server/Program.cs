using System;
using System.Threading;

namespace FrameSync.Server
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int port = ServerConfig.Port;
            if (args.Length > 0 && !int.TryParse(args[0], out port))
            {
                Console.WriteLine("用法: FrameSync.Server [端口]");
                return 1;
            }

            if (port <= 0 || port > 65535)
                port = ServerConfig.Port;

            using (GameServer server = new GameServer(port))
            {
                server.Start();
                Console.WriteLine("按 Ctrl+C 停止。客户端用 roomId=0 创建房间，再用返回的房间号加入。");
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    Console.CancelKeyPress += (_, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        stop.Set();
                    };
                    stop.Wait();
                }
            }

            return 0;
        }
    }
}
