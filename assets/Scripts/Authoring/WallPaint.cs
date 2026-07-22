using UnityEngine;

public static class WallPaint
{
    public static Color32 ToColor32(Color c, float k)
    {
        return new Color32(
            (byte)(Mathf.Clamp01(c.r * k) * 255f),
            (byte)(Mathf.Clamp01(c.g * k) * 255f),
            (byte)(Mathf.Clamp01(c.b * k) * 255f),
            255);
    }
}
