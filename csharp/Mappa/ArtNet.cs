using System;
using System.Net;
using System.Net.Sockets;

namespace Mappa
{
    /// <summary>
    /// Emetteur ArtNet minimal (UDP). C'est le domaine de la Personne A ; on
    /// fournit ici une reference fonctionnelle pour debloquer le jalon
    /// "allumer 1 LED reelle" et valider bout-en-bout que le RoutingPlan produit
    /// les bons octets.
    ///
    /// Encapsule un univers DMX512 (512 octets) dans un paquet ArtDMX conforme
    /// (en-tete "Art-Net\0", OpCode 0x5000, univers 15 bits) et l'envoie en UDP
    /// au port ArtNet (6454) du controleur cible.
    /// </summary>
    public sealed class ArtNetSender : IDisposable
    {
        private readonly UdpClient _udp;
        private byte _sequence;

        public ArtNetSender()
        {
            _udp = new UdpClient();
            // Autorise l'envoi en broadcast (255.255.255.255 ou x.x.x.255) :
            // certains controleurs ArtNet n'ecoutent qu'en broadcast.
            _udp.EnableBroadcast = true;
        }

        /// <summary>Construit un paquet ArtDMX pret a envoyer.</summary>
        public static byte[] BuildPacket(int universe, byte[] dmx512, byte sequence = 0)
        {
            if (dmx512.Length > Config.DmxChannelsPerUniverse)
            {
                throw new ArgumentException(
                    $"DMX > {Config.DmxChannelsPerUniverse} octets ({dmx512.Length}).");
            }
            // Longueur des donnees : ArtNet exige un nombre pair, >= 2, <= 512.
            int dataLen = Math.Max(2, dmx512.Length);
            if (dataLen % 2 != 0) dataLen++;

            var pkt = new byte[18 + dataLen];
            // "Art-Net\0"
            pkt[0] = (byte)'A'; pkt[1] = (byte)'r'; pkt[2] = (byte)'t';
            pkt[3] = (byte)'-'; pkt[4] = (byte)'N'; pkt[5] = (byte)'e';
            pkt[6] = (byte)'t'; pkt[7] = 0;
            // OpCode ArtDMX = 0x5000 (little-endian)
            pkt[8] = 0x00; pkt[9] = 0x50;
            // Version protocole = 14 (big-endian)
            pkt[10] = 0x00; pkt[11] = 14;
            // Sequence, Physical
            pkt[12] = sequence; pkt[13] = 0;
            // Univers (15 bits, little-endian)
            pkt[14] = (byte)(universe & 0xFF);
            pkt[15] = (byte)((universe >> 8) & 0x7F);
            // Longueur des donnees (big-endian)
            pkt[16] = (byte)((dataLen >> 8) & 0xFF);
            pkt[17] = (byte)(dataLen & 0xFF);
            Array.Copy(dmx512, 0, pkt, 18, dmx512.Length);
            return pkt;
        }

        /// <summary>Envoie un univers a une IP:port (6454 par defaut).</summary>
        public void Send(string ip, int universe, byte[] dmx512, int port = 6454)
        {
            var pkt = BuildPacket(universe, dmx512, _sequence);
            _sequence = (byte)(_sequence == 255 ? 1 : _sequence + 1);
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
            try
            {
                _udp.Send(pkt, pkt.Length, endpoint);
            }
            catch (SocketException)
            {
                // Controleur injoignable (hors reseau, eteint...) : on ignore
                // cet univers pour cette frame plutot que de tuer le routage.
            }
        }

        /// <summary>
        /// Envoie tous les univers d'un RoutingPlan rendu, chacun a l'IP du
        /// controleur qui le pilote (selon la Config). C'est la boucle type que
        /// la Personne A executera a ~40 Hz.
        /// </summary>
        public void SendPlan(Config config, System.Collections.Generic.IReadOnlyDictionary<int, byte[]> packets)
        {
            foreach (var kv in packets)
            {
                var universe = Failover.UniverseOf(config, kv.Key);
                if (universe == null) continue; // univers non rattache
                var controller = config.Controllers.Find(c => c.Id == universe.ControllerId);
                if (controller == null) continue;
                // On envoie le numero d'univers ArtNet LOCAL du controleur (0..31),
                // pas l'index global unique interne a la config.
                Send(controller.Ip, universe.EffectiveArtNetUniverse, kv.Value, controller.Port);
            }
        }

        public void Dispose() => _udp.Dispose();
    }
}
