using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Mappa
{
    public sealed class EhubReceiver : IDisposable
    {
        public sealed class PacketSnapshot
        {
            public DateTimeOffset ReceivedAt { get; set; }
            public IPEndPoint RemoteEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 0);
            public byte Type { get; set; }
            public byte Universe { get; set; }
            public ushort Count { get; set; }
            public int[] EntityIds { get; set; } = Array.Empty<int>();
            public uint[] PackedColors { get; set; } = Array.Empty<uint>();

            public string Key => $"{ReceivedAt.UtcTicks}|{RemoteEndPoint}|{Type}|{Universe}|{Count}";

            public override string ToString()
                => $"{ReceivedAt:HH:mm:ss}  {RemoteEndPoint.Address}:{RemoteEndPoint.Port}  u{Universe}  x{Count}";

            public State ToState()
            {
                var state = new State(EntityIds);
                for (int i = 0; i < EntityIds.Length && i < PackedColors.Length; i++)
                {
                    uint c = PackedColors[i];
                    state.Set(EntityIds[i], (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
                }
                state.MarkUpdated();
                return state;
            }
        }

        private readonly UdpClient _udp;
        private readonly Thread _thread;
        private volatile bool _running;
        private readonly ConcurrentDictionary<int, uint> _colors = new ConcurrentDictionary<int, uint>();
        private readonly ConcurrentQueue<PacketSnapshot> _packets = new ConcurrentQueue<PacketSnapshot>();

        private const int MaxPackets = 256;

        public int Port { get; }
        public long PacketsReceived { get; private set; }

        public EhubReceiver(int port = Ehub.DefaultUdpPort)
        {
            _udp = new UdpClient(port);
            Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
            _udp.Client.ReceiveTimeout = 300;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "ehub-rx" };
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

                Ehub.Message msg;
                try { msg = Ehub.Decode(data); }
                catch { continue; }

                if (msg.Type != Ehub.TypeUpdate) continue;

                var p = msg.Payload;
                var ids = new List<int>(msg.Count);
                var colors = new List<uint>(msg.Count);
                for (int i = 0; i + Ehub.SextuorSize <= p.Length; i += Ehub.SextuorSize)
                {
                    int id = Ehub.ReadU16(p, i);
                    uint packed = (uint)((p[i + 2] << 24) | (p[i + 3] << 16) | (p[i + 4] << 8) | p[i + 5]);
                    _colors[id] = packed;
                    ids.Add(id);
                    colors.Add(packed);
                }
                _packets.Enqueue(new PacketSnapshot
                {
                    ReceivedAt = DateTimeOffset.UtcNow,
                    RemoteEndPoint = new IPEndPoint(remote.Address, remote.Port),
                    Type = msg.Type,
                    Universe = msg.Universe,
                    Count = msg.Count,
                    EntityIds = ids.ToArray(),
                    PackedColors = colors.ToArray(),
                });
                while (_packets.Count > MaxPackets && _packets.TryDequeue(out _)) { }
                PacketsReceived++;
            }
        }

        public IReadOnlyList<PacketSnapshot> SnapshotPackets() => _packets.ToArray();

        public void Fill(State state)
        {
            foreach (var kv in _colors)
            {
                uint c = kv.Value;
                state.Set(kv.Key, (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
            }
            state.MarkUpdated();
        }

        public void Dispose()
        {
            _running = false;
            _udp.Dispose();
            _thread.Join(500);
        }
    }
}
