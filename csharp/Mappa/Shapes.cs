using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Mappa
{
    /// <summary>Un point 3D (les positions ne font pas partie du contrat de routage).</summary>
    public readonly struct Vec3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// Un ruban de LED place dans l'espace. Les entites
    /// [EntityStart, EntityStart + LedCount - 1] sont reparties regulierement le
    /// long de la polyligne Points (2 points = segment droit, plus = courbe brisee).
    /// </summary>
    public sealed class Segment
    {
        public string Id { get; set; } = "";
        public int EntityStart { get; set; }
        public int LedCount { get; set; }
        public IReadOnlyList<Vec3> Points { get; set; } = Array.Empty<Vec3>();
        public LedType LedType { get; set; } = LedType.RGB;

        /// <summary>Retourne (entityId, position) pour chaque LED du segment.</summary>
        public List<(int EntityId, Vec3 Pos)> Positions()
        {
            var pts = new List<Vec3>(Points);
            var result = new List<(int, Vec3)>(LedCount);

            if (pts.Count < 2)
            {
                var p = pts.Count > 0 ? pts[0] : new Vec3(0, 0, 0);
                for (int i = 0; i < LedCount; i++) result.Add((EntityStart + i, p));
                return result;
            }

            var segLen = new double[pts.Count - 1];
            double total = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                segLen[i] = Distance(pts[i], pts[i + 1]);
                total += segLen[i];
            }
            if (total == 0) total = 1.0;

            for (int i = 0; i < LedCount; i++)
            {
                double t = LedCount > 1 ? (double)i / (LedCount - 1) : 0.0;
                result.Add((EntityStart + i, PointAt(pts, segLen, t * total)));
            }
            return result;
        }

        private static double Distance(Vec3 a, Vec3 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static Vec3 PointAt(List<Vec3> pts, double[] segLen, double dist)
        {
            double acc = 0;
            for (int i = 0; i < segLen.Length; i++)
            {
                if (acc + segLen[i] >= dist || i == segLen.Length - 1)
                {
                    double local = segLen[i] == 0 ? 0.0 : (dist - acc) / segLen[i];
                    Vec3 a = pts[i], b = pts[i + 1];
                    return new Vec3(
                        a.X + (b.X - a.X) * local,
                        a.Y + (b.Y - a.Y) * local,
                        a.Z + (b.Z - a.Z) * local);
                }
                acc += segLen[i];
            }
            return pts[pts.Count - 1];
        }
    }

    /// <summary>
    /// Formes non-2D : preuve que l'architecture gere des installations quelconques.
    ///
    /// Les positions 3D ne font PAS partie du contrat de routage (A n'en a pas
    /// besoin) : elles servent a l'authoring (C) et au debug (D). On les sauvegarde
    /// a cote de la config (*.positions.json), ce qui garde le contrat inchange.
    /// </summary>
    public static class Shapes
    {
        public static (Config Config, Dictionary<int, Vec3> Positions) BuildShapeConfig(
            IEnumerable<Segment> segments,
            string name = "shape",
            string controllerIp = "192.168.1.10",
            int controllerPort = 6454)
        {
            var config = new Config(name);
            config.Controllers.Add(new Controller
            {
                Id = "ctrl-1",
                Ip = controllerIp,
                Port = controllerPort,
                Outputs = 16,
            });
            var positions = new Dictionary<int, Vec3>();

            int universeIndex = 0;
            foreach (var seg in segments)
            {
                config.EntityMap.Add(new EntityMapping
                {
                    EntityStart = seg.EntityStart,
                    EntityEnd = seg.EntityStart + seg.LedCount - 1,
                    UniverseStart = universeIndex,
                    ChannelStart = 0,
                    LedType = seg.LedType,
                });
                config.Strips.Add(new Strip
                {
                    Id = seg.Id,
                    LedCount = seg.LedCount,
                    UniverseStart = universeIndex,
                    ChannelStart = 0,
                    LedType = seg.LedType,
                });
                config.Universes.Add(new Universe
                {
                    Index = universeIndex,
                    ControllerId = "ctrl-1",
                    Output = universeIndex / Config.UniversesPerOutput,
                });
                foreach (var (eid, pos) in seg.Positions()) positions[eid] = pos;
                universeIndex++;
            }
            return (config, positions);
        }

        /// <summary>
        /// Installation "araignee" non planaire : un corps circulaire (z=0) et
        /// des pattes coudees montant en z. Demontre que le meme pipeline
        /// State/Config/RoutingPlan gere une geometrie quelconque.
        /// </summary>
        public static (Config Config, Dictionary<int, Vec3> Positions) BuildSpiderConfig(
            int legs = 8,
            int ledsPerLeg = 30,
            int bodyLeds = 20,
            double legLength = 1.5,
            double bodyRadius = 0.3)
        {
            var segments = new List<Segment>();

            var bodyPts = new List<Vec3>();
            for (int i = 0; i < Math.Max(bodyLeds, 2); i++)
            {
                double ang = 2 * Math.PI * i / bodyLeds;
                bodyPts.Add(new Vec3(bodyRadius * Math.Cos(ang), bodyRadius * Math.Sin(ang), 0.0));
            }
            segments.Add(new Segment { Id = "body", EntityStart = 100, LedCount = bodyLeds, Points = bodyPts });

            for (int leg = 0; leg < legs; leg++)
            {
                double ang = 2 * Math.PI * leg / legs;
                var basePt = new Vec3(bodyRadius * Math.Cos(ang), bodyRadius * Math.Sin(ang), 0.0);
                var knee = new Vec3(
                    (bodyRadius + legLength * 0.5) * Math.Cos(ang),
                    (bodyRadius + legLength * 0.5) * Math.Sin(ang),
                    0.6);
                var foot = new Vec3(
                    (bodyRadius + legLength) * Math.Cos(ang),
                    (bodyRadius + legLength) * Math.Sin(ang),
                    0.0);
                segments.Add(new Segment
                {
                    Id = $"leg-{leg + 1}",
                    EntityStart = 1000 + leg * 1000,
                    LedCount = ledsPerLeg,
                    Points = new List<Vec3> { basePt, knee, foot },
                });
            }

            return BuildShapeConfig(segments, name: "spider");
        }

        // ---- Sauvegarde / chargement des positions 3D ---- //
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void SavePositions(Dictionary<int, Vec3> positions, string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.Append("{\n");
            int i = 0;
            foreach (var kv in positions)
            {
                sb.Append($"  \"{kv.Key}\": [{Num(kv.Value.X)}, {Num(kv.Value.Y)}, {Num(kv.Value.Z)}]");
                sb.Append(++i < positions.Count ? ",\n" : "\n");
            }
            sb.Append("}\n");
            File.WriteAllText(path, sb.ToString(), Utf8NoBom);
        }

        public static Dictionary<int, Vec3> LoadPositions(string path)
        {
            string text = File.ReadAllText(path, Utf8NoBom);
            var root = (Dictionary<string, object?>)new JsonParser(text).Parse()!;
            var result = new Dictionary<int, Vec3>();
            foreach (var kv in root)
            {
                var arr = (List<object?>)kv.Value!;
                result[int.Parse(kv.Key, CultureInfo.InvariantCulture)] =
                    new Vec3((double)arr[0]!, (double)arr[1]!, (double)arr[2]!);
            }
            return result;
        }

        private static string Num(double d) => d.ToString("0.############", CultureInfo.InvariantCulture);
    }
}
