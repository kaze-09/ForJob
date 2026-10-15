using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using FrameSync.Protocol;

namespace FrameSync.Server
{
    /// <summary>
    /// 一个房间一条循环。网络线程只把操作丢进队列。
    /// 玩法不在服务器上跑：每个逻辑帧采样各座位最新的按键状态，按座位号广播。
    /// 没收到新包就沿用上一帧，用来表示按住不放。
    /// </summary>
    internal sealed class GameRoom
    {
        private readonly RoomManager _manager;
        private readonly Channel<Action> _inbox = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        private readonly List<Seat> _seats = new List<Seat>();
        private readonly Dictionary<uint, PlayerInput[]> _history = new Dictionary<uint, PlayerInput[]>();
        private readonly Queue<uint> _historyOrder = new Queue<uint>();
        private readonly Dictionary<uint, uint[]> _hashes = new Dictionary<uint, uint[]>();
        private readonly Dictionary<uint, bool[]> _hashSeen = new Dictionary<uint, bool[]>();
        private readonly HashSet<uint> _desyncSent = new HashSet<uint>();

        private RoomState _state = RoomState.Waiting;
        private uint _frame = 1;
        private uint _randomSeed;
        private int _nextPlayerId = 1;
        private bool _running = true;

        public GameRoom(int roomId, RoomManager manager)
        {
            RoomId = roomId;
            _manager = manager;
        }

        public int RoomId { get; }

        public void Start()
        {
            Task.Run(RunAsync);
        }

        public bool Enqueue(Action action)
        {
            return _inbox.Writer.TryWrite(action);
        }

        public void ForceClose()
        {
            if (!_running)
                return;
            Shutdown(GameOverReason.RoomClosed, _seats.Count > 0);
        }

        public void HandleJoin(ClientSession session, string name)
        {
            if (!_running || _state != RoomState.Waiting)
            {
                Reject(session, JoinResultCode.AlreadyStarted);
                return;
            }

            if (Find(session) != null)
                return;

            if (_seats.Count >= ServerConfig.MaxPlayers)
            {
                Reject(session, JoinResultCode.RoomFull);
                return;
            }

            int playerId = _nextPlayerId++;
            Seat seat = new Seat();
            seat.Session = session;
            seat.PlayerId = playerId;
            seat.SeatIndex = AllocateSeat();
            seat.Name = NormalizeName(name, playerId);
            _seats.Add(seat);

            session.Send(new JoinResultMsg
            {
                Code = JoinResultCode.Ok,
                RoomId = RoomId,
                PlayerId = playerId,
                Seat = seat.SeatIndex,
            }.ToPacket());
            Log.Info("房间 " + RoomId + " +" + seat.Name + " seat=" + seat.SeatIndex);
            Broadcast(BuildRoomInfo().ToPacket());
        }

        public void HandleReady(ClientSession session)
        {
            if (_state != RoomState.Waiting)
                return;

            Seat? seat = Find(session);
            if (seat == null || seat.Ready)
                return;

            seat.Ready = true;
            Broadcast(BuildRoomInfo().ToPacket());
            TryStart();
        }

        public void HandleInput(ClientSession session, PlayerInput input)
        {
            if (_state != RoomState.Playing)
                return;

            Seat? seat = Find(session);
            if (seat == null)
                return;

            input.MoveX = Axis(input.MoveX);
            input.MoveY = Axis(input.MoveY);
            seat.Input = input;
        }

        public void HandleChase(ClientSession session, uint fromFrame)
        {
            if (Find(session) == null || _state == RoomState.Waiting)
                return;

            uint oldest = _historyOrder.Count > 0 ? _historyOrder.Peek() : _frame;
            bool missed = fromFrame < oldest;
            uint cursor = missed ? oldest : fromFrame;
            List<FrameMsg> frames = new List<FrameMsg>();
            while (cursor < _frame && frames.Count < ServerConfig.ChaseBatchSize)
            {
                PlayerInput[]? inputs;
                if (!_history.TryGetValue(cursor, out inputs) || inputs == null)
                    break;
                frames.Add(new FrameMsg { Frame = cursor, Inputs = inputs });
                cursor++;
            }

            session.Send(new FrameBatchMsg
            {
                Missed = missed,
                OldestFrame = oldest,
                Frames = frames.ToArray(),
            }.ToPacket());
        }

        public void HandleChecksum(ClientSession session, uint frame, uint hash)
        {
            Seat? seat = Find(session);
            if (seat == null || frame == 0 || frame >= _frame)
                return;
            if (!_history.ContainsKey(frame))
                return;

            bool[]? seen;
            uint[] hashes;
            if (!_hashSeen.TryGetValue(frame, out seen) || seen == null)
            {
                seen = new bool[ServerConfig.MaxPlayers];
                hashes = new uint[ServerConfig.MaxPlayers];
                _hashSeen[frame] = seen;
                _hashes[frame] = hashes;
            }
            else
            {
                hashes = _hashes[frame];
            }

            int seatIndex = seat.SeatIndex;
            if (seen[seatIndex])
                return;

            seen[seatIndex] = true;
            hashes[seatIndex] = hash;
            if (_desyncSent.Contains(frame))
                return;

            int first = -1;
            for (int i = 0; i < seen.Length; i++)
            {
                if (!seen[i])
                    continue;
                if (first < 0)
                {
                    first = i;
                    continue;
                }

                if (hashes[i] == hashes[first])
                    continue;

                _desyncSent.Add(frame);
                Seat? seatA = FindSeat(first);
                Seat? seatB = FindSeat(i);
                Broadcast(new DesyncMsg
                {
                    Frame = frame,
                    PlayerIdA = seatA != null ? seatA.PlayerId : 0,
                    HashA = hashes[first],
                    PlayerIdB = seatB != null ? seatB.PlayerId : 0,
                    HashB = hashes[i],
                }.ToPacket());
                return;
            }
        }

        public void HandleLeave(ClientSession session)
        {
            HandleDisconnect(session);
        }

        public void HandleDisconnect(ClientSession session)
        {
            if (!_running)
                return;

            Seat? seat = Find(session);
            if (seat == null)
            {
                if (session.Room == this)
                    session.Room = null;
                return;
            }

            if (_state == RoomState.Playing)
            {
                Log.Info("房间 " + RoomId + " 掉线 " + seat.Name);
                Shutdown(GameOverReason.PlayerLeft, true);
                return;
            }

            _seats.Remove(seat);
            if (session.Room == this)
                session.Room = null;
            Log.Info("房间 " + RoomId + " -" + seat.Name);
            if (_seats.Count == 0)
                Shutdown(GameOverReason.RoomClosed, false);
            else
                Broadcast(BuildRoomInfo().ToPacket());
        }

        private void TryStart()
        {
            if (_state != RoomState.Waiting || _seats.Count < ServerConfig.MinPlayersToStart)
                return;

            for (int i = 0; i < _seats.Count; i++)
            {
                if (!_seats[i].Ready)
                    return;
            }

            _state = RoomState.Playing;
            _randomSeed = (uint)Random.Shared.Next(1, int.MaxValue);
            _frame = 1;
            Log.Info("房间 " + RoomId + " 开始 seed=" + _randomSeed);
            Broadcast(BuildRoomInfo().ToPacket());
            Broadcast(BuildGameStart().ToPacket());
        }

        private void CloseFrame()
        {
            if (_state != RoomState.Playing)
                return;

            PlayerInput[] inputs = new PlayerInput[ServerConfig.MaxPlayers];
            for (int i = 0; i < _seats.Count; i++)
            {
                Seat seat = _seats[i];
                inputs[seat.SeatIndex] = seat.Input;
            }

            _history[_frame] = inputs;
            _historyOrder.Enqueue(_frame);
            while (_historyOrder.Count > ServerConfig.FrameHistory)
            {
                uint old = _historyOrder.Dequeue();
                _history.Remove(old);
                _hashes.Remove(old);
                _hashSeen.Remove(old);
                _desyncSent.Remove(old);
            }

            Broadcast(new FrameMsg { Frame = _frame, Inputs = inputs }.ToPacket());
            _frame++;
        }

        private void Shutdown(GameOverReason reason, bool notify)
        {
            if (!_running)
                return;

            _running = false;
            _state = RoomState.Over;
            if (notify)
            {
                uint lastFrame = _frame > 0 ? _frame - 1 : 0;
                Broadcast(new GameOverMsg { Reason = reason, LastFrame = lastFrame }.ToPacket());
            }

            for (int i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].Session.Room == this)
                    _seats[i].Session.Room = null;
            }

            _seats.Clear();
            _manager.Remove(this);
        }

        private async Task RunAsync()
        {
            using (PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / ServerConfig.LogicFps)))
            {
                ChannelReader<Action> reader = _inbox.Reader;
                Task<bool> readTask = reader.WaitToReadAsync().AsTask();
                Task<bool> tickTask = timer.WaitForNextTickAsync().AsTask();
                try
                {
                    while (_running)
                    {
                        await Task.WhenAny(readTask, tickTask).ConfigureAwait(false);
                        if (readTask.IsCompleted)
                        {
                            if (!readTask.Result)
                                break;

                            Action? action;
                            while (_running && reader.TryRead(out action))
                            {
                                if (action == null)
                                    continue;
                                try
                                {
                                    action();
                                }
                                catch (Exception exception)
                                {
                                    Log.Info("房间 " + RoomId + " 处理失败: " + exception.Message);
                                }
                            }

                            if (!_running)
                                break;
                            readTask = reader.WaitToReadAsync().AsTask();
                        }

                        if (tickTask.IsCompleted)
                        {
                            if (_state == RoomState.Playing)
                                CloseFrame();
                            tickTask = timer.WaitForNextTickAsync().AsTask();
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Info("房间 " + RoomId + " 结束: " + exception.Message);
                }
            }

            _inbox.Writer.TryComplete();
        }

        private RoomInfoMsg BuildRoomInfo()
        {
            return new RoomInfoMsg
            {
                RoomId = RoomId,
                State = _state,
                Players = BuildSlots(),
            };
        }

        private GameStartMsg BuildGameStart()
        {
            return new GameStartMsg
            {
                RoomId = RoomId,
                RandomSeed = _randomSeed,
                LogicFps = (ushort)ServerConfig.LogicFps,
                StartFrame = 1,
                Players = BuildSlots(),
            };
        }

        private PlayerSlot[] BuildSlots()
        {
            Seat[] ordered = _seats.ToArray();
            Array.Sort(ordered, (a, b) => a.SeatIndex.CompareTo(b.SeatIndex));
            PlayerSlot[] slots = new PlayerSlot[ordered.Length];
            for (int i = 0; i < ordered.Length; i++)
            {
                Seat seat = ordered[i];
                slots[i] = new PlayerSlot
                {
                    PlayerId = seat.PlayerId,
                    Seat = seat.SeatIndex,
                    Ready = seat.Ready,
                    Connected = true,
                    Name = seat.Name,
                };
            }

            return slots;
        }

        private void Broadcast(byte[] packet)
        {
            for (int i = 0; i < _seats.Count; i++)
                _seats[i].Session.Send(packet);
        }

        private void Reject(ClientSession session, JoinResultCode code)
        {
            if (session.Room == this)
                session.Room = null;
            session.Send(new JoinResultMsg
            {
                Code = code,
                RoomId = RoomId,
            }.ToPacket());
        }

        private Seat? Find(ClientSession session)
        {
            for (int i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].Session == session)
                    return _seats[i];
            }

            return null;
        }

        private Seat? FindSeat(int seatIndex)
        {
            for (int i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].SeatIndex == seatIndex)
                    return _seats[i];
            }

            return null;
        }

        private byte AllocateSeat()
        {
            bool[] used = new bool[ServerConfig.MaxPlayers];
            for (int i = 0; i < _seats.Count; i++)
                used[_seats[i].SeatIndex] = true;
            for (byte i = 0; i < used.Length; i++)
            {
                if (!used[i])
                    return i;
            }

            return 0;
        }

        private static sbyte Axis(sbyte value)
        {
            if (value < 0)
                return -1;
            if (value > 0)
                return 1;
            return 0;
        }

        private static string NormalizeName(string name, int playerId)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "P" + playerId;
            name = name.Trim();
            if (name.Length > ServerConfig.MaxNameChars)
                name = name.Substring(0, ServerConfig.MaxNameChars);
            return name;
        }

        private sealed class Seat
        {
            public ClientSession Session = null!;
            public int PlayerId;
            public byte SeatIndex;
            public string Name = "";
            public bool Ready;
            public PlayerInput Input;
        }
    }
}
