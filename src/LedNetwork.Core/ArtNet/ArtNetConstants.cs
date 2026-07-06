namespace LedNetwork.Core.ArtNet;

/// <summary>
/// Constantes du protocole Art-Net (spécification Art-Net 4, Artistic Licence).
/// </summary>
public static class ArtNetConstants
{
    /// <summary>En-tête ASCII présent au début de chaque paquet : "Art-Net\0".</summary>
    public static readonly byte[] Id = { (byte)'A', (byte)'r', (byte)'t', (byte)'-', (byte)'N', (byte)'e', (byte)'t', 0x00 };

    /// <summary>Port UDP standard Art-Net.</summary>
    public const int UdpPort = 6454;

    /// <summary>Version du protocole (14 = Art-Net 4).</summary>
    public const byte ProtocolVersionHi = 0x00;
    public const byte ProtocolVersionLo = 14;

    /// <summary>Nombre maximal de canaux DMX par univers.</summary>
    public const int DmxChannelsPerUniverse = 512;

    // OpCodes (little-endian dans le paquet)
    public const ushort OpPoll = 0x2000;
    public const ushort OpPollReply = 0x2100;
    public const ushort OpDmx = 0x5000; // ArtDMX / OpOutput
}
