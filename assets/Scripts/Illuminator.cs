using UnityEngine;

public sealed class Illuminator : MonoBehaviour
{
    public Color color = Color.white;
    public float radius = 0.4f;
    [Range(0f, 1f)] public float intensity = 1f;
    public bool smooth = true;

    private void Update()
    {
        var f = EntityField.Instance;
        if (f == null) return;

        Vector3 p = transform.position;
        float r2 = radius * radius;

        for (int i = 0; i < f.World.Length; i++)
        {
            Vector3 d = f.World[i] - p;
            float sq = d.x * d.x + d.y * d.y + d.z * d.z;
            if (sq > r2) continue;

            float k = intensity;
            if (smooth) k *= 1f - Mathf.Sqrt(sq) / radius;
            if (k <= 0f) continue;

            f.AddColor(i, new Color32(
                (byte)(color.r * k * 255f),
                (byte)(color.g * k * 255f),
                (byte)(color.b * k * 255f),
                (byte)(color.a * k * 255f)));
        }
    }
}
