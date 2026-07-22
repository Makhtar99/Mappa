using UnityEngine;

// Projecteur DMX en convention "1 canal = 1 entite" (RAW1). Chaque canal DMX
// est envoye comme une entite separee: baseEntityId + i porte l'octet du
// canal DMX i (0-indexe) du projecteur. L'ordre par defaut (dimmer, R, G, B, W)
// est courant mais ajustable en changeant les affectations _ch ci-dessous.
public sealed class ProjectorController : MonoBehaviour
{
    public DeviceEmitter emitter;
    // Note: baseEntityId au lieu de entityId. Le projecteur occupe channelCount
    // entites consecutives, une par canal DMX. Defaut = 1 pour matcher l'Excel
    // eHuB (Projector: Entity Start=1, Entity End=1).
    public int baseEntityId = 1;

    [Header("Controle")]
    public Color color = Color.white;
    [Range(0f, 1f)] public float white = 0f;
    [Range(0f, 1f)] public float dimmer = 1f;

    [Header("DMX")]
    // Nombre de canaux emis :
    //   1 = mono-canal (juste dimmer, cas de l'Excel actuel: Projector Entity 1..1)
    //   3 = RGB pur (color.r/g/b)
    //   5 = dimmer + R + G + B + W (defaut precedent)
    // Ajuster selon le profil DMX reel du projecteur.
    [Range(1, 16)] public int channelCount = 1;

    private readonly byte[] _ch = new byte[16];

    private void Update()
    {
        if (emitter == null) return;

        // Ordre par defaut: dimmer, R, G, B, W. Les canaux au-dela restent a 0.
        for (int i = 0; i < _ch.Length; i++) _ch[i] = 0;
        if (channelCount >= 1) _ch[0] = (byte)(Mathf.Clamp01(dimmer) * 255f);
        if (channelCount >= 2) _ch[1] = (byte)(Mathf.Clamp01(color.r) * 255f);
        if (channelCount >= 3) _ch[2] = (byte)(Mathf.Clamp01(color.g) * 255f);
        if (channelCount >= 4) _ch[3] = (byte)(Mathf.Clamp01(color.b) * 255f);
        if (channelCount >= 5) _ch[4] = (byte)(Mathf.Clamp01(white) * 255f);

        int n = Mathf.Clamp(channelCount, 1, _ch.Length);
        for (int i = 0; i < n; i++)
        {
            emitter.Set(baseEntityId + i, _ch[i], 0, 0, 0);
        }
    }
}
