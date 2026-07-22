using UnityEngine;

public sealed class StarSpinEffect : MonoBehaviour, IShowEffect
{
    public float Intensity { get; set; }

    public int petals = 6;
    public float baseRadius = 0.34f;
    public float petalDepth = 0.32f;
    public float spin = 0.4f;
    public float edge = 0.06f;
    public float jitter = 0.05f;
    public Color coreColor = new Color(1f, 0.92f, 0.85f);
    public Color tipColor = new Color(1f, 0.12f, 0.16f);

    private void Update()
    {
        if (Intensity <= 0.001f) return;
        var f = EntityField.Instance;
        if (f == null) return;

        float t = Time.time * spin;
        float grow = 1f + ShowAudio.Bass * 0.5f;
        float pulse = 0.7f + 0.3f * ShowAudio.Level;

        for (int i = 0; i < f.Norm.Length; i++)
        {
            float dx = f.Norm[i].x - 0.5f;
            float dy = f.Norm[i].y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);

            float wave = 0.55f + 0.45f * Mathf.Cos(petals * (ang - t));
            float jag = jitter * Mathf.Cos(petals * 3f * (ang + t * 0.5f));
            float starR = (baseRadius + petalDepth * wave + jag) * grow;

            float fill = 1f - Mathf.Clamp01((r - starR) / edge);
            float k = Mathf.Clamp01(fill * pulse * Intensity);
            if (k <= 0f) continue;

            Color c = Color.Lerp(coreColor, tipColor, Mathf.Clamp01(r / Mathf.Max(0.01f, starR)));
            f.AddColor(i, new Color32(
                (byte)(Mathf.Clamp01(c.r * k) * 255f),
                (byte)(Mathf.Clamp01(c.g * k) * 255f),
                (byte)(Mathf.Clamp01(c.b * k) * 255f),
                255));
        }
    }
}
