using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Mappa;
using Xunit;

namespace Mappa.Tests
{
    /// <summary>
    /// Prouve le chemin du "faux emitter" (niveau B) : on encode un State en
    /// paquet eHuB Update (comme la commande `emit`), on l'envoie en UDP, et le
    /// noeud 1 (EhubReceiver) doit le recevoir et retrouver les bonnes couleurs.
    /// C'est le test end-to-end de la reception eHuB, sans Unity.
    /// </summary>
    public class EhubEmitReceiveTests
    {
        [Fact]
        public void EmittedFrameIsReceivedWithCorrectColors()
        {
            // Recepteur (noeud 1) sur un port ephemere choisi par l'OS.
            using var receiver = new EhubReceiver(port: 0);

            // Cote emitter : 10 entites toutes en blanc (toutes LED allumees).
            var ids = Enumerable.Range(1, 10).ToList();
            var source = new State(ids);
            foreach (int id in ids) source.SetRgb(id, 255, 255, 255);

            byte[] packet = Ehub.EncodeUpdate(ehubUniverse: 0, ids, source);

            // Envoi UDP en loopback vers le port reel du recepteur.
            using (var udp = new UdpClient())
            {
                var target = new IPEndPoint(IPAddress.Loopback, receiver.Port);
                udp.Send(packet, packet.Length, target);
            }

            // Le recepteur tourne sur un thread : on attend qu'il compte le paquet.
            var deadline = System.DateTime.UtcNow.AddSeconds(2);
            while (receiver.PacketsReceived == 0 && System.DateTime.UtcNow < deadline)
                Thread.Sleep(10);

            Assert.True(receiver.PacketsReceived >= 1, "aucun paquet eHuB recu");

            // Le recepteur reinjecte les couleurs dans un State cote routage.
            var routed = new State(ids);
            receiver.Fill(routed);

            Assert.All(ids, id =>
            {
                var c = routed.Get(id);
                Assert.Equal((byte)255, c.R);
                Assert.Equal((byte)255, c.G);
                Assert.Equal((byte)255, c.B);
            });
        }
    }
}
