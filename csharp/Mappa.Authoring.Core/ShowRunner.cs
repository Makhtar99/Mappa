using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Mappa;

namespace Mappa.Authoring.Core
{
    public sealed class ShowRunner : IDisposable
    {
        private readonly Show _show;
        private readonly EntityLayout _layout;
        private readonly ShowPlayer _player;
        private readonly State _state;
        private readonly EhubSender _sender;
        private readonly List<EhubChunk> _chunks;

        private Thread? _thread;
        private volatile bool _running;
        private readonly Stopwatch _clock = new Stopwatch();

        public Func<double>? TimeSource;
        public bool Loop = true;

        public int FramesSent { get; private set; }
        public double LastFrameTime { get; private set; }

        public ShowRunner(Show show, EntityLayout layout, EhubSender sender, int maxEntitiesPerMessage = EhubUniversePlan.DefaultMaxEntitiesPerMessage)
        {
            _show = show;
            _layout = layout;
            _sender = sender;
            _player = new ShowPlayer(show, layout);
            _state = new State(layout.Ids);
            _chunks = EhubUniversePlan.Build(layout.Ids, maxEntitiesPerMessage);
        }

        public State State => _state;
        public IReadOnlyList<EhubChunk> Chunks => _chunks;

        public void SendConfig()
        {
            foreach (var chunk in _chunks)
                _sender.SendConfig(chunk);
        }

        public void RenderAndSend(double t)
        {
            _player.RenderAt(t, _state);
            foreach (var chunk in _chunks)
                _sender.SendUpdate(chunk, _state);
            LastFrameTime = t;
            FramesSent++;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _clock.Restart();
            _thread = new Thread(Loop_) { IsBackground = true, Name = "ehub-runner" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(500);
            _thread = null;
        }

        private double Now() => TimeSource?.Invoke() ?? _clock.Elapsed.TotalSeconds;

        private void Loop_()
        {
            double period = 1.0 / (_show.Fps > 0 ? _show.Fps : 40.0);
            SendConfig();
            double lastConfig = Now();
            var sw = new Stopwatch();

            while (_running)
            {
                sw.Restart();
                double t = Now();
                if (Loop && _show.Duration > 0)
                    t %= _show.Duration;

                RenderAndSend(t);

                if (Now() - lastConfig >= 1.0)
                {
                    SendConfig();
                    lastConfig = Now();
                }

                double sleep = period - sw.Elapsed.TotalSeconds;
                if (sleep > 0)
                    Thread.Sleep((int)(sleep * 1000));
            }
        }

        public void Dispose() => Stop();
    }
}
