using System;
using System.Collections.Generic;

namespace Mappa
{
    /// <summary>
    /// Couleur RVBW d'une entite (valeurs DMX 0-255).
    /// </summary>
    public readonly struct Color
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte W;

        public Color(byte r, byte g, byte b, byte w = 0)
        {
            R = r;
            G = g;
            B = b;
            W = w;
        }

        public static readonly Color Black = new Color(0, 0, 0, 0);
    }

    /// <summary>
    /// Le contrat du "state" : l'interface centrale entre authoring (C) et routage (A).
    ///
    /// Un state represente les valeurs RVBW de chaque entite (= un pixel / une LED)
    /// a un instant t. Une entite est identifiee par un ID unique (non sequentiel).
    ///
    /// Regle de contrat :
    ///  - L'authoring (C) ECRIT dans le state (Set / SetRgb / ...).
    ///  - Le routage (A) LIT le state (Buffer / Get).
    ///  - Aucun ne connait la representation interne de l'autre : ils ne
    ///    manipulent que des IDs d'entites et des couleurs RVBW.
    ///
    /// Performance : sur ~16 000 entites a 40 Hz, on utilise un buffer dense
    /// reutilise (4 octets RVBW / entite), indexe par un slot contigu, pour eviter
    /// toute allocation par frame.
    /// </summary>
    public sealed class State
    {
        private readonly int[] _ids;
        private readonly Dictionary<int, int> _idToSlot;
        private readonly byte[] _buffer;

        public double Timestamp { get; private set; }

        public State(IEnumerable<int> entityIds)
        {
            var list = new List<int>(entityIds);
            _ids = list.ToArray();
            _idToSlot = new Dictionary<int, int>(_ids.Length);
            for (int slot = 0; slot < _ids.Length; slot++)
            {
                _idToSlot[_ids[slot]] = slot;
            }
            _buffer = new byte[4 * _ids.Length];
            Timestamp = 0.0;
        }

        /// <summary>Cree un state dimensionne pour toutes les entites d'une config.</summary>
        public static State FromConfig(Config config)
        {
            return new State(config.EntityIds());
        }

        private static byte Clamp8(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }

        // -------------------------------------------------------------- //
        // Ecriture (authoring C)
        // -------------------------------------------------------------- //
        public void Set(int entityId, int r, int g, int b, int w = 0)
        {
            if (!_idToSlot.TryGetValue(entityId, out int slot))
            {
                return; // ID inconnu ignore : l'authoring reste decouple du dimensionnement
            }
            int baseIdx = slot * 4;
            _buffer[baseIdx] = Clamp8(r);
            _buffer[baseIdx + 1] = Clamp8(g);
            _buffer[baseIdx + 2] = Clamp8(b);
            _buffer[baseIdx + 3] = Clamp8(w);
        }

        public void SetRgb(int entityId, int r, int g, int b) => Set(entityId, r, g, b, 0);

        public void SetColor(int entityId, Color c) => Set(entityId, c.R, c.G, c.B, c.W);

        public void Fill(int r, int g, int b, int w = 0)
        {
            byte rr = Clamp8(r), gg = Clamp8(g), bb = Clamp8(b), ww = Clamp8(w);
            for (int i = 0; i < _buffer.Length; i += 4)
            {
                _buffer[i] = rr;
                _buffer[i + 1] = gg;
                _buffer[i + 2] = bb;
                _buffer[i + 3] = ww;
            }
        }

        public void Clear() => Array.Clear(_buffer, 0, _buffer.Length);

        /// <summary>Horodate le state (a appeler par C quand une frame est prete).</summary>
        public void MarkUpdated() => Timestamp = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;

        // -------------------------------------------------------------- //
        // Lecture (routage A)
        // -------------------------------------------------------------- //
        public Color Get(int entityId)
        {
            if (!_idToSlot.TryGetValue(entityId, out int slot))
            {
                return Color.Black;
            }
            int baseIdx = slot * 4;
            return new Color(_buffer[baseIdx], _buffer[baseIdx + 1],
                             _buffer[baseIdx + 2], _buffer[baseIdx + 3]);
        }

        /// <summary>Buffer brut RVBW (ordre des slots). Lecture seule cote routage.</summary>
        public byte[] Buffer => _buffer;

        /// <summary>Slot (offset entite) d'un ID, ou -1 si inconnu.</summary>
        public int SlotOf(int entityId) => _idToSlot.TryGetValue(entityId, out int slot) ? slot : -1;

        public IReadOnlyList<int> EntityIds => _ids;

        public int Count => _ids.Length;

        public bool Contains(int entityId) => _idToSlot.ContainsKey(entityId);
    }
}
