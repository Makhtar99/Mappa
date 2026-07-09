using System.Collections.Generic;

namespace Mappa
{
    /// <summary>
    /// Adresse physique resolue d'une entite. Channel est 0-indexe dans l'univers
    /// (offset dans le paquet DMX de 512 octets). Channels vaut 3 (RGB) ou 4 (RGBW).
    /// </summary>
    public readonly struct EntityAddress
    {
        public readonly int EntityId;
        public readonly int Universe;
        public readonly int Channel;
        public readonly int Channels;

        public EntityAddress(int entityId, int universe, int channel, int channels)
        {
            EntityId = entityId;
            Universe = universe;
            Channel = channel;
            Channels = channels;
        }
    }

    /// <summary>
    /// Resolution entite -> adresse physique (le pont vers le routage A).
    ///
    /// Ce module ne fait PAS d'ArtNet/UDP (travail de la Personne A). Il fournit
    /// a A ce dont il a besoin : ou va chaque entite, sous forme d'un plan
    /// precalcule. A lit alors les octets RVBW du state et les depose aux bonnes
    /// positions via Render.
    ///
    /// Modele d'adressage : chaque entite occupe LedType.Channels() canaux
    /// consecutifs a partir de (UniverseStart, ChannelStart) de sa plage, avec
    /// debordement automatique quand on depasse 512 canaux. Ordre R, V, B (, W).
    /// </summary>
    public sealed class RoutingPlan
    {
        private readonly Dictionary<int, EntityAddress> _addresses = new Dictionary<int, EntityAddress>();
        private readonly List<int> _universes = new List<int>();
        private readonly Dictionary<int, byte[]> _packets = new Dictionary<int, byte[]>();

        public RoutingPlan(Config config)
        {
            var universeSet = new SortedSet<int>();
            foreach (var mapping in config.EntityMap)
            {
                int chPerLed = mapping.LedType.Channels();
                int universe = mapping.UniverseStart;
                int channel = mapping.ChannelStart;
                for (int eid = mapping.EntityStart; eid <= mapping.EntityEnd; eid++)
                {
                    if (channel + chPerLed > Config.DmxChannelsPerUniverse)
                    {
                        universe += 1;
                        channel = 0;
                    }
                    _addresses[eid] = new EntityAddress(eid, universe, channel, chPerLed);
                    universeSet.Add(universe);
                    channel += chPerLed;
                }
            }
            foreach (var device in config.Devices)
            {
                universeSet.Add(device.Universe);
            }
            _universes.AddRange(universeSet);
            foreach (var u in _universes)
            {
                _packets[u] = new byte[Config.DmxChannelsPerUniverse];
            }
        }

        public EntityAddress? AddressOf(int entityId)
        {
            return _addresses.TryGetValue(entityId, out var addr) ? addr : (EntityAddress?)null;
        }

        public IReadOnlyList<int> Universes => _universes;

        public int Count => _addresses.Count;

        /// <summary>
        /// Projette un state sur les paquets DMX (un byte[512] par univers).
        /// Les buffers sont reutilises (remis a zero puis remplis), donc le dict
        /// retourne ne doit pas etre conserve entre deux frames.
        /// </summary>
        public IReadOnlyDictionary<int, byte[]> Render(State state)
        {
            foreach (var pkt in _packets.Values)
            {
                System.Array.Clear(pkt, 0, pkt.Length);
            }

            byte[] buf = state.Buffer;
            foreach (var kv in _addresses)
            {
                int slot = state.SlotOf(kv.Key);
                if (slot < 0) continue;
                int baseIdx = slot * 4; // buffer state = 4 octets RVBW / entite
                var addr = kv.Value;
                byte[] pkt = _packets[addr.Universe];
                int ch = addr.Channel;
                pkt[ch] = buf[baseIdx];         // R
                pkt[ch + 1] = buf[baseIdx + 1]; // V
                pkt[ch + 2] = buf[baseIdx + 2]; // B
                if (addr.Channels == 4)
                {
                    pkt[ch + 3] = buf[baseIdx + 3]; // W
                }
            }
            return _packets;
        }
    }
}
