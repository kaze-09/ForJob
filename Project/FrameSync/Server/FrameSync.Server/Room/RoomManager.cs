using System;
using System.Collections.Generic;
using FrameSync.Protocol;

namespace FrameSync.Server
{
    internal sealed class RoomManager : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<int, GameRoom> _rooms = new Dictionary<int, GameRoom>();
        private int _nextRoomId = 1000;

        public void OnJoin(ClientSession session, int roomId, string name)
        {
            if (session.Room != null)
            {
                session.Send(new JoinResultMsg { Code = JoinResultCode.AlreadyInRoom }.ToPacket());
                return;
            }

            if (roomId < 0)
            {
                session.Send(new JoinResultMsg { Code = JoinResultCode.RoomNotFound, RoomId = roomId }.ToPacket());
                return;
            }

            GameRoom? room;
            lock (_gate)
            {
                if (roomId == 0)
                {
                    room = CreateRoom();
                }
                else if (!_rooms.TryGetValue(roomId, out room) || room == null)
                {
                    session.Send(new JoinResultMsg { Code = JoinResultCode.RoomNotFound, RoomId = roomId }.ToPacket());
                    return;
                }

                session.Room = room;
            }

            if (!room.Enqueue(() => room.HandleJoin(session, name)))
            {
                if (session.Room == room)
                    session.Room = null;
                session.Send(new JoinResultMsg { Code = JoinResultCode.AlreadyStarted, RoomId = room.RoomId }.ToPacket());
            }
        }

        public void OnReady(ClientSession session)
        {
            Post(session, room => room.HandleReady(session));
        }

        public void OnInput(ClientSession session, PlayerInput input)
        {
            Post(session, room => room.HandleInput(session, input));
        }

        public void OnChase(ClientSession session, uint fromFrame)
        {
            Post(session, room => room.HandleChase(session, fromFrame));
        }

        public void OnChecksum(ClientSession session, uint frame, uint hash)
        {
            Post(session, room => room.HandleChecksum(session, frame, hash));
        }

        public void OnLeave(ClientSession session)
        {
            Post(session, room => room.HandleLeave(session));
        }

        public void OnDisconnect(ClientSession session)
        {
            Post(session, room => room.HandleDisconnect(session));
        }

        public void Remove(GameRoom room)
        {
            lock (_gate)
            {
                if (_rooms.TryGetValue(room.RoomId, out GameRoom? current) && current == room)
                    _rooms.Remove(room.RoomId);
            }

            Log.Info("关闭房间 " + room.RoomId);
        }

        public void Dispose()
        {
            GameRoom[] rooms;
            lock (_gate)
            {
                rooms = new GameRoom[_rooms.Count];
                _rooms.Values.CopyTo(rooms, 0);
            }

            for (int i = 0; i < rooms.Length; i++)
                rooms[i].Enqueue(rooms[i].ForceClose);
        }

        private void Post(ClientSession session, Action<GameRoom> action)
        {
            GameRoom? room = session.Room;
            if (room == null)
                return;
            room.Enqueue(() => action(room));
        }

        private GameRoom CreateRoom()
        {
            int id = _nextRoomId++;
            GameRoom room = new GameRoom(id, this);
            _rooms.Add(id, room);
            room.Start();
            Log.Info("创建房间 " + id);
            return room;
        }
    }
}
