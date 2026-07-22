using UnityEngine;

// Projecteur statique 4 canaux DMX = R, V, B, W (source :
// https://learn.glassworks.tech/led/arch/other-devices).
//
// Cote reseau : convention "1 canal = 1 entite eHuB" (RAW1). Chaque canal DMX
// est envoye comme une entite separee : baseEntityId + i porte l'octet du
// canal DMX i (0-indexe) du projecteur.
//
// Mapping ecran.json : entites 1..4 -> canaux DMX 1..4 de l'univers 33.
//
// Controle depuis Unity :
//   - Cocher/decocher "isOn" : allume ou eteint le projo (dimmer force a 0).
//   - "color" : couleur RGB (curseurs classiques dans l'Inspector).
//   - "white" : canal W separe (0..1).
//   - "dimmer" : attenuation globale 0..1 (multiplie R, V, B et W).
//
// Depuis un script tiers : SetColor(...), SetOn(bool), TurnOff(), Blink(...).
public sealed class ProjectorController : MonoBehaviour
{
    public DeviceEmitter emitter;

    // Le projecteur occupe 4 entites consecutives (une par canal DMX).
    // Defaut = 1 pour matcher l'Excel eHuB (Projector: Entity Start=1) et
    // la doc glassworks (canaux DMX 1..4).
    public int baseEntityId = 1;

    [Header("Controle")]
    // Interrupteur logique : decoche pour eteindre completement sans perdre
    // les autres reglages (color/white/dimmer sont conserves).
    public bool isOn = true;

    public Color color = Color.white;
    [Range(0f, 1f)] public float white = 0f;

    // Attenuation appliquee logiciellement sur R, V, B, W (le projecteur n'a
    // pas de canal dimmer separe : les 4 canaux sont directement R, V, B, W).
    [Range(0f, 1f)] public float dimmer = 1f;

    // Nombre de canaux DMX du projecteur. Fixe a 4 par la doc glassworks
    // (canaux 1..4 = R, V, B, W).
    public const int ChannelCount = 4;

    private readonly byte[] _ch = new byte[ChannelCount];

    private void Update()
    {
        if (emitter == null) return;

        // Si eteint, on envoie 0 partout mais on continue de "pulser" les
        // canaux (sinon le controleur DMX peut garder la derniere valeur).
        float k = isOn ? Mathf.Clamp01(dimmer) : 0f;
        _ch[0] = (byte)(Mathf.Clamp01(color.r) * k * 255f); // canal 1 : R
        _ch[1] = (byte)(Mathf.Clamp01(color.g) * k * 255f); // canal 2 : V
        _ch[2] = (byte)(Mathf.Clamp01(color.b) * k * 255f); // canal 3 : B
        _ch[3] = (byte)(Mathf.Clamp01(white)   * k * 255f); // canal 4 : W

        for (int i = 0; i < ChannelCount; i++)
        {
            emitter.Set(baseEntityId + i, _ch[i], 0, 0, 0);
        }
    }

    // ------------------------------------------------------------------ //
    // API publique pour piloter le projo depuis d'autres scripts, boutons
    // Unity UI, animations Timeline, etc.
    // ------------------------------------------------------------------ //

    /// <summary>Allume / eteint le projecteur (raccourci pour isOn).</summary>
    public void SetOn(bool on) => isOn = on;

    /// <summary>Toggle on/off.</summary>
    public void Toggle() => isOn = !isOn;

    /// <summary>Eteint le projecteur (equivalent a SetOn(false)).</summary>
    public void TurnOff() => isOn = false;

    /// <summary>Allume le projecteur avec la couleur actuelle.</summary>
    public void TurnOn() => isOn = true;

    /// <summary>Change la couleur RGB et allume le projecteur.</summary>
    public void SetColor(Color c)
    {
        color = c;
        isOn = true;
    }

    /// <summary>Change la couleur en composantes 0..1 et allume.</summary>
    public void SetColor(float r, float g, float b)
    {
        color = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
        isOn = true;
    }

    /// <summary>Change l'intensite du canal W (0..1).</summary>
    public void SetWhite(float w) => white = Mathf.Clamp01(w);

    /// <summary>Change le dimmer global (0..1).</summary>
    public void SetDimmer(float d) => dimmer = Mathf.Clamp01(d);
}
