using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Mappa
{
    /// <summary>
    /// Save / Load des configurations (P1) + rechargement a chaud (P4).
    ///
    /// Format : JSON (lisible, versionnable, editable a la main). C'est aussi le
    /// livrable exige ("tous les fichiers de configuration").
    ///
    /// Le JSON est ecrit/lu par un mini-serialiseur maison sans dependance externe,
    /// afin de rester compatible avec toutes les versions d'Unity. Le format est
    /// identique a celui de l'implementation Python (interoperable).
    /// </summary>
    public static class Persistence
    {
        // UTF-8 SANS BOM : le BOM ferait echouer le parseur JSON de Python
        // (interoperabilite avec l'implementation Python).
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void SaveConfig(Config config, string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ConfigJson.Serialize(config), Utf8NoBom);
        }

        public static Config LoadConfig(string path)
        {
            // ReadAllText detecte et retire un eventuel BOM automatiquement.
            string text = File.ReadAllText(path, Utf8NoBom);
            return ConfigJson.Deserialize(text);
        }
    }

    /// <summary>
    /// Gere la config courante et son rechargement a chaud.
    ///
    /// Exemple :
    ///   var mgr = new ConfigManager("configs/wall.json");
    ///   mgr.OnReload += cfg => router.Rebuild(cfg);
    ///   mgr.Load();
    ///   mgr.Reload("configs/spider.json"); // reconfiguration en direct
    /// </summary>
    public sealed class ConfigManager
    {
        private string? _path;
        private Config? _config;

        public event Action<Config>? OnReload;

        public ConfigManager(string? path = null)
        {
            _path = path;
        }

        public Config Config =>
            _config ?? throw new InvalidOperationException("Aucune configuration chargee (appeler Load()).");

        public string? Path => _path;

        public Config Load(string? path = null)
        {
            if (path != null) _path = path;
            if (_path == null) throw new ArgumentException("Aucun chemin de configuration fourni.");

            var config = Persistence.LoadConfig(_path);
            var problems = config.Validate();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Configuration invalide :\n  - " + string.Join("\n  - ", problems));
            }
            _config = config;
            OnReload?.Invoke(config);
            return config;
        }

        public Config Reload(string? path = null) => Load(path);

        public void Save(string? path = null)
        {
            var target = path ?? _path;
            if (target == null) throw new ArgumentException("Aucun chemin de sauvegarde fourni.");
            Persistence.SaveConfig(Config, target);
        }
    }

    /// <summary>Serialisation JSON de Config, interoperable avec la version Python.</summary>
    internal static class ConfigJson
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Serialize(Config c)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"name\": {Str(c.Name)},\n");

            AppendArray(sb, "controllers", c.Controllers, ctrl =>
                $"{{\"id\": {Str(ctrl.Id)}, \"ip\": {Str(ctrl.Ip)}, \"port\": {ctrl.Port}, \"outputs\": {ctrl.Outputs}}}");
            sb.Append(",\n");

            AppendArray(sb, "universes", c.Universes, u =>
                $"{{\"index\": {u.Index}, \"controller_id\": {Str(u.ControllerId)}, \"output\": {u.Output}, \"artnet_universe\": {u.EffectiveArtNetUniverse}}}");
            sb.Append(",\n");

            AppendArray(sb, "strips", c.Strips, s =>
                $"{{\"id\": {Str(s.Id)}, \"led_count\": {s.LedCount}, \"universe_start\": {s.UniverseStart}, \"channel_start\": {s.ChannelStart}, \"led_type\": {Str(s.LedType.ToString())}}}");
            sb.Append(",\n");

            AppendArray(sb, "devices", c.Devices, d =>
                $"{{\"id\": {Str(d.Id)}, \"type\": {Str(d.Type)}, \"universe\": {d.Universe}, \"channel_start\": {d.ChannelStart}, \"channel_count\": {d.ChannelCount}}}");
            sb.Append(",\n");

            AppendArray(sb, "entity_map", c.EntityMap, m =>
                $"{{\"entity_start\": {m.EntityStart}, \"entity_end\": {m.EntityEnd}, \"universe_start\": {m.UniverseStart}, \"channel_start\": {m.ChannelStart}, \"led_type\": {Str(m.LedType.ToString())}}}");
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static void AppendArray<T>(StringBuilder sb, string key, IList<T> items, Func<T, string> fmt)
        {
            sb.Append($"  \"{key}\": [");
            if (items.Count == 0)
            {
                sb.Append("]");
                return;
            }
            sb.Append("\n");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append("    ");
                sb.Append(fmt(items[i]));
                sb.Append(i < items.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]");
        }

        private static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // ---- Deserialisation via un petit parseur JSON generique ---- //
        public static Config Deserialize(string json)
        {
            object? parsed = new JsonParser(json).Parse();
            var root = (Dictionary<string, object?>)parsed!;
            var c = new Config(GetString(root, "name", "untitled"));

            foreach (var o in GetArray(root, "controllers"))
            {
                var d = (Dictionary<string, object?>)o!;
                c.Controllers.Add(new Controller
                {
                    Id = GetString(d, "id", ""),
                    Ip = GetString(d, "ip", ""),
                    Port = GetInt(d, "port", 6454),
                    Outputs = GetInt(d, "outputs", 16),
                });
            }
            foreach (var o in GetArray(root, "universes"))
            {
                var d = (Dictionary<string, object?>)o!;
                c.Universes.Add(new Universe
                {
                    Index = GetInt(d, "index", 0),
                    ControllerId = GetString(d, "controller_id", ""),
                    Output = GetInt(d, "output", 0),
                    ArtNetUniverse = GetInt(d, "artnet_universe", -1),
                });
            }
            foreach (var o in GetArray(root, "strips"))
            {
                var d = (Dictionary<string, object?>)o!;
                c.Strips.Add(new Strip
                {
                    Id = GetString(d, "id", ""),
                    LedCount = GetInt(d, "led_count", 0),
                    UniverseStart = GetInt(d, "universe_start", 0),
                    ChannelStart = GetInt(d, "channel_start", 0),
                    LedType = LedTypeExtensions.FromString(GetString(d, "led_type", "RGB")),
                });
            }
            foreach (var o in GetArray(root, "devices"))
            {
                var d = (Dictionary<string, object?>)o!;
                c.Devices.Add(new Device
                {
                    Id = GetString(d, "id", ""),
                    Type = GetString(d, "type", ""),
                    Universe = GetInt(d, "universe", 0),
                    ChannelStart = GetInt(d, "channel_start", 0),
                    ChannelCount = GetInt(d, "channel_count", 0),
                });
            }
            foreach (var o in GetArray(root, "entity_map"))
            {
                var d = (Dictionary<string, object?>)o!;
                c.EntityMap.Add(new EntityMapping
                {
                    EntityStart = GetInt(d, "entity_start", 0),
                    EntityEnd = GetInt(d, "entity_end", 0),
                    UniverseStart = GetInt(d, "universe_start", 0),
                    ChannelStart = GetInt(d, "channel_start", 0),
                    LedType = LedTypeExtensions.FromString(GetString(d, "led_type", "RGB")),
                });
            }
            return c;
        }

        private static string GetString(Dictionary<string, object?> d, string k, string def)
            => d.TryGetValue(k, out var v) && v is string s ? s : def;

        private static int GetInt(Dictionary<string, object?> d, string k, int def)
            => d.TryGetValue(k, out var v) && v is double n ? (int)n : def;

        private static IEnumerable<object?> GetArray(Dictionary<string, object?> d, string k)
            => d.TryGetValue(k, out var v) && v is List<object?> list ? list : new List<object?>();
    }

    /// <summary>Parseur JSON minimal (objets, tableaux, string, number, bool, null).</summary>
    internal sealed class JsonParser
    {
        private readonly string _s;
        private int _i;

        public JsonParser(string s) { _s = s; _i = 0; }

        public object? Parse()
        {
            SkipWs();
            var v = ParseValue();
            SkipWs();
            return v;
        }

        private object? ParseValue()
        {
            SkipWs();
            char c = _s[_i];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': _i += 4; return true;
                case 'f': _i += 5; return false;
                case 'n': _i += 4; return null;
                default: return ParseNumber();
            }
        }

        private Dictionary<string, object?> ParseObject()
        {
            var d = new Dictionary<string, object?>();
            _i++; // {
            SkipWs();
            if (_s[_i] == '}') { _i++; return d; }
            while (true)
            {
                SkipWs();
                string key = ParseString();
                SkipWs();
                _i++; // :
                d[key] = ParseValue();
                SkipWs();
                char c = _s[_i++];
                if (c == '}') break;
                // c == ',' -> continue
            }
            return d;
        }

        private List<object?> ParseArray()
        {
            var list = new List<object?>();
            _i++; // [
            SkipWs();
            if (_s[_i] == ']') { _i++; return list; }
            while (true)
            {
                list.Add(ParseValue());
                SkipWs();
                char c = _s[_i++];
                if (c == ']') break;
                // c == ',' -> continue
            }
            return list;
        }

        private string ParseString()
        {
            var sb = new StringBuilder();
            _i++; // opening quote
            while (true)
            {
                char c = _s[_i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    char e = _s[_i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        default: sb.Append(e); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private double ParseNumber()
        {
            int start = _i;
            while (_i < _s.Length && ("-+.eE0123456789".IndexOf(_s[_i]) >= 0)) _i++;
            return double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture);
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }
}
