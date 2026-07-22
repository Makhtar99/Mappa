using UnityEngine;

// Variante 8 bits de LyreController (au cas ou les vraies lyres n'auraient pas
// de canaux "fine" et fonctionneraient sur 9 canaux au lieu de 13).
//
// Profil DMX suppose (a verifier avec dmx_sweep_rotation.sh) :
//   canal 1 : pan   (0..255 = course complete horizontale)
//   canal 2 : tilt  (0..255 = course complete verticale)
//   canal 3 : speed (0 = plus rapide, 255 = plus lent, selon modele)
//   canal 4 : dimmer
//   canal 5 : strobe
//   canal 6 : red
//   canal 7 : green
//   canal 8 : blue
//   canal 9 : white
//
// Utilisation :
//   1. Desactive le composant LyreController d'origine sur le GameObject.
//   2. Ajoute LyreController8bit avec le meme baseEntityId.
//   3. Dans configs/ecran.json, reduis entity_end a baseEntityId+8 (9 entites).
//
// Ne pas confondre ChannelCount ici (9) avec celui du 13 bits (13). Le routing
// C# n'utilise pas cette constante : il se base sur entity_map dans le JSON.
public sealed class LyreController8bit : MonoBehaviour
{
    public DeviceEmitter emitter;
    public int baseEntityId = 10;
    public const int ChannelCount = 9;

    [Header("Controle")]
    [Range(0f, 1f)] public float pan = 0.5f;
    [Range(0f, 1f)] public float tilt = 0.5f;
    [Range(0f, 1f)] public float speed = 0f;
    [Range(0f, 1f)] public float dimmer = 1f;
    [Range(0f, 1f)] public float strobe = 0f;
    public Color color = Color.white;
    [Range(0f, 1f)] public float white = 0f;

    private readonly byte[] _ch = new byte[ChannelCount];

    private void Update()
    {
        if (emitter == null) return;

        _ch[0] = (byte)(Mathf.Clamp01(pan) * 255f);    // pan 8 bits
        _ch[1] = (byte)(Mathf.Clamp01(tilt) * 255f);   // tilt 8 bits
        _ch[2] = (byte)(speed * 255f);                 // speed
        _ch[3] = (byte)(dimmer * 255f);                // dimmer
        _ch[4] = (byte)(strobe * 255f);                // strobe
        _ch[5] = (byte)(color.r * 255f);               // red
        _ch[6] = (byte)(color.g * 255f);               // green
        _ch[7] = (byte)(color.b * 255f);               // blue
        _ch[8] = (byte)(white * 255f);                 // white

        for (int i = 0; i < ChannelCount; i++)
        {
            emitter.Set(baseEntityId + i, _ch[i], 0, 0, 0);
        }
    }
}
