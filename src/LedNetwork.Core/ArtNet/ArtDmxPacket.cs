using System.Buffers.Binary;

namespace LedNetwork.Core.ArtNet;

/// <summary>
/// Paquet ArtDMX (OpOutput) : transporte une trame DMX512 (jusqu'à 512 canaux)
/// vers un univers Art-Net donné. C'est le paquet que l'outil de routage envoie
/// aux contrôleurs BC216.
///
/// L'adresse de port Art-Net (Port-Address) est un identifiant 15 bits :
///   Net (7 bits) | SubNet (4 bits) | Universe (4 bits)
/// Dans le paquet, elle est répartie sur deux octets : SubUni (octet bas) et Net.
/// </summary>
public sealed class ArtDmxPacket
{
    /// <summary>Numéro de séquence (1..255, 0 = désactivé) pour détecter les paquets hors ordre.</summary>
    public byte Sequence { get; set; }

    /// <summary>Port physique d'entrée, purement informatif.</summary>
    public byte Physical { get; set; }

    /// <summary>Adresse de port Art-Net 15 bits (0..32767).</summary>
    public ushort PortAddress { get; set; }

    /// <summary>Données DMX : 1 à 512 octets (idéalement un nombre pair).</summary>
    public byte[] Data { get; set; } = new byte[ArtNetConstants.DmxChannelsPerUniverse];

    public byte Net => (byte)((PortAddress >> 8) & 0x7F);
    public byte SubUni => (byte)(PortAddress & 0xFF);

    /// <summary>Sérialise le paquet vers un tableau d'octets prêt à envoyer en UDP.</summary>
    public byte[] Serialize()
    {
        int length = Data.Length;
        if (length < 1 || length > ArtNetConstants.DmxChannelsPerUniverse)
            throw new InvalidOperationException($"Longueur DMX invalide : {length}");

        // La longueur DMX doit être paire (règle Art-Net) : on arrondit au pair supérieur.
        int paddedLength = length % 2 == 0 ? length : length + 1;

        var buffer = new byte[18 + paddedLength];
        var span = buffer.AsSpan();

        ArtNetConstants.Id.CopyTo(span);                                    // 0..7  : "Art-Net\0"
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], ArtNetConstants.OpDmx); // 8..9 : OpCode
        span[10] = ArtNetConstants.ProtocolVersionHi;                       // 10
        span[11] = ArtNetConstants.ProtocolVersionLo;                       // 11
        span[12] = Sequence;                                               // 12
        span[13] = Physical;                                              // 13
        span[14] = SubUni;                                                // 14 : octet bas de l'adresse
        span[15] = Net;                                                   // 15 : octet haut (Net)
        BinaryPrimitives.WriteUInt16BigEndian(span[16..], (ushort)paddedLength); // 16..17 : longueur (big-endian)

        Data.CopyTo(span[18..]);                                          // 18.. : données DMX

        return buffer;
    }

    /// <summary>Tente de parser un datagramme reçu en un ArtDmxPacket. Retourne false si ce n'est pas de l'ArtDMX.</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out ArtDmxPacket? packet)
    {
        packet = null;
        if (data.Length < 18) return false;
        if (!data[..8].SequenceEqual(ArtNetConstants.Id)) return false;

        ushort opCode = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        if (opCode != ArtNetConstants.OpDmx) return false;

        int length = BinaryPrimitives.ReadUInt16BigEndian(data[16..]);
        if (length < 1 || length > ArtNetConstants.DmxChannelsPerUniverse) return false;
        if (data.Length < 18 + length) return false;

        packet = new ArtDmxPacket
        {
            Sequence = data[12],
            Physical = data[13],
            PortAddress = (ushort)(((data[15] & 0x7F) << 8) | data[14]),
            Data = data.Slice(18, length).ToArray()
        };
        return true;
    }
}
