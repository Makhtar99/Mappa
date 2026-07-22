using UnityEngine;

// Projecteur statique 4 canaux DMX = R, V, B, W (source :
// https://learn.glassworks.tech/led/arch/other-devices).
//
// Cote reseau : convention "1 canal = 1 entite eHuB" (RAW1). Chaque canal DMX
// est envoye comme une entite separee : baseEntityId + i porte l'octet du
// canal DMX i (0-indexe) du projecteur.
//
// Mapping ecran.json : entites 1..4 -> canaux DMX 1..4 de l'univers 33.
public sealed class ProjectorController : MonoBehaviour
{
    public DeviceEmitter emitter;

    // Le projecteur occupe 4 entites consecutives (une par canal DMX).
    // Defaut = 1 pour matcher l'Excel eHuB (Projector: Entity Start=1) et
    // la doc glassworks (canaux DMX 1..4).
    public int baseEntityId = 1;

    [Header("Controle")]
    public Color color = Color.white;
    [Range(0f, 1f)] public float white = 0f;
    // Attenuation appliquee logiciellement sur R, V, B, W (le projecteur n'a
    // pas de canal dimmer separe : les 4 canaux sont directement R, V, B, W).
    [Range(0f, 1f)] public float dimmer = 1f;

    // Nombre de canaux DMX du projecteur. Fixe a 4 par la doc glassworks
    // (canaux 1..4 = R, V, B, W). Expose pour reste ajustable si le profil
    // reel differe.
    public const int ChannelCount = 4;

    private readonly byte[] _ch = new byte[ChannelCount];

    private void Update()
    {
        if (emitter == null) return;

        float k = Mathf.Clamp01(dimmer);
        _ch[0] = (byte)(Mathf.Clamp01(color.r) * k * 255f); // canal 1 : R
        _ch[1] = (byte)(Mathf.Clamp01(color.g) * k * 255f); // canal 2 : V
        _ch[2] = (byte)(Mathf.Clamp01(color.b) * k * 255f); // canal 3 : B
        _ch[3] = (byte)(Mathf.Clamp01(white)   * k * 255f); // canal 4 : W

        for (int i = 0; i < ChannelCount; i++)
        {
            emitter.Set(baseEntityId + i, _ch[i], 0, 0, 0);
        }
    }
}
