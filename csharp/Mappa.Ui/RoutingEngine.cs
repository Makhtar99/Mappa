using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Mappa;

namespace Mappa.Ui
{
    /// <summary>
    /// Le coeur "temps reel" de la Personne A. Tourne sur un thread dedie a
    /// ~40 Hz, totalement decouple de l'UI (exigence P2) :
    ///
    ///   1. (optionnel) un Faker remplit le State (debug/demo, exigence P8) ;
    ///   2. RoutingPlan.Render(State) projette le State sur les paquets DMX ;
    ///   3. (optionnel) ArtNetSender.SendPlan envoie chaque univers en UDP ;
    ///   4. un snapshot des octets DMX est publie pour que l'UI le dessine.
    ///
    /// L'UI ne touche JAMAIS au State ni au plan directement : elle lit une copie
    /// via CopySnapshot(). Aucune allocation dans la boucle chaude (buffers
    /// reutilises), pour tenir le budget de 25 ms/frame avec une marge confortable.
    /// </summary>
    public sealed class RoutingEngine : IDisposable
    {
        private const int Stride = Config.DmxChannelsPerUniverse; // 512 octets / univers

        private readonly object _lock = new object();

        private Config _config = null!;
        private State _state = null!;
        private RoutingPlan _plan = null!;
        private int[] _universes = Array.Empty<int>();
        private byte[] _snapshot = Array.Empty<byte>(); // [univers * 512] a plat

        private ArtNetSender? _sender;
        private Thread? _thread;
        private volatile bool _running;

        /// <summary>Active l'emission ArtNet reelle (UDP) vers les controleurs.</summary>
        public volatile bool SendArtNet;

        /// <summary>Generateur de signal de test (null = on route le State tel quel).</summary>
        public IFaker? Faker { get; set; }

        // --- Statistiques (lues par l'UI) ---
        public double Fps { get; private set; }
        public long PacketsSent { get; private set; }
        public bool IsRunning => _running;
        public int UniverseCount { get { lock (_lock) { return _universes.Length; } } }
        public int EntityCount { get { lock (_lock) { return _state.Count; } } }
        public string ConfigName { get { lock (_lock) { return _config.Name; } } }

        public RoutingEngine(Config config) => SetConfig(config);

        /// <summary>
        /// (Re)construit l'etat interne pour une nouvelle config (hot reload, P1/P4).
        /// Peut etre appele pendant que la boucle tourne : l'echange est atomique.
        /// </summary>
        public void SetConfig(Config config)
        {
            var state = State.FromConfig(config);
            var plan = new RoutingPlan(config);
            var universes = new List<int>(plan.Universes).ToArray();
            var snapshot = new byte[universes.Length * Stride];
            lock (_lock)
            {
                _config = config;
                _state = state;
                _plan = plan;
                _universes = universes;
                _snapshot = snapshot;
            }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Mappa-Routing" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(500);
            _thread = null;
            _sender?.Dispose();
            _sender = null;
        }

        private void Loop()
        {
            const double frameMs = 1000.0 / 40.0; // 25 ms
            var clock = Stopwatch.StartNew();
            double nextTick = clock.Elapsed.TotalMilliseconds;

            var fpsClock = Stopwatch.StartNew();
            int frames = 0;

            while (_running)
            {
                // Snapshot atomique des references (protege du hot reload).
                State state; RoutingPlan plan; Config config; int[] universes; byte[] snapshot;
                lock (_lock)
                {
                    state = _state; plan = _plan; config = _config;
                    universes = _universes; snapshot = _snapshot;
                }

                Faker?.Fill(state);

                IReadOnlyDictionary<int, byte[]> packets = plan.Render(state);

                if (SendArtNet)
                {
                    _sender ??= new ArtNetSender();
                    _sender.SendPlan(config, packets);
                    PacketsSent += packets.Count;
                }

                // Publie le snapshot pour l'UI (copie sous verrou, ~65 Ko max).
                lock (_lock)
                {
                    if (snapshot.Length == _snapshot.Length) // pas de reconfig entre-temps
                    {
                        for (int i = 0; i < universes.Length; i++)
                        {
                            if (packets.TryGetValue(universes[i], out var pkt))
                                Buffer.BlockCopy(pkt, 0, snapshot, i * Stride, Stride);
                        }
                    }
                }

                // --- Compteur FPS (fenetre glissante 0,5 s) ---
                frames++;
                if (fpsClock.Elapsed.TotalMilliseconds >= 500)
                {
                    Fps = frames * 1000.0 / fpsClock.Elapsed.TotalMilliseconds;
                    frames = 0;
                    fpsClock.Restart();
                }

                // --- Cadencement 40 Hz ---
                nextTick += frameMs;
                double now = clock.Elapsed.TotalMilliseconds;
                double sleep = nextTick - now;
                if (sleep > 1.0) Thread.Sleep((int)sleep);
                else if (sleep < -frameMs) nextTick = now; // on a decroche : on resynchronise
            }
        }

        /// <summary>
        /// Copie le dernier snapshot DMX publie (octets a plat, 512/univers) dans
        /// <paramref name="dest"/> (redimensionne si besoin). Appele par l'UI.
        /// </summary>
        public void CopySnapshot(ref byte[] dest, out int stride, out int[] universes)
        {
            lock (_lock)
            {
                stride = Stride;
                universes = _universes;
                if (dest.Length != _snapshot.Length) dest = new byte[_snapshot.Length];
                Buffer.BlockCopy(_snapshot, 0, dest, 0, _snapshot.Length);
            }
        }

        public void Dispose() => Stop();
    }
}
