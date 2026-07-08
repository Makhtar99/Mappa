using System;
using System.Net;
using System.Net.Sockets;
using Mappa;

namespace Mappa.Authoring.Core
{
    public sealed class EhubSender : IDisposable
    {
        private readonly UdpClient _udp;
        private readonly IPEndPoint _endpoint;

        public EhubSender(string host, int port)
        {
            _udp = new UdpClient { EnableBroadcast = true };
            _endpoint = new IPEndPoint(ResolveAddress(host), port);
        }

        private static IPAddress ResolveAddress(string host)
        {
            if (IPAddress.TryParse(host, out var ip)) return ip;
            var addrs = Dns.GetHostAddresses(host);
            if (addrs.Length == 0) throw new ArgumentException($"Hote introuvable : {host}");
            return addrs[0];
        }

        public void SendRaw(byte[] message) => _udp.Send(message, message.Length, _endpoint);

        public void SendUpdate(EhubChunk chunk, State state)
            => SendRaw(Ehub.EncodeUpdate(chunk.Universe, chunk.Ids, state));

        public void SendConfig(EhubChunk chunk)
            => SendRaw(Ehub.EncodeConfig(chunk.Universe, chunk.Ranges));

        public void Dispose() => _udp.Dispose();
    }
}
