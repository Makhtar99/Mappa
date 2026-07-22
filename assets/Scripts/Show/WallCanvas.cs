using UnityEngine;

public sealed class WallCanvas
{
    public readonly int res;
    private readonly float[] _r, _g, _b;

    public WallCanvas(int res)
    {
        this.res = res;
        int n = res * res;
        _r = new float[n];
        _g = new float[n];
        _b = new float[n];
    }

    public void Fade(float keep)
    {
        for (int i = 0; i < _r.Length; i++)
        {
            _r[i] *= keep;
            _g[i] *= keep;
            _b[i] *= keep;
        }
    }

    public void AddPixel(int x, int y, float r, float g, float b)
    {
        if ((uint)x >= (uint)res || (uint)y >= (uint)res) return;
        int i = y * res + x;
        _r[i] += r; _g[i] += g; _b[i] += b;
    }

    public void Dot(float cx, float cy, float radius, float r, float g, float b, float a)
    {
        if (radius < 0.5f) radius = 0.5f;
        int x0 = Mathf.FloorToInt(cx - radius), x1 = Mathf.CeilToInt(cx + radius);
        int y0 = Mathf.FloorToInt(cy - radius), y1 = Mathf.CeilToInt(cy + radius);
        float inv = 1f / radius;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * inv;
                if (d >= 1f) continue;
                float w = (1f - d) * a;
                AddPixel(x, y, r * w, g * w, b * w);
            }
        }
    }

    public void Line(float x0, float y0, float x1, float y1, float width, float r, float g, float b, float a)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        int steps = Mathf.Max(1, Mathf.CeilToInt(len));
        float rad = Mathf.Max(0.5f, width * 0.5f);
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            Dot(x0 + dx * t, y0 + dy * t, rad, r, g, b, a);
        }
    }

    public void BlitToField(EntityField f, float gain, float rot, float zoom, float shx, float shy)
    {
        if (f == null) return;
        float c = res * 0.5f;
        float cos = Mathf.Cos(-rot), sin = Mathf.Sin(-rot);
        float invZoom = 1f / Mathf.Max(0.01f, zoom);

        for (int k = 0; k < f.Norm.Length; k++)
        {
            float px = f.Norm[k].x * (res - 1);
            float py = (1f - f.Norm[k].y) * (res - 1);
            float dx = (px - c) * invZoom, dy = (py - c) * invZoom;
            float rx = dx * cos - dy * sin;
            float ry = dx * sin + dy * cos;
            int sx = Mathf.RoundToInt(c + rx - shx);
            int sy = Mathf.RoundToInt(c + ry - shy);
            if ((uint)sx >= (uint)res || (uint)sy >= (uint)res) { f.SetColor(k, new Color32(0, 0, 0, 255)); continue; }
            int i = sy * res + sx;
            f.SetColor(k, new Color32(
                (byte)Mathf.Min(255f, _r[i] * gain),
                (byte)Mathf.Min(255f, _g[i] * gain),
                (byte)Mathf.Min(255f, _b[i] * gain),
                255));
        }
    }
}
