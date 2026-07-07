using System;
using System.IO;
using System.Linq;
using Mappa;
using Xunit;

namespace Mappa.Tests
{
    public class WallTests
    {
        [Fact]
        public void WallGeneratesWithoutOverlap()
        {
            Assert.Empty(Wall.BuildWallConfig().Validate());
        }

        [Fact]
        public void WallEntityBases()
        {
            var cfg = Wall.BuildWallConfig();
            var starts = cfg.EntityMap.Select(m => m.EntityStart).ToList();
            Assert.Equal(100, starts[0]);
            Assert.Equal(400, starts[1]);
            Assert.Contains(5100, starts);
            Assert.Contains(10100, starts);
        }

        [Fact]
        public void WallRejectsOverlapParams()
        {
            Assert.Throws<ArgumentException>(() =>
                Wall.BuildWallConfig(ledsPerColumn: 400, columnStride: 300));
        }
    }

    public class ShapeTests
    {
        [Fact]
        public void SegmentEndpoints()
        {
            var seg = new Segment
            {
                Id = "s", EntityStart = 0, LedCount = 3,
                Points = new[] { new Vec3(0, 0, 0), new Vec3(0, 0, 2) },
            };
            var pos = seg.Positions();
            Assert.Equal(0.0, pos[0].Pos.Z);
            Assert.Equal(2.0, pos[^1].Pos.Z, 9);
            Assert.Equal(1.0, pos[1].Pos.Z, 9);
        }

        [Fact]
        public void SpiderIs3DAndRoutable()
        {
            var (cfg, positions) = Shapes.BuildSpiderConfig(legs: 8, ledsPerLeg: 30, bodyLeds: 20);
            Assert.Empty(cfg.Validate());
            Assert.Equal(20 + 8 * 30, cfg.EntityIds().Count);
            Assert.Contains(positions.Values, p => p.Z > 0);
            Assert.Equal(9, new RoutingPlan(cfg).Universes.Count);
        }

        [Fact]
        public void PositionsRoundTrip()
        {
            var (_, positions) = Shapes.BuildSpiderConfig();
            string path = Path.Combine(Path.GetTempPath(), "mappa_test_pos.json");
            Shapes.SavePositions(positions, path);
            var loaded = Shapes.LoadPositions(path);
            Assert.Equal(positions.Count, loaded.Count);
            foreach (var kv in positions)
            {
                Assert.Equal(kv.Value.X, loaded[kv.Key].X, 6);
                Assert.Equal(kv.Value.Z, loaded[kv.Key].Z, 6);
            }
        }
    }

    public class FailoverTests
    {
        private static Config Dual()
        {
            var c = new Config("dual");
            c.Controllers.Add(new Controller { Id = "A", Ip = "10.0.0.1" });
            c.Controllers.Add(new Controller { Id = "B", Ip = "10.0.0.2" });
            c.Universes.Add(new Universe { Index = 0, ControllerId = "A", Output = 0 });
            c.Universes.Add(new Universe { Index = 1, ControllerId = "A", Output = 1 });
            c.Universes.Add(new Universe { Index = 2, ControllerId = "B", Output = 2 });
            c.EntityMap.Add(new EntityMapping { EntityStart = 1, EntityEnd = 3, UniverseStart = 0 });
            c.EntityMap.Add(new EntityMapping { EntityStart = 100, EntityEnd = 102, UniverseStart = 1 });
            c.EntityMap.Add(new EntityMapping { EntityStart = 200, EntityEnd = 202, UniverseStart = 2 });
            return c;
        }

        [Fact]
        public void AddControllerRejectsDuplicate()
        {
            Assert.Throws<ArgumentException>(() => Failover.AddController(Dual(), "A", "10.0.0.9"));
        }

        [Fact]
        public void ReassignSubset()
        {
            var c = Dual();
            var moved = Failover.ReassignUniverses(c, "A", "B", new[] { 1 });
            Assert.Equal(new[] { 1 }, moved.ToArray());
            Assert.Equal("A", c.UniverseByIndex(0).ControllerId);
            Assert.Equal("B", c.UniverseByIndex(1).ControllerId);
        }

        [Fact]
        public void ReplaceControllerPreservesAddressing()
        {
            var c = Dual();
            var planBefore = new RoutingPlan(c);
            var moved = Failover.ReplaceController(c, "A", "C", "10.0.0.3");

            Assert.Equal(new[] { 0, 1 }, moved.OrderBy(x => x).ToArray());
            Assert.DoesNotContain(c.Controllers, x => x.Id == "A");
            Assert.Contains(c.Controllers, x => x.Id == "C");
            Assert.Equal("C", Failover.ControllerOfUniverse(c, 0)!.Id);
            Assert.Equal("B", Failover.ControllerOfUniverse(c, 2)!.Id);

            var planAfter = new RoutingPlan(c);
            foreach (var e in c.EntityIds())
            {
                Assert.Equal(planBefore.AddressOf(e)!.Value.Universe, planAfter.AddressOf(e)!.Value.Universe);
                Assert.Equal(planBefore.AddressOf(e)!.Value.Channel, planAfter.AddressOf(e)!.Value.Channel);
            }
            Assert.Empty(c.Validate());
        }

        [Fact]
        public void ReplaceUnknownRaises()
        {
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() =>
                Failover.ReplaceController(Dual(), "ghost", "C", "10.0.0.3"));
        }
    }

    public class ArtNetTests
    {
        [Fact]
        public void BuildPacketHeader()
        {
            var dmx = new byte[6] { 10, 20, 30, 40, 50, 60 };
            var pkt = ArtNetSender.BuildPacket(universe: 5, dmx512: dmx, sequence: 1);
            // "Art-Net\0"
            Assert.Equal((byte)'A', pkt[0]);
            Assert.Equal((byte)0, pkt[7]);
            // OpCode ArtDMX 0x5000 little-endian
            Assert.Equal(0x00, pkt[8]);
            Assert.Equal(0x50, pkt[9]);
            // Univers 5
            Assert.Equal(5, pkt[14]);
            Assert.Equal(0, pkt[15]);
            // Donnees copiees a l'offset 18
            Assert.Equal(new byte[] { 10, 20, 30, 40, 50, 60 }, pkt.Skip(18).Take(6).ToArray());
        }
    }
}
