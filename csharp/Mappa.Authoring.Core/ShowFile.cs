using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mappa.Authoring.Core
{
    public static class ShowFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void Save(Show show, string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, Serialize(show), Utf8NoBom);
        }

        public static Show Load(string path)
        {
            return Deserialize(File.ReadAllText(path, Utf8NoBom));
        }

        public static string Serialize(Show show)
        {
            var tracks = new JsonArray();
            foreach (var track in show.Tracks)
            {
                var clips = new JsonArray();
                foreach (var clip in track.Clips)
                {
                    var node = new JsonObject
                    {
                        ["name"] = clip.Name,
                        ["start"] = clip.Start,
                        ["duration"] = clip.Duration,
                        ["fade_in"] = clip.FadeIn,
                        ["fade_out"] = clip.FadeOut,
                        ["effect"] = EffectToJson(clip.Effect),
                    };
                    if (clip.Targets != null)
                    {
                        var arr = new JsonArray();
                        foreach (int id in clip.Targets) arr.Add(id);
                        node["targets"] = arr;
                    }
                    clips.Add(node);
                }
                tracks.Add(new JsonObject
                {
                    ["name"] = track.Name,
                    ["enabled"] = track.Enabled,
                    ["clips"] = clips,
                });
            }

            var root = new JsonObject
            {
                ["name"] = show.Name,
                ["config_path"] = show.ConfigPath,
                ["audio_path"] = show.AudioPath,
                ["fps"] = show.Fps,
                ["duration"] = show.Duration,
                ["tracks"] = tracks,
            };
            return root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            });
        }

        public static Show Deserialize(string json)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            var show = new Show
            {
                Name = Str(root, "name", "untitled-show"),
                ConfigPath = Str(root, "config_path", ""),
                AudioPath = Str(root, "audio_path", ""),
                Fps = Num(root, "fps", 40.0),
                Duration = Num(root, "duration", 60.0),
            };

            if (root["tracks"] is JsonArray tracks)
            {
                foreach (var tn in tracks)
                {
                    var to = tn!.AsObject();
                    var track = new Track
                    {
                        Name = Str(to, "name", "track"),
                        Enabled = Bool(to, "enabled", true),
                    };
                    if (to["clips"] is JsonArray clips)
                    {
                        foreach (var cn in clips)
                        {
                            var co = cn!.AsObject();
                            var clip = new Clip
                            {
                                Name = Str(co, "name", "clip"),
                                Start = Num(co, "start", 0),
                                Duration = Num(co, "duration", 0),
                                FadeIn = Num(co, "fade_in", 0),
                                FadeOut = Num(co, "fade_out", 0),
                                Effect = EffectFromJson(co["effect"]!.AsObject()),
                            };
                            if (co["targets"] is JsonArray ta)
                            {
                                var ids = new int[ta.Count];
                                for (int i = 0; i < ta.Count; i++) ids[i] = (int)ta[i]!.GetValue<double>();
                                clip.Targets = ids;
                            }
                            track.Clips.Add(clip);
                        }
                    }
                    show.Tracks.Add(track);
                }
            }
            return show;
        }

        private static JsonObject EffectToJson(IEffect e)
        {
            var o = new JsonObject { ["kind"] = e.Kind };
            switch (e)
            {
                case SolidColorEffect s:
                    o["color"] = ColorJson(s.Color);
                    break;
                case GradientSweepEffect g:
                    o["color_a"] = ColorJson(g.ColorA);
                    o["color_b"] = ColorJson(g.ColorB);
                    o["speed"] = g.Speed;
                    o["cycles"] = g.Cycles;
                    break;
                case PlasmaEffect p:
                    o["scale"] = p.Scale;
                    o["speed"] = p.Speed;
                    o["saturation"] = p.Saturation;
                    o["value"] = p.Value;
                    break;
                case StrobeEffect st:
                    o["color"] = ColorJson(st.Color);
                    o["frequency"] = st.Frequency;
                    o["duty_cycle"] = st.DutyCycle;
                    break;
                case ImageEffect im:
                    o["path"] = im.Path;
                    o["flip_y"] = im.FlipY;
                    break;
            }
            return o;
        }

        private static IEffect EffectFromJson(JsonObject o)
        {
            string kind = Str(o, "kind", "solid");
            switch (kind)
            {
                case "gradient":
                    return new GradientSweepEffect
                    {
                        ColorA = ColorFrom(o["color_a"]),
                        ColorB = ColorFrom(o["color_b"]),
                        Speed = (float)Num(o, "speed", 0.25),
                        Cycles = (float)Num(o, "cycles", 1),
                    };
                case "plasma":
                    return new PlasmaEffect
                    {
                        Scale = (float)Num(o, "scale", 4),
                        Speed = (float)Num(o, "speed", 0.5),
                        Saturation = (float)Num(o, "saturation", 1),
                        Value = (float)Num(o, "value", 1),
                    };
                case "strobe":
                    return new StrobeEffect
                    {
                        Color = ColorFrom(o["color"]),
                        Frequency = (float)Num(o, "frequency", 8),
                        DutyCycle = (float)Num(o, "duty_cycle", 0.5),
                    };
                case "image":
                    return new ImageEffect
                    {
                        Path = Str(o, "path", ""),
                        FlipY = Bool(o, "flip_y", true),
                    };
                default:
                    return new SolidColorEffect { Color = ColorFrom(o["color"]) };
            }
        }

        private static JsonArray ColorJson(ColorF c) => new JsonArray { c.R, c.G, c.B, c.W };

        private static ColorF ColorFrom(JsonNode? node)
        {
            if (node is not JsonArray a || a.Count < 3) return ColorF.White;
            float w = a.Count > 3 ? (float)a[3]!.GetValue<double>() : 0f;
            return new ColorF(
                (float)a[0]!.GetValue<double>(),
                (float)a[1]!.GetValue<double>(),
                (float)a[2]!.GetValue<double>(),
                w);
        }

        private static string Str(JsonObject o, string key, string def)
            => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s! : def;

        private static double Num(JsonObject o, string key, double def)
            => o[key] is JsonValue v && v.TryGetValue(out double d) ? d : def;

        private static bool Bool(JsonObject o, string key, bool def)
            => o[key] is JsonValue v && v.TryGetValue(out bool b) ? b : def;
    }
}
