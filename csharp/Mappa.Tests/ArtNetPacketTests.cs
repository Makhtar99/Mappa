using Mappa;
using Xunit;

namespace Mappa.Tests
{
    /// <summary>
    /// Verrouille la structure de l'en-tete ArtDMX. Le noeud 4 de la page de
    /// debogage affiche ces 18 octets tels quels : si la construction change,
    /// l'outil de debogage mentirait sur ce qui part reellement sur le reseau.
    /// </summary>
    public class ArtNetPacketTests
    {
        [Fact]
        public void HeaderHasExpectedLayout()
        {
            var dmx = new byte[512];
            byte[] pkt = ArtNetSender.BuildPacket(universe: 0, dmx512: dmx, sequence: 7);

            // "Art-Net\0"
            Assert.Equal((byte)'A', pkt[0]);
            Assert.Equal((byte)'r', pkt[1]);
            Assert.Equal((byte)'t', pkt[2]);
            Assert.Equal((byte)'-', pkt[3]);
            Assert.Equal((byte)'N', pkt[4]);
            Assert.Equal((byte)'e', pkt[5]);
            Assert.Equal((byte)'t', pkt[6]);
            Assert.Equal((byte)0, pkt[7]);

            Assert.Equal(0x00, pkt[8]);   // OpCode ArtDMX = 0x5000, little-endian
            Assert.Equal(0x50, pkt[9]);
            Assert.Equal(0x00, pkt[10]);  // version 14, big-endian
            Assert.Equal(14, pkt[11]);
            Assert.Equal(7, pkt[12]);     // sequence
            Assert.Equal(0, pkt[13]);     // physical

            Assert.Equal(512, (pkt[16] << 8) | pkt[17]);   // longueur, big-endian
            Assert.Equal(18 + 512, pkt.Length);
        }

        /// <summary>
        /// L'univers occupe 15 bits en little-endian : c'est exactement ce que le
        /// noeud 4 affiche en hexadecimal quand on change le champ « Univers ».
        /// </summary>
        [Theory]
        [InlineData(0, 0x00, 0x00)]
        [InlineData(1, 0x01, 0x00)]
        [InlineData(31, 0x1F, 0x00)]
        [InlineData(300, 0x2C, 0x01)]
        public void UniverseIsEncodedAs15BitsLittleEndian(int universe, byte low, byte high)
        {
            byte[] pkt = ArtNetSender.BuildPacket(universe, new byte[512]);
            Assert.Equal(low, pkt[14]);
            Assert.Equal(high, pkt[15]);
        }

        /// <summary>Aller-retour : ce qu'on construit doit etre relisible tel quel.</summary>
        [Fact]
        public void BuiltPacketIsParsedBack()
        {
            var dmx = new byte[512];
            dmx[0] = 255; dmx[1] = 128; dmx[511] = 42;

            byte[] pkt = ArtNetSender.BuildPacket(universe: 12, dmx512: dmx);

            Assert.True(ArtNetReceiver.TryParseArtDmx(pkt, out int universe, out byte[] back));
            Assert.Equal(12, universe);
            Assert.Equal(512, back.Length);
            Assert.Equal((byte)255, back[0]);
            Assert.Equal((byte)128, back[1]);
            Assert.Equal((byte)42, back[511]);
        }

        /// <summary>Un datagramme quelconque ne doit pas etre pris pour de l'ArtNet.</summary>
        [Fact]
        public void NonArtNetDatagramIsRejected()
        {
            Assert.False(ArtNetReceiver.TryParseArtDmx(new byte[] { 1, 2, 3 }, out _, out _));
            var wrongMagic = new byte[32];
            Assert.False(ArtNetReceiver.TryParseArtDmx(wrongMagic, out _, out _));
        }
    }
}
