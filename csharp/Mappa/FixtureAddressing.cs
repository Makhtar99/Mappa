using System;
using System.Collections.Generic;

namespace Mappa
{
    /// <summary>
    /// Adressage des fixtures externes du tableau Ecran.xlsx
    /// (projecteur + 4 lyres sur Art-Net univers 33 / 192.168.1.48).
    ///
    /// Excel : chaque entité = 3 canaux RGB. Les lyres occupent 14 entités
    /// (42 canaux) empilées ; le projecteur est l'entité 1 au canal 168.
    /// </summary>
    public static class FixtureAddressing
    {
        public const int ArtNetUniverse = 33;
        public const string DefaultIp = "192.168.1.48";

        public const int ProjectorEntityId = 1;
        public const int LyreEntityCount = 14;
        public const int LyreChannelCount = 13;

        public static readonly int[] LyreBaseEntityIds = { 10, 30, 50, 70 };

        /// <summary>Offset DMX 0-indexé d'une entité fixture sur l'univers 33.</summary>
        public static int DmxChannelOf(int entityId)
        {
            if (entityId == ProjectorEntityId) return 168;
            if (entityId >= 10 && entityId <= 23) return (entityId - 10) * 3;
            if (entityId >= 30 && entityId <= 43) return 42 + (entityId - 30) * 3;
            if (entityId >= 50 && entityId <= 63) return 84 + (entityId - 50) * 3;
            if (entityId >= 70 && entityId <= 83) return 126 + (entityId - 70) * 3;
            return -1;
        }

        /// <summary>
        /// Remplit les 13 canaux DMX d'une lyre (pan/tilt 16-bit, dimmer, RGB…).
        /// </summary>
        public static void FillLyreChannels(
            byte[] ch13,
            float pan, float tilt, float speed, float dimmer, float strobe,
            byte r, byte g, byte b, byte white)
        {
            if (ch13 == null || ch13.Length < LyreChannelCount)
                throw new ArgumentException("Buffer lyre : 13 octets requis.");

            int pan16 = (int)(Clamp01(pan) * 65535f);
            int tilt16 = (int)(Clamp01(tilt) * 65535f);

            ch13[0] = (byte)(pan16 >> 8);
            ch13[1] = (byte)(pan16 & 0xFF);
            ch13[2] = (byte)(tilt16 >> 8);
            ch13[3] = (byte)(tilt16 & 0xFF);
            ch13[4] = (byte)(Clamp01(speed) * 255f);
            ch13[5] = (byte)(Clamp01(dimmer) * 255f);
            ch13[6] = (byte)(Clamp01(strobe) * 255f);
            ch13[7] = r;
            ch13[8] = g;
            ch13[9] = b;
            ch13[10] = white;
            ch13[11] = 0;
            ch13[12] = 0;
        }

        /// <summary>
        /// Emballe les canaux lyre en entités RGB Excel (baseEntityId .. +13).
        /// Retourne (entityId → R,G,B).
        /// </summary>
        public static Dictionary<int, (byte R, byte G, byte B)> PackLyreEntities(
            int baseEntityId, byte[] ch13)
        {
            var map = new Dictionary<int, (byte, byte, byte)>();
            for (int e = 0; e < LyreEntityCount; e++)
            {
                int c = e * 3;
                map[baseEntityId + e] = (
                    Get(ch13, c),
                    Get(ch13, c + 1),
                    Get(ch13, c + 2));
            }
            return map;
        }

        /// <summary>Écrit les entités RGB d'une lyre dans un buffer DMX 512.</summary>
        public static void WriteLyreToDmx(byte[] dmx512, int baseEntityId, byte[] ch13)
        {
            var packed = PackLyreEntities(baseEntityId, ch13);
            foreach (var kv in packed)
            {
                int ch = DmxChannelOf(kv.Key);
                if (ch < 0 || ch > 509) continue;
                dmx512[ch] = kv.Value.R;
                dmx512[ch + 1] = kv.Value.G;
                dmx512[ch + 2] = kv.Value.B;
            }
        }

        /// <summary>Écrit le projecteur (entité 1) en RGB au canal 168.</summary>
        public static void WriteProjectorToDmx(byte[] dmx512, byte r, byte g, byte b)
        {
            int ch = DmxChannelOf(ProjectorEntityId);
            dmx512[ch] = r;
            dmx512[ch + 1] = g;
            dmx512[ch + 2] = b;
        }

        private static byte Get(byte[] ch, int i) =>
            i < ch.Length ? ch[i] : (byte)0;

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
