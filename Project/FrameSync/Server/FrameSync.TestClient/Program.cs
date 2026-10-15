using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FrameSync;
using FrameSync.Protocol;
using FrameSync.Server;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
            return SmokeTest.Run();

        return BotClient.Run(args);
    }
}

internal static class SmokeTest
{
    public static int Run()
    {
        if (!RoundTrip())
            return 1;

        using (GameServer server = new GameServer(0))
        {
            server.Start();
            Peer a = new Peer();
            Peer b = new Peer();
            Peer c = new Peer();
            Thread input = null;
            try
            {
                Connect(a, server.BoundPort);
                Connect(b, server.BoundPort);
                Connect(c, server.BoundPort);

                a.Conn.Join(0, "甲");
                if (!Wait(() => Locked(a, () => a.Join != null && a.Join.Code == JoinResultCode.Ok), 3000))
                    return Fail("A 没有进房");

                int roomId = a.Join.RoomId;
                b.Conn.Join(roomId, "乙");
                if (!Wait(() => Locked(b, () => b.Join != null && b.Join.Code == JoinResultCode.Ok), 3000))
                    return Fail("B 没有进房");

                c.Conn.Join(roomId, "丙");
                if (!Wait(() => Locked(c, () => c.Join != null && c.Join.Code == JoinResultCode.RoomFull), 3000))
                    return Fail("满员时没有拒绝第三人");

                if (a.Join.PlayerId == b.Join.PlayerId)
                    return Fail("两个玩家分到了同一个 id");

                a.Conn.Ready();
                b.Conn.Ready();
                if (!Wait(() => a.Start != null && b.Start != null, 3000))
                    return Fail("没有开局");

                if (a.Start.RandomSeed != b.Start.RandomSeed || a.Start.LogicFps != 30 || a.Start.StartFrame != 1)
                    return Fail("开局参数不一致");

                Flag runInput = new Flag();
                input = new Thread(() =>
                {
                    while (runInput.Run)
                    {
                        try
                        {
                            a.Conn.SendInput(1, 0, InputButtons.Attack);
                            b.Conn.SendInput(-1, 1, 0);
                        }
                        catch (Exception)
                        {
                            break;
                        }

                        Thread.Sleep(20);
                    }
                });
                input.IsBackground = true;
                input.Start();

                if (!Wait(() => Count(a) >= 20 && Count(b) >= 20, 5000))
                    return Fail("帧不够，A=" + Count(a) + " B=" + Count(b));

                FrameMsg[] framesA = Snapshot(a);
                FrameMsg[] framesB = Snapshot(b);
                for (int i = 0; i < framesA.Length; i++)
                {
                    if (framesA[i].Frame != (uint)(i + 1) || framesA[i].Inputs.Length != 2)
                        return Fail("A 的帧序号不连续");
                }

                FrameMsg matched = null;
                for (int i = 0; i < framesA.Length; i++)
                {
                    PlayerInput[] inputs = framesA[i].Inputs;
                    if (inputs[0].MoveX == 1 && inputs[0].Buttons == InputButtons.Attack
                        && inputs[1].MoveX == -1 && inputs[1].MoveY == 1)
                    {
                        matched = framesA[i];
                        break;
                    }
                }

                if (matched == null)
                    return Fail("采样到的输入不对");

                FrameMsg sameOnB = null;
                for (int i = 0; i < framesB.Length; i++)
                {
                    if (framesB[i].Frame == matched.Frame)
                    {
                        sameOnB = framesB[i];
                        break;
                    }
                }

                if (sameOnB == null || sameOnB.Inputs[0].MoveX != 1 || sameOnB.Inputs[1].MoveX != -1)
                    return Fail("两边同一帧的输入不一致");

                a.Conn.RequestChase(1);
                if (!Wait(() => a.Batch != null && !a.Batch.Missed && a.Batch.Frames.Length > 0 && a.Batch.Frames[0].Frame == 1, 3000))
                    return Fail("追帧失败");

                a.Conn.SendChecksum(5, 111);
                b.Conn.SendChecksum(5, 222);
                if (!Wait(() => a.Desync != null && a.Desync.Frame == 5 && a.Desync.HashA != a.Desync.HashB, 3000))
                    return Fail("没有报不同步");

                runInput.Run = false;
                b.Conn.Dispose();
                if (!Wait(() => a.Over != null && a.Over.Reason == GameOverReason.PlayerLeft, 3000))
                    return Fail("掉线后没有结束");

                Console.WriteLine("帧同步烟测通过，房间 " + roomId + "，seed " + a.Start.RandomSeed);
                return 0;
            }
            finally
            {
                if (input != null)
                    input.Join(500);
                a.Conn.Dispose();
                b.Conn.Dispose();
                c.Conn.Dispose();
            }
        }
    }

    private static bool RoundTrip()
    {
        JoinRequest join = new JoinRequest { RoomId = 12, Name = "甲" };
        byte[] packet = join.ToPacket();
        JoinRequest back = JoinRequest.Read(Payload(packet));
        if (back.RoomId != 12 || back.Name != "甲")
        {
            Fail("Join 编解码失败");
            return false;
        }

        FrameMsg frame = new FrameMsg
        {
            Frame = 7,
            Inputs = new[]
            {
                new PlayerInput { MoveX = 1, MoveY = 0, Buttons = InputButtons.Jump },
                new PlayerInput { MoveX = -1, MoveY = 1, Buttons = 0 },
            },
        };
        FrameMsg frameBack = FrameMsg.Read(Payload(frame.ToPacket()));
        if (frameBack.Frame != 7 || frameBack.Inputs.Length != 2 || frameBack.Inputs[1].MoveY != 1)
        {
            Fail("Frame 编解码失败");
            return false;
        }

        return true;
    }

    private static void Connect(Peer peer, int port)
    {
        peer.Conn.Joined += message => { lock (peer.Gate) peer.Join = message; };
        peer.Conn.GameStarted += message => { lock (peer.Gate) peer.Start = message; };
        peer.Conn.FrameReceived += message => { lock (peer.Gate) peer.Frames.Add(message); };
        peer.Conn.FrameBatchReceived += message => { lock (peer.Gate) peer.Batch = message; };
        peer.Conn.Desync += message => { lock (peer.Gate) peer.Desync = message; };
        peer.Conn.GameOver += message => { lock (peer.Gate) peer.Over = message; };
        TaskWait(peer.Conn.ConnectAsync("127.0.0.1", port), 3000, "连接失败");
    }

    private static void TaskWait(System.Threading.Tasks.Task task, int milliseconds, string message)
    {
        if (!task.Wait(milliseconds))
            throw new TimeoutException(message);
        if (task.IsFaulted)
            throw task.Exception.GetBaseException();
    }

    private static byte[] Payload(byte[] packet)
    {
        int bodyLength = packet[0] | (packet[1] << 8);
        byte[] payload = new byte[bodyLength - 2];
        Buffer.BlockCopy(packet, 4, payload, 0, payload.Length);
        return payload;
    }

    private static bool Wait(Func<bool> condition, int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }

        return condition();
    }

    private static bool Locked(Peer peer, Func<bool> condition)
    {
        lock (peer.Gate)
            return condition();
    }

    private static int Count(Peer peer)
    {
        lock (peer.Gate)
            return peer.Frames.Count;
    }

    private static FrameMsg[] Snapshot(Peer peer)
    {
        lock (peer.Gate)
            return peer.Frames.ToArray();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private sealed class Flag
    {
        public volatile bool Run = true;
    }

    private sealed class Peer
    {
        public readonly FrameSyncConnection Conn = new FrameSyncConnection();
        public readonly object Gate = new object();
        public readonly List<FrameMsg> Frames = new List<FrameMsg>();
        public JoinResultMsg Join;
        public GameStartMsg Start;
        public FrameBatchMsg Batch;
        public DesyncMsg Desync;
        public GameOverMsg Over;
    }
}

internal static class BotClient
{
    public static int Run(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("自测: dotnet run --project Server/FrameSync.TestClient");
            Console.WriteLine("机器人: dotnet run --project Server/FrameSync.TestClient -- <host> <port> <name> <roomId>");
            Console.WriteLine("roomId 填 0 会创建房间。");
            return 1;
        }

        string host = args[0];
        int port = int.Parse(args[1]);
        string name = args[2];
        int roomId = int.Parse(args[3]);
        using (FrameSyncConnection connection = new FrameSyncConnection())
        using (ManualResetEventSlim done = new ManualResetEventSlim(false))
        {
            bool playing = false;
            bool readied = false;
            connection.Joined += message =>
                Console.WriteLine("加入结果 " + message.Code + " 房间 " + message.RoomId + " 座位 " + message.Seat);
            connection.RoomUpdated += message =>
            {
                Console.WriteLine("房间 " + message.RoomId + " 状态 " + message.State + " 人数 " + message.Players.Length);
                if (!readied && message.State == RoomState.Waiting)
                {
                    readied = true;
                    connection.Ready();
                }
            };
            connection.GameStarted += message =>
            {
                playing = true;
                Console.WriteLine("开局 seed=" + message.RandomSeed + " fps=" + message.LogicFps);
            };
            connection.FrameReceived += message =>
            {
                if (message.Frame % 30 == 0)
                    Console.WriteLine("帧 " + message.Frame);
            };
            connection.Desync += message =>
                Console.WriteLine("不同步 帧 " + message.Frame + " " + message.HashA + " != " + message.HashB);
            connection.GameOver += message =>
            {
                playing = false;
                Console.WriteLine("结束 " + message.Reason + " last=" + message.LastFrame);
                done.Set();
            };
            connection.Disconnected += reason =>
            {
                Console.WriteLine("断开 " + reason);
                done.Set();
            };

            connection.ConnectAsync(host, port).GetAwaiter().GetResult();
            connection.Join(roomId, name);
            Thread input = new Thread(() =>
            {
                sbyte move = 1;
                while (playing || !readied)
                {
                    if (playing)
                    {
                        try
                        {
                            connection.SendInput(move, 0, 0);
                            move = (sbyte)-move;
                        }
                        catch (Exception)
                        {
                            break;
                        }
                    }

                    Thread.Sleep(30);
                }
            });
            input.IsBackground = true;
            input.Start();

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                done.Set();
            };
            done.Wait();
            playing = false;
        }

        return 0;
    }
}
