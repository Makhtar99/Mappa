using UnityEngine;

public sealed class RadialBurstEffect : MonoBehaviour, IShowEffect
{
    public float Intensity { get; set; }

    public int rays = 22;
    public float sharpness = 5f;
    public float maxRadius = 0.72f;
    public float spin = 0.15f;
    public Color rayColor = new Color(1f, 0.1f, 0.15f);
    public Color coreColor = new Color(1f, 0.9f, 0.8f);

    private void Update()
    {
        if (Intensity <= 0.001f) return;
        var f = EntityField.Instance;
        if (f == null) return;

        float t = Time.time * spin;
        float pulse = 0.55f + 0.45f * ShowAudio.Level + ShowAudio.Bass * 0.4f;

        for (int i = 0; i < f.Norm.Length; i++)
        {
            float dx = f.Norm[i].x - 0.5f;
            float dy = f.Norm[i].y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > maxRadius) continue;
            float ang = Mathf.Atan2(dy, dx);

            float wave = 0.5f + 0.5f * Mathf.Cos((ang + t) * rays);
            float ray = Mathf.Pow(wave, sharpness);
            float radial = Mathf.Clamp01(1f - r / maxRadius);
            float core = Mathf.Clamp01(1f - r / 0.14f);

            float rv = ray * radial * pulse;
            float k = Mathf.Clamp01((rv + core) * Intensity);
            if (k <= 0f) continue;

            Color c = Color.Lerp(rayColor, coreColor, Mathf.Clamp01(core + rv * 0.3f));
            f.AddColor(i, new Color32(
                (byte)(Mathf.Clamp01(c.r * k) * 255f),
                (byte)(Mathf.Clamp01(c.g * k) * 255f),
                (byte)(Mathf.Clamp01(c.b * k) * 255f),
                255));
        }
    }
}
