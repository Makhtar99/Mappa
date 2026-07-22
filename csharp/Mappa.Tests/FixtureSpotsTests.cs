using System;
using System.IO;
using System.Linq;
using Mappa;
using Xunit;

namespace Mappa.Tests
{
    public class FixtureSpotsTests
    {
        private static string EcranConfigPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "configs", "ecran.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("configs/ecran.json introuvable depuis " + AppContext.BaseDirectory);
        }

        [Theory]
        [InlineData(1, 168)]
        [InlineData(10, 0)]
        [InlineData(11, 3)]
        [InlineData(23, 39)]
        [InlineData(30, 42)]
        [InlineData(50, 84)]
        [InlineData(70, 126)]
        [InlineData(83, 165)]
        [InlineData(100, -1)]
        public void DmxChannelOf_MatchesEcranXlsx(int entityId, int expected)
        {
            Assert.Equal(expected, FixtureAddressing.DmxChannelOf(entityId));
        }

        [Fact]
        public void LyrePacking_DimmerLandsOnDmxChannel5()
        {
            var ch = new byte[13];
            FixtureAddressing.FillLyreChannels(
                ch, pan: 0.5f, tilt: 0.5f, speed: 0f, dimmer: 1f, strobe: 0f,
                r: 255, g: 0, b: 0, white: 0);

            Assert.Equal(255, ch[5]);
            Assert.Equal(255, ch[7]);

            var dmx = new byte[512];
            FixtureAddressing.WriteLyreToDmx(dmx, baseEntityId: 10, ch);

            Assert.Equal(255, dmx[5]);
            Assert.Equal(255, dmx[7]);
            Assert.Equal(0, dmx[8]);
        }

        [Fact]
        public void LyrePacking_PanHighChangesWhenPanMoves()
        {
            var a = new byte[13];
            var b = new byte[13];
            FixtureAddressing.FillLyreChannels(a, 0f, 0.5f, 0, 1, 0, 0, 0, 0, 0);
            FixtureAddressing.FillLyreChannels(b, 1f, 0.5f, 0, 1, 0, 0, 0, 0, 0);

            Assert.NotEqual(a[0], b[0]);
            Assert.Equal(0, a[0]);
            Assert.Equal(255, b[0]);
        }

        [Fact]
        public void Projector_WritesRgbAtChannel168()
        {
            var dmx = new byte[512];
            FixtureAddressing.WriteProjectorToDmx(dmx, 10, 20, 30);
            Assert.Equal(10, dmx[168]);
            Assert.Equal(20, dmx[169]);
            Assert.Equal(30, dmx[170]);
            Assert.Equal(0, dmx[0]);
        }

        [Fact]
        public void EcranConfig_RoutesFixtureEntitiesLikeExcel()
        {
            var cfg = Persistence.LoadConfig(EcranConfigPath());
            var plan = new RoutingPlan(cfg);

            Assert.NotNull(plan.AddressOf(1));
            Assert.Equal(168, plan.AddressOf(1)!.Value.Channel);
            Assert.Equal(3, plan.AddressOf(1)!.Value.Channels);

            Assert.Equal(0, plan.AddressOf(10)!.Value.Channel);
            Assert.Equal(3, plan.AddressOf(11)!.Value.Channel);
            Assert.Equal(42, plan.AddressOf(30)!.Value.Channel);
        }

        [Fact]
        public void EcranConfig_RenderLyreMatchesDirectPacking()
        {
            var cfg = Persistence.LoadConfig(EcranConfigPath());
            var plan = new RoutingPlan(cfg);
            var state = State.FromConfig(cfg);

            var ch = new byte[13];
            FixtureAddressing.FillLyreChannels(
                ch, 0.25f, 0.75f, 0f, 1f, 0f, 0, 255, 0, 0);
            var packed = FixtureAddressing.PackLyreEntities(10, ch);
            foreach (var kv in packed)
                state.Set(kv.Key, kv.Value.R, kv.Value.G, kv.Value.B, 0);

            var packets = plan.Render(state);
            Assert.True(packets.ContainsKey(135));

            var viaPlan = packets[135];
            var viaDirect = new byte[512];
            FixtureAddressing.WriteLyreToDmx(viaDirect, 10, ch);

            Assert.Equal(viaDirect.Take(42).ToArray(), viaPlan.Take(42).ToArray());
        }

        [Fact]
        public void ArtNetPacket_HasUniverse33AndDmxPayload()
        {
            var dmx = new byte[512];
            FixtureAddressing.WriteProjectorToDmx(dmx, 255, 128, 64);
            var pkt = ArtNetSender.BuildPacket(FixtureAddressing.ArtNetUniverse, dmx, sequence: 1);

            Assert.Equal((byte)'A', pkt[0]);
            Assert.Equal(0x00, pkt[8]);
            Assert.Equal(0x50, pkt[9]);
            Assert.Equal(33, pkt[14]);
            Assert.Equal(0, pkt[15]);
            Assert.Equal(255, pkt[18 + 168]);
            Assert.Equal(128, pkt[18 + 169]);
            Assert.Equal(64, pkt[18 + 170]);
        }

        [Fact]
        public void ArtNetLoopback_ReceivesMovingLyrePan()
        {
            using var listener = new System.Net.Sockets.UdpClient(0);
            int port = ((System.Net.IPEndPoint)listener.Client.LocalEndPoint!).Port;
            listener.Client.ReceiveTimeout = 2000;

            var dmx = new byte[512];
            var ch = new byte[13];
            FixtureAddressing.FillLyreChannels(ch, pan: 0.75f, tilt: 0.25f, speed: 0f,
                dimmer: 1f, strobe: 0f, r: 0, g: 255, b: 0, white: 0);
            FixtureAddressing.WriteLyreToDmx(dmx, 10, ch);
            FixtureAddressing.WriteProjectorToDmx(dmx, 255, 0, 0);

            using (var sender = new ArtNetSender())
                sender.Send("127.0.0.1", FixtureAddressing.ArtNetUniverse, dmx, port);

            var remote = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            byte[] got = listener.Receive(ref remote);

            Assert.True(got.Length >= 18 + 171);
            Assert.Equal(33, got[14]);
            Assert.Equal(255, got[18 + 168]); // projecteur R
            Assert.Equal(0xBF, got[18 + 0]);  // pan high ≈ 0.75
            Assert.Equal(255, got[18 + 5]);   // dimmer
            Assert.Equal(255, got[18 + 8]);   // G
        }
    }
}
