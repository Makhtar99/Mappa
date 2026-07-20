using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Mappa
{
    /// <summary>
    /// Récepteur ArtNet (débogage P8). Écoute le port ArtNet (6454), décode les
    /// paquets ArtDMX et mémorise, PAR UNIVERS, la dernière trame DMX reçue +
    /// d'où elle vient. Permet de visualiser ce qui circule réellement sur le
    /// réseau (« écouter l'ArtNet et voir ce qui est reçu »).
    /// </summary>
    public sealed class ArtNetReceiver : IDisposable
    {
        /// <summary>État reçu pour un univers ArtNet.</summary>
        public sealed class UniverseSnapshot
        {
            public int Universe { get; set; }
            public byte[] Dmx { get; } = new byte[Config.DmxChannelsPerUniverse];
            public long PacketCount { get; set; }
            public DateTimeOffset LastSeen { get; set; }
            public IPEndPoint From { get; set; } = new IPEndPoint(IPAddress.Any, 0);
        }

        private readonly UdpClient _udp;
        private readonly Thread _thread;
        private volatile bool _running;
        private readonly ConcurrentDictionary<int, UniverseSnapshot> _universes = new ConcurrentDictionary<int, UniverseSnapshot>();

        public int Port { get; }
        public long PacketsReceived { get; private set; }

        public ArtNetReceiver(int port = 6454)
        {
            _udp = new UdpClient(port);
            Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
            _udp.Client.ReceiveTimeout = 300;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "artnet-rx" };
            _thread.Start();
        }

        private void Loop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try { data = _udp.Receive(ref remote); }
                catch (SocketException) { continue; }
                catch (ObjectDisposedException) { break; }

                if (!TryParseArtDmx(data, out int universe, out byte[] dmx)) continue;

                var snap = _universes.GetOrAdd(universe, u => new UniverseSnapshot { Universe = u });
                Array.Clear(snap.Dmx, 0, snap.Dmx.Length);
                Array.Copy(dmx, 0, snap.Dmx, 0, Math.Min(dmx.Length, snap.Dmx.Length));
                snap.PacketCount++;
                snap.LastSeen = DateTimeOffset.Now;
                snap.From = new IPEndPoint(remote.Address, remote.Port);
                PacketsReceived++;
            }
        }

        /// <summary>Décode un paquet ArtDMX. Retourne false si ce n'en est pas un.</summary>
        public static bool TryParseArtDmx(byte[] p, out int universe, out byte[] dmx)
        {
            universe = 0;
            dmx = Array.Empty<byte>();
            if (p.Length < 18) return false;
            // "Art-Net\0"
            if (p[0] != 'A' || p[1] != 'r' || p[2] != 't' || p[3] != '-' ||
                p[4] != 'N' || p[5] != 'e' || p[6] != 't' || p[7] != 0) return false;
            // OpCode ArtDMX = 0x5000 (little-endian)
            if (p[8] != 0x00 || p[9] != 0x50) return false;

            universe = p[14] | ((p[15] & 0x7F) << 8);   // univers 15 bits, LE
            int len = (p[16] << 8) | p[17];              // longueur données, BE
            if (18 + len > p.Length) len = p.Length - 18;
            if (len < 0) return false;
            dmx = new byte[len];
            Array.Copy(p, 18, dmx, 0, len);
            return true;
        }

        /// <summary>Les univers reçus, triés par numéro.</summary>
        public IReadOnlyList<UniverseSnapshot> Snapshot()
            => _universes.Values.OrderBy(u => u.Universe).ToList();

        public void Dispose()
        {
            _running = false;
            try { _udp.Dispose(); } catch { /* déjà fermé */ }
        }
    }
}
