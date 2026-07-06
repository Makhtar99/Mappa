using System.Buffers.Binary;

namespace LedNetwork.Core.DesignProtocol;

/// <summary>
/// Message du protocole UDP personnalisé émis par l'outil de conception artistique.
/// Il décrit l'« état souhaité » de l'installation : une liste d'entités (pixels,
/// rubans, fixtures) avec leur couleur RGB(W).
///
/// Format binaire (little-endian) :
///   [0..3]   Magic  = "WISH" (0x57 0x49 0x53 0x48)
///   [4]      Version (1)
///   [5..8]   FrameId (uint32) — numéro de frame de l'animation
///   [9..10]  EntityCount (uint16)
///   puis, par entité (7 octets) :
///     EntityId (uint16) | R (u8) | G (u8) | B (u8) | W (u8) | Intensity (u8)
///
/// C'est un point de départ : adaptez le format à ce que produit réellement
/// votre outil de conception.
/// </summary>
public sealed class DesignStateMessage
{
    public static readonly byte[] Magic = { (byte)'W', (byte)'I', (byte)'S', (byte)'H' };
    public const byte CurrentVersion = 1;

    public uint FrameId { get; set; }
    public List<EntityColor> Entities { get; set; } = new();

    public byte[] Serialize()
    {
        var buffer = new byte[11 + Entities.Count * 7];
        var span = buffer.AsSpan();

        Magic.CopyTo(span);
        span[4] = CurrentVersion;
        BinaryPrimitives.WriteUInt32LittleEndian(span[5..], FrameId);
        BinaryPrimitives.WriteUInt16LittleEndian(span[9..], (ushort)Entities.Count);

        int offset = 11;
        foreach (var e in Entities)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], e.EntityId);
            span[offset + 2] = e.R;
            span[offset + 3] = e.G;
            span[offset + 4] = e.B;
            span[offset + 5] = e.W;
            span[offset + 6] = e.Intensity;
            offset += 7;
        }

        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out DesignStateMessage? message)
    {
        message = null;
        if (data.Length < 11) return false;
        if (!data[..4].SequenceEqual(Magic)) return false;
        if (data[4] != CurrentVersion) return false;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[9..]);
        if (data.Length < 11 + count * 7) return false;

        var msg = new DesignStateMessage
        {
            FrameId = BinaryPrimitives.ReadUInt32LittleEndian(data[5..])
        };

        int offset = 11;
        for (int i = 0; i < count; i++)
        {
            msg.Entities.Add(new EntityColor
            {
                EntityId = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]),
                R = data[offset + 2],
                G = data[offset + 3],
                B = data[offset + 4],
                W = data[offset + 5],
                Intensity = data[offset + 6]
            });
            offset += 7;
        }

        message = msg;
        return true;
    }
}

/// <summary>Couleur d'une entité de l'installation (RGBW + intensité maître).</summary>
public struct EntityColor
{
    public ushort EntityId;
    public byte R, G, B, W, Intensity;
}
