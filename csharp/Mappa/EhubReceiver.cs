using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Mappa
{
    public sealed class EhubReceiver : IDisposable
    {
        private readonly UdpClient _udp;
        private readonly Thread _thread;
        private volatile bool _running;
        private readonly ConcurrentDictionary<int, uint> _colors = new ConcurrentDictionary<int, uint>();

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
                for (int i = 0; i + Ehub.SextuorSize <= p.Length; i += Ehub.SextuorSize)
                {
                    int id = Ehub.ReadU16(p, i);
                    uint packed = (uint)((p[i + 2] << 24) | (p[i + 3] << 16) | (p[i + 4] << 8) | p[i + 5]);
                    _colors[id] = packed;
                }
                PacketsReceived++;
            }
        }

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
