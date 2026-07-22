using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Mappa;
using Xunit;

namespace Mappa.Tests
{
    public class EhubBridgeTests
    {
        [Fact]
        public void EhubReceiver_ReceivesUpdate_FillsState()
        {
            using var rx = new EhubReceiver(0);
            int port = rx.Port;

            var ids = new List<int> { 100, 101, 102 };
            var src = new State(ids);
            src.Set(100, 255, 0, 0);
            src.Set(101, 0, 255, 0);
            src.Set(102, 0, 0, 255, 64);

            byte[] msg = Ehub.EncodeUpdate(0, ids, src);
            using (var udp = new UdpClient())
                udp.Send(msg, msg.Length, "127.0.0.1", port);

            for (int i = 0; i < 100 && rx.PacketsReceived == 0; i++)
                Thread.Sleep(10);

            Assert.True(rx.PacketsReceived > 0);

            var dst = new State(ids);
            rx.Fill(dst);

            Assert.Equal(255, dst.Get(100).R);
            Assert.Equal(255, dst.Get(101).G);
            Assert.Equal(255, dst.Get(102).B);
            Assert.Equal(64, dst.Get(102).W);
        }

        // ------------------------------------------------------------------
        // Reproduit EXACTEMENT le format binaire produit par DeviceEmitter.cs
        // (assets/Scripts/DeviceEmitter.cs) : c'est le codeur cote Unity. On
        // s'assure que ce que Unity envoie est bien decode par EhubReceiver.
        // ------------------------------------------------------------------
        private static byte[] BuildDeviceEmitterPacket(byte universe, Dictionary<int, (byte r, byte g, byte b, byte w)> data)
        {
            var ids = new List<int>(data.Keys);
            ids.Sort();
            var payload = new byte[ids.Count * 6];
            int p = 0;
            foreach (int id in ids)
            {
                var c = data[id];
                payload[p    ] = (byte)(id & 0xFF);
                payload[p + 1] = (byte)((id >> 8) & 0xFF);
                payload[p + 2] = c.r;
                payload[p + 3] = c.g;
                payload[p + 4] = c.b;
                payload[p + 5] = c.w;
                p += 6;
            }
            byte[] gz;
            using (var ms = new MemoryStream())
            {
                using (var g = new GZipStream(ms, CompressionMode.Compress, true))
                    g.Write(payload, 0, payload.Length);
                gz = ms.ToArray();
            }
            var msg = new byte[10 + gz.Length];
            msg[0] = (byte)'e'; msg[1] = (byte)'H'; msg[2] = (byte)'u'; msg[3] = (byte)'B';
            msg[4] = 2;                        // TypeUpdate
            msg[5] = universe;
            msg[6] = (byte)(ids.Count & 0xFF);
            msg[7] = (byte)((ids.Count >> 8) & 0xFF);
            msg[8] = (byte)(gz.Length & 0xFF);
            msg[9] = (byte)((gz.Length >> 8) & 0xFF);
            Array.Copy(gz, 0, msg, 10, gz.Length);
            return msg;
        }

        [Fact]
        public void DeviceEmitterFormat_IsDecodedByEhubReceiver()
        {
            // Verifie que le paquet binaire construit par DeviceEmitter.cs (Unity)
            // est bel et bien decode par EhubReceiver. Aucun ajustement de format
            // n'est possible cote reception : si ce test passe, la compatibilite
            // Unity <-> Mappa.Ui est prouvee au niveau octet.
            using var rx = new EhubReceiver(0);
            int port = rx.Port;

            var data = new Dictionary<int, (byte, byte, byte, byte)>
            {
                { 10, (200, 0, 0, 0) },   // canal 1 Lyre 1 : pan_hi
                { 11, (255, 0, 0, 0) },   // canal 2 Lyre 1 : pan_lo
                { 15, (128, 0, 0, 0) },   // canal 6 Lyre 1 : dimmer 50%
                { 1,  (77,  0, 0, 0) },   // Projecteur : dimmer 30%
            };
            byte[] msg = BuildDeviceEmitterPacket(33, data);
            using (var udp = new UdpClient())
                udp.Send(msg, msg.Length, "127.0.0.1", port);

            for (int i = 0; i < 100 && rx.PacketsReceived == 0; i++)
                Thread.Sleep(10);
            Assert.True(rx.PacketsReceived > 0, "aucun paquet recu");

            var state = new State(new List<int> { 1, 10, 11, 15 });
            rx.Fill(state);
            Assert.Equal(200, state.Get(10).R);
            Assert.Equal(255, state.Get(11).R);
            Assert.Equal(128, state.Get(15).R);
            Assert.Equal(77,  state.Get(1).R);
        }

        [Fact]
        public void EndToEnd_UnityPacket_ProducesCorrectArtNetPacket()
        {
            // Test bout-en-bout complet, sans dependre du materiel :
            //   1) Un paquet eHuB "format Unity" est envoye en UDP sur loopback.
            //   2) EhubReceiver le decode et remplit un State.
            //   3) RoutingPlan projette le State en buffers DMX par univers.
            //   4) ArtNetSender emet chaque univers en Art-Net (UDP:6454).
            //   5) Un sniffer UDP local recoit le paquet Art-Net.
            //   6) On verifie qu'il contient bien 200 au canal 1 de l'univers 33.
            //
            // C'est exactement ce que le materiel BC216 recevra demain, sauf que
            // la lyre elle-meme (interpretation des canaux) n'est pas testee.

            // 1) Charge la vraie config ecran.json
            string repoRoot = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(EhubBridgeTests).Assembly.Location)!,
                "..", "..", "..", "..", ".."));
            string cfgPath = Path.Combine(repoRoot, "configs", "ecran.json");
            var cfg = Persistence.LoadConfig(cfgPath);

            // Redirige les 4 controleurs vers loopback pour sniffer localement.
            foreach (var c in cfg.Controllers) c.Ip = "127.0.0.1";
            // Utilise un port Art-Net local libre (evite le vrai 6454 qui peut
            // etre occupe par un vrai controleur/logiciel).
            int artnetPort = 6455;
            foreach (var c in cfg.Controllers) c.Port = artnetPort;

            var plan = new RoutingPlan(cfg);
            var state = State.FromConfig(cfg);

            // 2) Le sniffer Art-Net local (avant que ArtNetSender emette).
            using var sniffer = new UdpClient(artnetPort);
            sniffer.Client.ReceiveTimeout = 1500;
            var received = new List<byte[]>();

            // 3) Recepteur eHuB : simule Mappa.Ui.
            using var rx = new EhubReceiver(0);

            // 4) Envoie du paquet "Unity" : pan=200 sur entite 10 (canal DMX 1
            //    de Lyre 1) et dimmer=128 sur entite 15 (canal DMX 6).
            var data = new Dictionary<int, (byte, byte, byte, byte)>
            {
                { 10, (200, 0, 0, 0) },
                { 15, (128, 0, 0, 0) },
                { 1,  (77,  0, 0, 0) },  // projo, canal DMX 169
            };
            byte[] ehubPkt = BuildDeviceEmitterPacket(33, data);
            using (var udpTx = new UdpClient())
                udpTx.Send(ehubPkt, ehubPkt.Length, "127.0.0.1", rx.Port);

            // 5) Attente reception eHuB.
            for (int i = 0; i < 100 && rx.PacketsReceived == 0; i++)
                Thread.Sleep(10);
            Assert.True(rx.PacketsReceived > 0);
            rx.Fill(state);

            // 6) RoutingPlan + ArtNetSender.
            var packets = plan.Render(state);
            using (var sender = new ArtNetSender())
                sender.SendPlan(cfg, packets);

            // 7) On sniffe TOUS les paquets Art-Net emis (129 univers dans la
            //    config), on garde celui qui correspond a l'univers ArtNet 33.
            var stop = DateTime.UtcNow.AddMilliseconds(2000);
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (DateTime.UtcNow < stop)
            {
                try
                {
                    byte[] pkt = sniffer.Receive(ref from);
                    received.Add(pkt);
                }
                catch (SocketException) { break; }
            }
            Assert.NotEmpty(received);

            // Trouve le paquet Art-Net pour l'univers 33.
            byte[]? univ33 = null;
            foreach (var pkt in received)
            {
                if (pkt.Length < 18) continue;
                if (pkt[0] != 'A' || pkt[1] != 'r' || pkt[2] != 't') continue;
                int u = pkt[14] | (pkt[15] << 8);
                if (u == 33) { univ33 = pkt; break; }
            }
            Assert.NotNull(univ33);
            // Le paquet Art-Net a un header de 18 octets. Le DMX commence a l'offset 18.
            // Canal DMX 1 = univ33[18], canal DMX 6 = univ33[23], canal DMX 169 = univ33[186].
            Assert.Equal(200, univ33![18]);   // Lyre 1 canal 1 = pan_hi
            Assert.Equal(128, univ33![23]);   // Lyre 1 canal 6 = dimmer
            Assert.Equal(77,  univ33![186]);  // Projecteur canal 169
        }
    }
}
