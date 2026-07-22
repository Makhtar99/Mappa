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
            // Mapping officiel (source : learn.glassworks.tech/led/arch/other-devices)
            // AVEC INVERSION de l'ordre des lyres (Lyre 1 = gauche, Lyre 4 = droite) :
            //   - Projector : entites 1..4    -> canaux DMX 1..4   (offset 0..3)    R,V,B,W
            //   - Lyre 1    : entites 10..22  -> canaux DMX 70..82 (offset 69..81)  13 canaux (gauche)
            //   - Lyre 2    : entites 30..42  -> canaux DMX 50..62 (offset 49..61)
            //   - Lyre 3    : entites 50..62  -> canaux DMX 30..42 (offset 29..41)
            //   - Lyre 4    : entites 70..82  -> canaux DMX 10..22 (offset 9..21)   (droite)
            //   - Toutes sur l'univers ArtNet 33 (mappe via universe.index 135).
            string repoRoot = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(ConfigRoutingTests).Assembly.Location)!,
                "..", "..", "..", "..", ".."));
            string cfgPath = Path.Combine(repoRoot, "configs", "ecran.json");
            Assert.True(File.Exists(cfgPath), $"config introuvable: {cfgPath}");
            var cfg = Persistence.LoadConfig(cfgPath);
            var plan = new RoutingPlan(cfg);

            // Projector : entites 1..4 -> canaux DMX 1..4 (offsets 0..3), inchange.
            var projR = plan.AddressOf(1)!.Value;
            var projW = plan.AddressOf(4)!.Value;
            Assert.Equal(135, projR.Universe);
            Assert.Equal(0, projR.Channel);
            Assert.Equal(3, projW.Channel);
            Assert.Equal(1, projR.Channels); // RAW1

            // Lyre 1 (GAUCHE, inverse) : entites 10..22 -> canaux DMX 70..82 (offsets 69..81).
            var l1c1 = plan.AddressOf(10)!.Value;
            var l1c13 = plan.AddressOf(22)!.Value;
            Assert.Equal(135, l1c1.Universe);
            Assert.Equal(69, l1c1.Channel);
            Assert.Equal(1, l1c1.Channels); // RAW1
            Assert.Equal(81, l1c13.Channel);

            // Lyre 4 (DROITE, inverse) : entites 70..82 -> canaux DMX 10..22 (offsets 9..21).
            var l4c1 = plan.AddressOf(70)!.Value;
            var l4c13 = plan.AddressOf(82)!.Value;
            Assert.Equal(9, l4c1.Channel);
            Assert.Equal(21, l4c13.Channel);

            // Simulation du LyreController : pan=0.5, tilt=0.5 en 16 bits =>
            // pan_hi = 0x7F (127), pan_lo = 0xFF (255) sur les 2 premiers canaux.
            // On reproduit ici la meme sequence d'ecriture que LyreController.cs.
            // Lyre 1 debut = offset 9 (canal DMX 10).
            var state = State.FromConfig(cfg);
            const int baseId = 10; // Lyre 1
            int pan16 = (int)(0.5f * 65535f);
            int tilt16 = (int)(0.5f * 65535f);
            state.Set(baseId + 0, (byte)(pan16 >> 8), 0, 0);        // canal 1  : pan_hi
            state.Set(baseId + 1, (byte)(pan16 & 0xFF), 0, 0);      // canal 2  : pan_lo
            state.Set(baseId + 2, (byte)(tilt16 >> 8), 0, 0);       // canal 3  : tilt_hi
            state.Set(baseId + 3, (byte)(tilt16 & 0xFF), 0, 0);     // canal 4  : tilt_lo
            state.Set(baseId + 5, 255, 0, 0);                       // canal 6  : dimmer=100%
            state.Set(baseId + 7, 200, 0, 0);                       // canal 8  : R=200
            state.Set(baseId + 8, 100, 0, 0);                       // canal 9  : G=100
            state.Set(baseId + 9, 50,  0, 0);                       // canal 10 : B=50
            state.Set(baseId + 12, 42, 0, 0);                       // canal 13 : auto/reset=42
            var packets = plan.Render(state);
            var univ33 = packets[135];

            // Lyre 1 (inversee) commence a l'offset 69 (canal DMX 70). pan_hi -> pkt[69], etc.
            Assert.Equal(0x7F, univ33[69]);  // pan_hi   (canal DMX 70)
            Assert.Equal(0xFF, univ33[70]);  // pan_lo   (canal DMX 71)
            Assert.Equal(0x7F, univ33[71]);  // tilt_hi  (canal DMX 72)
            Assert.Equal(0xFF, univ33[72]);  // tilt_lo  (canal DMX 73)
            Assert.Equal(255,  univ33[74]);  // dimmer   (canal DMX 75)
            Assert.Equal(200,  univ33[76]);  // R        (canal DMX 77)
            Assert.Equal(100,  univ33[77]);  // G        (canal DMX 78)
            Assert.Equal(50,   univ33[78]);  // B        (canal DMX 79)
            Assert.Equal(42,   univ33[81]);  // auto     (canal DMX 82, 13e canal)

            // Lyre 2 (inversee) commence a l'offset 49 (canal DMX 50). entity 30 -> pkt[49].
            state.Set(30, 111, 0, 0);
            packets = plan.Render(state);
            Assert.Equal(111, packets[135][49]);

            // Projecteur : entite 1 (R) -> offset 0 (canal DMX 1).
            state.Set(1, 222, 0, 0);
            // entite 4 (W) -> offset 3 (canal DMX 4).
            state.Set(4, 88, 0, 0);
            packets = plan.Render(state);
            Assert.Equal(222, packets[135][0]);
            Assert.Equal(88,  packets[135][3]);

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
