using System.IO;
using System.Linq;
using Mappa;
using Xunit;

namespace Mappa.Tests
{
    public class ConfigRoutingTests
    {
        private static Config Sample()
        {
            var c = new Config("t");
            c.Controllers.Add(new Controller { Id = "c1", Ip = "127.0.0.1" });
            c.Universes.Add(new Universe { Index = 0, ControllerId = "c1", Output = 0 });
            c.Strips.Add(new Strip { Id = "s1", LedCount = 2, UniverseStart = 0 });
            c.EntityMap.Add(new EntityMapping { EntityStart = 1, EntityEnd = 2, UniverseStart = 0 });
            return c;
        }

        [Fact]
        public void EntityIdsSortedUnique()
        {
            var c = new Config();
            c.EntityMap.Add(new EntityMapping { EntityStart = 1, EntityEnd = 3, UniverseStart = 0 });
            c.EntityMap.Add(new EntityMapping { EntityStart = 10, EntityEnd = 11, UniverseStart = 1 });
            Assert.Equal(new[] { 1, 2, 3, 10, 11 }, c.EntityIds().ToArray());
        }

        [Fact]
        public void ValidateDetectsUnknownController()
        {
            var c = new Config();
            c.Universes.Add(new Universe { Index = 0, ControllerId = "ghost", Output = 0 });
            Assert.Contains(c.Validate(), p => p.Contains("controleur inconnu"));
        }

        [Fact]
        public void ValidateDetectsOverlap()
        {
            var c = new Config();
            c.EntityMap.Add(new EntityMapping { EntityStart = 1, EntityEnd = 5, UniverseStart = 0 });
            c.EntityMap.Add(new EntityMapping { EntityStart = 4, EntityEnd = 8, UniverseStart = 1 });
            Assert.Contains(c.Validate(), p => p.Contains("plusieurs fois"));
        }

        [Fact]
        public void SaveLoadRoundTrip()
        {
            var c = Sample();
            string path = Path.Combine(Path.GetTempPath(), "mappa_test_cfg.json");
            Persistence.SaveConfig(c, path);
            var c2 = Persistence.LoadConfig(path);
            Assert.Equal(c.Name, c2.Name);
            Assert.Equal(c.EntityIds(), c2.EntityIds());
            Assert.Equal(c.Controllers[0].Ip, c2.Controllers[0].Ip);
        }

        [Fact]
        public void RoutingAddressRgb()
        {
            var c = new Config();
            c.EntityMap.Add(new EntityMapping { EntityStart = 1, EntityEnd = 3, UniverseStart = 0, LedType = LedType.RGB });
            var plan = new RoutingPlan(c);
            Assert.Equal(0, plan.AddressOf(1)!.Value.Channel);
            Assert.Equal(3, plan.AddressOf(2)!.Value.Channel);
            Assert.Equal(6, plan.AddressOf(3)!.Value.Channel);
        }

        [Fact]
        public void RoutingUniverseOverflow()
        {
            var c = new Config();
            c.EntityMap.Add(new EntityMapping { EntityStart = 1, EntityEnd = 171, UniverseStart = 5, LedType = LedType.RGB });
            var plan = new RoutingPlan(c);
            Assert.Equal(5, plan.AddressOf(170)!.Value.Universe);
            Assert.Equal(6, plan.AddressOf(171)!.Value.Universe);
            Assert.Equal(0, plan.AddressOf(171)!.Value.Channel);
        }

        [Fact]
        public void RenderPlacesBytes()
        {
            var c = new Config();
            c.EntityMap.Add(new EntityMapping { EntityStart = 1, EntityEnd = 2, UniverseStart = 0, LedType = LedType.RGB });
            var plan = new RoutingPlan(c);
            var s = State.FromConfig(c);
            s.Set(1, 10, 20, 30);
            s.Set(2, 40, 50, 60);
            var packets = plan.Render(s);
            Assert.Equal(new byte[] { 10, 20, 30, 40, 50, 60 }, packets[0].Take(6).ToArray());
        }
    }
}
