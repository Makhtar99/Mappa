using LedNetwork.Core.ArtNet;
using LedNetwork.Core.DesignProtocol;

namespace LedNetwork.Core.Routing;

/// <summary>Ordre des composantes de couleur sur les canaux DMX du ruban.</summary>
public enum ColorOrder
{
    Rgb,
    Rgbw,
    Grb,   // fréquent sur les rubans WS2812
    Grbw
}

/// <summary>
/// Décrit où une entité de l'installation est câblée : sur quel univers Art-Net
/// et à partir de quel canal DMX, dans quel ordre de couleur. C'est une ligne
/// de la table de patch de l'outil de routage.
/// </summary>
public sealed class FixturePatch
{
    public ushort EntityId { get; init; }
    public ushort Universe { get; init; }

    /// <summary>Canal DMX de départ (1..512, convention DMX 1-based).</summary>
    public int StartChannel { get; init; }

    public ColorOrder ColorOrder { get; init; } = ColorOrder.Rgb;

    /// <summary>Écrit la couleur de l'entité (intensité appliquée) dans le tampon DMX de l'univers.</summary>
    public void Write(byte[] dmx, EntityColor entity)
    {
        int i = StartChannel - 1; // conversion 1-based -> index 0-based
        if (i < 0) return;

        byte r = Scale(entity.R, entity.Intensity);
        byte g = Scale(entity.G, entity.Intensity);
        byte b = Scale(entity.B, entity.Intensity);
        byte w = Scale(entity.W, entity.Intensity);

        switch (ColorOrder)
        {
            case ColorOrder.Rgb:  WriteChannels(dmx, i, r, g, b); break;
            case ColorOrder.Grb:  WriteChannels(dmx, i, g, r, b); break;
            case ColorOrder.Rgbw: WriteChannels(dmx, i, r, g, b, w); break;
            case ColorOrder.Grbw: WriteChannels(dmx, i, g, r, b, w); break;
        }
    }

    private static void WriteChannels(byte[] dmx, int start, params byte[] values)
    {
        for (int k = 0; k < values.Length; k++)
        {
            int idx = start + k;
            if (idx >= 0 && idx < ArtNetConstants.DmxChannelsPerUniverse)
                dmx[idx] = values[k];
        }
    }

    /// <summary>Applique l'intensité maître (0..255) à une composante de couleur.</summary>
    private static byte Scale(byte value, byte intensity) => (byte)(value * intensity / 255);
}
