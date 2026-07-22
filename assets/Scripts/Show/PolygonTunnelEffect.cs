using UnityEngine;

public sealed class PolygonTunnelEffect : MonoBehaviour, IShowEffect
{
    public float Intensity { get; set; }

    public int sides = 6;
    public int rings = 6;
    public float maxRadius = 0.72f;
    public float thickness = 0.03f;
    public float scrollSpeed = 0.25f;
    public float rotSpeed = 0.3f;
    public Color edgeColor = new Color(1f, 0.15f, 0.2f);
    public Color hotColor = new Color(1f, 0.85f, 0.85f);

    private void Update()
    {
        if (Intensity <= 0.001f) return;
        var f = EntityField.Instance;
        if (f == null) return;

        float scroll = Time.time * scrollSpeed;
        float rot = Time.time * rotSpeed;
        float sector = Mathf.PI * 2f / sides;
        float cosHalf = Mathf.Cos(Mathf.PI / sides);
        float pulse = 0.6f + 0.4f * ShowAudio.Level;

        for (int i = 0; i < f.Norm.Length; i++)
        {
            float dx = f.Norm[i].x - 0.5f;
            float dy = f.Norm[i].y - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > maxRadius) continue;
            float ang = Mathf.Atan2(dy, dx);

            float best = 0f;
            for (int k = 0; k < rings; k++)
            {
                float frac = Mathf.Repeat((float)k / rings + scroll, 1f);
                float ringR = frac * maxRadius + 0.03f;
                float a = ang - (rot + k * 0.4f);
                float aa = Mathf.Repeat(a + sector * 0.5f, sector) - sector * 0.5f;
                float polyR = ringR * cosHalf / Mathf.Cos(aa);
                float edge = 1f - Mathf.Clamp01(Mathf.Abs(r - polyR) / thickness);
                edge *= frac;
                if (edge > best) best = edge;
            }

            float kk = Mathf.Clamp01(best * pulse * Intensity);
            if (kk <= 0f) continue;

            Color c = Color.Lerp(edgeColor, hotColor, best * 0.4f);
            f.AddColor(i, new Color32(
                (byte)(Mathf.Clamp01(c.r * kk) * 255f),
                (byte)(Mathf.Clamp01(c.g * kk) * 255f),
                (byte)(Mathf.Clamp01(c.b * kk) * 255f),
                255));
        }
    }
}
