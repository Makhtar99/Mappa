using UnityEngine;

// Projecteur dynamique (lyre) 13 canaux DMX (source :
// https://learn.glassworks.tech/led/arch/other-devices).
//
// Convention: chaque canal DMX de la lyre est envoye comme UNE entite eHuB.
// L'entite baseEntityId + i porte l'octet du canal DMX i (0-indexe) de la lyre.
// Cote config (ecran.json), la plage doit etre declaree en led_type RAW1 avec
// entity_start = baseEntityId, entity_end = baseEntityId + ChannelCount - 1
// et channel_start = offset DMX du 1er canal de la lyre dans l'univers.
//
// Mapping ecran.json :
//   Lyre 1 : entites 10..22 -> canaux DMX 10..22 de l'univers 33
//   Lyre 2 : entites 30..42 -> canaux DMX 30..42
//   Lyre 3 : entites 50..62 -> canaux DMX 50..62
//   Lyre 4 : entites 70..82 -> canaux DMX 70..82
//
// L'ordre exact des 13 canaux depend du modele physique et n'est pas donne
// par la doc glassworks. On applique un profil courant tete mobile 13 canaux :
// pan16 (2 canaux) + tilt16 (2) + speed + dimmer + strobe + R + G + B + W +
// macro. Ajuster l'ordre ci-dessous SANS toucher au routing : seule la
// sequence des octets change.
public sealed class LyreController : MonoBehaviour
{
    public DeviceEmitter emitter;
    public int baseEntityId = 10;
    public const int ChannelCount = 13;

    [Header("Controle")]
    [Range(0f, 1f)] public float pan = 0.5f;
    [Range(0f, 1f)] public float tilt = 0.5f;
    [Range(0f, 1f)] public float speed = 0f;
    [Range(0f, 1f)] public float dimmer = 1f;
    [Range(0f, 1f)] public float strobe = 0f;
    public Color color = Color.white;
    [Range(0f, 1f)] public float white = 0f;
    [Range(0f, 1f)] public float macro = 0f; // 13e canal : macro couleur / gobo

    private readonly byte[] _ch = new byte[ChannelCount];

    private void Update()
    {
        if (emitter == null) return;

        int pan16 = (int)(Mathf.Clamp01(pan) * 65535f);
        int tilt16 = (int)(Mathf.Clamp01(tilt) * 65535f);

        _ch[0]  = (byte)(pan16 >> 8);      // canal 1  : pan (8 bits hauts)
        _ch[1]  = (byte)(pan16 & 0xFF);    // canal 2  : pan fine
        _ch[2]  = (byte)(tilt16 >> 8);     // canal 3  : tilt (8 bits hauts)
        _ch[3]  = (byte)(tilt16 & 0xFF);   // canal 4  : tilt fine
        _ch[4]  = (byte)(speed * 255f);    // canal 5  : speed
        _ch[5]  = (byte)(dimmer * 255f);   // canal 6  : dimmer
        _ch[6]  = (byte)(strobe * 255f);   // canal 7  : strobe
        _ch[7]  = (byte)(color.r * 255f);  // canal 8  : red
        _ch[8]  = (byte)(color.g * 255f);  // canal 9  : green
        _ch[9]  = (byte)(color.b * 255f);  // canal 10 : blue
        _ch[10] = (byte)(white * 255f);    // canal 11 : white
        _ch[11] = (byte)(macro * 255f);    // canal 12 : macro couleur / gobo
        _ch[12] = 0;                       // canal 13 : auto/reset (manuel)

        // 1 canal DMX = 1 entite eHuB. L'octet part dans le champ R; le
        // routing C# (LedType.RAW1) le copie tel quel au bon canal DMX.
        for (int i = 0; i < ChannelCount; i++)
        {
            emitter.Set(baseEntityId + i, _ch[i], 0, 0, 0);
        }
    }
}
