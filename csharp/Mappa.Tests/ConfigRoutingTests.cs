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

        [Fact]
        public void Raw1AddressAndRender()
        {
            // Regression : les appareils DMX (lyres, projecteur) utilisent RAW1
            // = 1 canal par entite, l'octet R du state est copie tel quel au canal.
            var c = new Config();
            c.EntityMap.Add(new EntityMapping
            {
                EntityStart = 10, EntityEnd = 12,
                UniverseStart = 0, ChannelStart = 5,
                LedType = LedType.RAW1,
            });
            var plan = new RoutingPlan(c);
            Assert.Equal(5, plan.AddressOf(10)!.Value.Channel);
            Assert.Equal(6, plan.AddressOf(11)!.Value.Channel);
            Assert.Equal(7, plan.AddressOf(12)!.Value.Channel);
            Assert.Equal(1, plan.AddressOf(10)!.Value.Channels);

            var s = State.FromConfig(c);
            s.Set(10, 200, 0, 0); // canal DMX 5 = 200
            s.Set(11, 100, 0, 0); // canal DMX 6 = 100
            s.Set(12, 50,  0, 0); // canal DMX 7 = 50
            var packets = plan.Render(s);
            Assert.Equal(200, packets[0][5]);
            Assert.Equal(100, packets[0][6]);
            Assert.Equal(50,  packets[0][7]);
        }

        [Fact]
        public void EcranJsonRoutesLyresAndProjectorCorrectly()
        {
            // Non-regression bout-en-bout sur la vraie config utilisee demain.
            // On charge configs/ecran.json et on valide que :
            //   - Lyre 1 a bien 14 entites (10..23) mappees canaux DMX 1..14.
            //   - Lyre 4 a bien 14 entites (70..83) mappees canaux DMX 127..140.
            //   - Projector = 1 entite (id 1) sur canal DMX 169.
            //   - Toutes sont sur l'univers ArtNet 33 (mappe via universe.index 135).
            //   - La couleur ecrite par LyreController arrive au bon octet DMX.
            string repoRoot = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(ConfigRoutingTests).Assembly.Location)!,
                "..", "..", "..", "..", ".."));
            string cfgPath = Path.Combine(repoRoot, "configs", "ecran.json");
            Assert.True(File.Exists(cfgPath), $"config introuvable: {cfgPath}");
            var cfg = Persistence.LoadConfig(cfgPath);
            var plan = new RoutingPlan(cfg);

            // Lyre 1 : entites 10..23, canal_start=0, univers 135
            var l1c1 = plan.AddressOf(10)!.Value;    // canal DMX 1 = offset 0
            var l1c14 = plan.AddressOf(23)!.Value;   // canal DMX 14 = offset 13
            Assert.Equal(135, l1c1.Universe);
            Assert.Equal(0, l1c1.Channel);
            Assert.Equal(1, l1c1.Channels); // RAW1
            Assert.Equal(13, l1c14.Channel);

            // Lyre 4 : entites 70..83, canal_start=126
            var l4c1 = plan.AddressOf(70)!.Value;
            var l4c14 = plan.AddressOf(83)!.Value;
            Assert.Equal(126, l4c1.Channel);
            Assert.Equal(139, l4c14.Channel);

            // Projector : entite 1, canal_start=168
            var proj = plan.AddressOf(1)!.Value;
            Assert.Equal(168, proj.Channel);
            Assert.Equal(1, proj.Channels);

            // Simulation du LyreController : pan=0.5, tilt=0.5 en 16 bits =>
            // pan_hi = 0x7F (127), pan_lo = 0xFF (255) sur les 2 premiers canaux.
            // On reproduit ici la meme sequence d'ecriture que LyreController.cs.
            var state = State.FromConfig(cfg);
            const int baseId = 10; // Lyre 1
            int pan16 = (int)(0.5f * 65535f);
            int tilt16 = (int)(0.5f * 65535f);
            state.Set(baseId + 0, (byte)(pan16 >> 8), 0, 0);        // canal 1 : pan_hi
            state.Set(baseId + 1, (byte)(pan16 & 0xFF), 0, 0);      // canal 2 : pan_lo
            state.Set(baseId + 2, (byte)(tilt16 >> 8), 0, 0);       // canal 3 : tilt_hi
            state.Set(baseId + 3, (byte)(tilt16 & 0xFF), 0, 0);     // canal 4 : tilt_lo
            state.Set(baseId + 5, 255, 0, 0);                       // canal 6 : dimmer=100%
            state.Set(baseId + 7, 200, 0, 0);                       // canal 8 : R=200
            state.Set(baseId + 8, 100, 0, 0);                       // canal 9 : G=100
            state.Set(baseId + 9, 50,  0, 0);                       // canal 10 : B=50
            state.Set(baseId + 13, 42, 0, 0);                       // canal 14 : reset=42
            var packets = plan.Render(state);
            var univ33 = packets[135];

            // Verifications finales des octets DMX sortants (canal DMX N -> pkt[N-1]).
            Assert.Equal(0x7F, univ33[0]);   // pan_hi
            Assert.Equal(0xFF, univ33[1]);   // pan_lo
            Assert.Equal(0x7F, univ33[2]);   // tilt_hi
            Assert.Equal(0xFF, univ33[3]);   // tilt_lo
            Assert.Equal(255,  univ33[5]);   // dimmer
            Assert.Equal(200,  univ33[7]);   // R
            Assert.Equal(100,  univ33[8]);   // G
            Assert.Equal(50,   univ33[9]);   // B
            Assert.Equal(42,   univ33[13]);  // reset (14e canal)

            // Lyre 2 doit commencer au canal DMX 43 (offset 42) sans collision.
            state.Set(30, 111, 0, 0);
            packets = plan.Render(state);
            Assert.Equal(111, packets[135][42]);

            // Projecteur : canal DMX 169 (offset 168).
            state.Set(1, 222, 0, 0);
            packets = plan.Render(state);
            Assert.Equal(222, packets[135][168]);

            // Validation globale de la config (pas d'overlap/inconnu).
            Assert.Empty(cfg.Validate());
        }

        [Fact]
        public void Raw1RoundTripJson()
        {
            // Regression : "RAW1" doit s'ecrire et se relire dans le JSON.
            var c = new Config("raw1test");
            c.Controllers.Add(new Controller { Id = "c1", Ip = "127.0.0.1" });
            c.Universes.Add(new Universe { Index = 0, ControllerId = "c1", Output = 0 });
            c.EntityMap.Add(new EntityMapping
            {
                EntityStart = 1, EntityEnd = 5, UniverseStart = 0, ChannelStart = 0,
                LedType = LedType.RAW1,
            });
            string path = Path.Combine(Path.GetTempPath(), "mappa_raw1_cfg.json");
            Persistence.SaveConfig(c, path);
            var c2 = Persistence.LoadConfig(path);
            Assert.Equal(LedType.RAW1, c2.EntityMap[0].LedType);
            Assert.Equal(5, c2.EntityMap[0].EntityEnd);
        }
    }
}
