using System;
using System.Collections.Generic;
using FrameSync.Protocol;
using UnityEngine;

namespace FrameSync
{
    /// <summary>
    /// 按服务器逻辑帧率，每次取出一帧交给玩法。缓冲空了就停，等下一帧，不能本地空跑。
    /// </summary>
    public sealed class LogicFramePump : MonoBehaviour
    {
        public FrameSyncSession session;
        public event Action<FrameMsg> LogicTick;

        private readonly Queue<FrameMsg> _queue = new Queue<FrameMsg>();
        private readonly Dictionary<uint, FrameMsg> _early = new Dictionary<uint, FrameMsg>();
        private float _accumulator;
        private float _interval = 1f / 30f;
        private uint _expected = 1;
        private bool _running;
        private bool _chasePending;

        private void Awake()
        {
            if (session == null)
                session = GetComponent<FrameSyncSession>();
        }

        private void OnEnable()
        {
            if (session == null)
                return;
            session.GameStarted += OnGameStarted;
            session.FrameReceived += OnFrame;
            session.GameOver += OnGameOver;
        }

        private void OnDisable()
        {
            if (session == null)
                return;
            session.GameStarted -= OnGameStarted;
            session.FrameReceived -= OnFrame;
            session.GameOver -= OnGameOver;
        }

        private void OnGameStarted(GameStartMsg message)
        {
            _queue.Clear();
            _early.Clear();
            _expected = message.StartFrame;
            _interval = message.LogicFps > 0 ? 1f / message.LogicFps : 1f / 30f;
            _accumulator = 0f;
            _running = true;
            _chasePending = false;
        }

        private void OnGameOver(GameOverMsg message)
        {
            _running = false;
        }

        private void OnFrame(FrameMsg message)
        {
            if (!_running)
                return;

            if (message.Frame < _expected)
                return;

            if (message.Frame > _expected)
            {
                _early[message.Frame] = message;
                if (!_chasePending && session != null)
                {
                    _chasePending = true;
                    session.RequestChase(_expected);
                }
                return;
            }

            _queue.Enqueue(message);
            _expected++;
            _chasePending = false;
            PullEarlyFrames();
        }

        private void PullEarlyFrames()
        {
            FrameMsg next;
            while (_early.TryGetValue(_expected, out next))
            {
                _early.Remove(_expected);
                _queue.Enqueue(next);
                _expected++;
            }
        }

        private void Update()
        {
            if (!_running)
                return;

            _accumulator += Time.unscaledDeltaTime;
            if (_accumulator > _interval * 5f)
                _accumulator = _interval * 5f;

            int steps = 0;
            while (_accumulator >= _interval && steps < 5)
            {
                if (_queue.Count == 0)
                    break;

                _accumulator -= _interval;
                FrameMsg frame = _queue.Dequeue();
                Action<FrameMsg> tick = LogicTick;
                if (tick != null)
                    tick(frame);
                steps++;
            }
        }
    }
}
