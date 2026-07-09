using System;
using System.Collections.Generic;

namespace Mappa
{
    /// <summary>Nombre de canaux DMX par LED selon le type.</summary>
    public enum LedType
    {
        RGB = 3,
        RGBW = 4,
    }

    public static class LedTypeExtensions
    {
        public static int Channels(this LedType t) => (int)t;

        public static LedType FromString(string value)
        {
            return (LedType)Enum.Parse(typeof(LedType), value.Trim().ToUpperInvariant());
        }
    }

    /// <summary>Un controleur physique (ex. BC216).</summary>
    public sealed class Controller
    {
        public string Id { get; set; } = "";
        public string Ip { get; set; } = "";
        public int Port { get; set; } = 6454; // port ArtNet par defaut
        public int Outputs { get; set; } = 16;
    }

    /// <summary>Un univers DMX512, rattache a une sortie d'un controleur.</summary>
    public sealed class Universe
    {
        public int Index { get; set; }        // identifiant GLOBAL unique dans la config
        public string ControllerId { get; set; } = "";
        public int Output { get; set; }

        // Numero d'univers ArtNet REELLEMENT envoye sur le fil a ce controleur.
        // Chaque controleur ayant sa propre numerotation locale (ex. 0..31),
        // il faut le decoupler de l'Index global (qui doit rester unique dans
        // la config). -1 = non specifie -> on retombe sur Index (retrocompat).
        public int ArtNetUniverse { get; set; } = -1;

        /// <summary>Univers ArtNet effectif (ArtNetUniverse si defini, sinon Index).</summary>
        public int EffectiveArtNetUniverse => ArtNetUniverse >= 0 ? ArtNetUniverse : Index;
    }

    /// <summary>Une bande LED.</summary>
    public sealed class Strip
    {
        public string Id { get; set; } = "";
        public int LedCount { get; set; }
        public int UniverseStart { get; set; }
        public int ChannelStart { get; set; }
        public LedType LedType { get; set; } = LedType.RGB;
    }

    /// <summary>Un appareil DMX generique (projecteur statique, lyre...).</summary>
    public sealed class Device
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";  // "static" | "lyre" | ...
        public int Universe { get; set; }
        public int ChannelStart { get; set; }    // 1-indexe (convention DMX)
        public int ChannelCount { get; set; }

        /// <summary>Plage 1-indexee inclusive (debut, fin).</summary>
        public (int Start, int End) ChannelRange => (ChannelStart, ChannelStart + ChannelCount - 1);
    }

    /// <summary>
    /// Mappe une plage contigue d'IDs d'entites vers une adresse physique.
    /// Les entites [EntityStart, EntityEnd] (inclusif) sont posees a partir de
    /// (UniverseStart, ChannelStart), chaque entite occupant LedType.Channels()
    /// canaux, avec debordement automatique d'univers.
    /// </summary>
    public sealed class EntityMapping
    {
        public int EntityStart { get; set; }
        public int EntityEnd { get; set; }
        public int UniverseStart { get; set; }
        public int ChannelStart { get; set; }
        public LedType LedType { get; set; } = LedType.RGB;

        public int Count => EntityEnd - EntityStart + 1;
    }

    /// <summary>Configuration complete d'une installation (P1).</summary>
    public sealed class Config
    {
        public const int DmxChannelsPerUniverse = 512;
        public const int UniversesPerOutput = 2; // BC216 : 1024 canaux / sortie

        public string Name { get; set; } = "untitled";
        public List<Controller> Controllers { get; } = new List<Controller>();
        public List<Universe> Universes { get; } = new List<Universe>();
        public List<Strip> Strips { get; } = new List<Strip>();
        public List<Device> Devices { get; } = new List<Device>();
        public List<EntityMapping> EntityMap { get; } = new List<EntityMapping>();

        public Config() { }

        public Config(string name) { Name = name; }

        // -------------------------------------------------------------- //
        // Requetes pratiques
        // -------------------------------------------------------------- //
        /// <summary>Liste triee, dedupliquee, de tous les IDs d'entites mappes.</summary>
        public List<int> EntityIds()
        {
            var set = new SortedSet<int>();
            foreach (var m in EntityMap)
            {
                for (int id = m.EntityStart; id <= m.EntityEnd; id++)
                {
                    set.Add(id);
                }
            }
            return new List<int>(set);
        }

        public Universe UniverseByIndex(int index)
        {
            foreach (var u in Universes)
            {
                if (u.Index == index) return u;
            }
            throw new KeyNotFoundException($"Univers inconnu : {index}");
        }

        /// <summary>Retourne la liste des problemes detectes (vide si tout est OK).</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var controllerIds = new HashSet<string>();
            foreach (var c in Controllers) controllerIds.Add(c.Id);

            foreach (var u in Universes)
            {
                if (!controllerIds.Contains(u.ControllerId))
                {
                    problems.Add($"Univers {u.Index} reference un controleur inconnu {u.ControllerId}");
                }
            }
            foreach (var d in Devices)
            {
                var (_, end) = d.ChannelRange;
                if (end > DmxChannelsPerUniverse)
                {
                    problems.Add($"Appareil {d.Id} deborde de l'univers (canal {end} > {DmxChannelsPerUniverse})");
                }
            }
            var seen = new Dictionary<int, EntityMapping>();
            foreach (var m in EntityMap)
            {
                for (int id = m.EntityStart; id <= m.EntityEnd; id++)
                {
                    if (seen.TryGetValue(id, out var prev))
                    {
                        problems.Add($"Entite {id} mappee plusieurs fois ({prev.EntityStart}-{prev.EntityEnd} et {m.EntityStart}-{m.EntityEnd})");
                        break;
                    }
                    seen[id] = m;
                }
            }
            return problems;
        }
    }
}
