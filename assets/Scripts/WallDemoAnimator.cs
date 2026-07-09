using UnityEngine;

public sealed class WallDemoAnimator : MonoBehaviour
{
    public Color color = Color.blue;
    public float speed = 0.5f;
    public float waves = 2f;

    private void Update()
    {
        var f = EntityField.Instance;
        if (f == null) return;

        float t = Time.time * speed;
        for (int i = 0; i < f.Ids.Length; i++)
        {
            float x = f.Norm[i].x;
            float b = 0.5f + 0.5f * Mathf.Sin((x * waves - t) * Mathf.PI * 2f);
            f.SetColor(i, new Color32(
                (byte)(color.r * b * 255f),
                (byte)(color.g * b * 255f),
                (byte)(color.b * b * 255f),
                255));
        }
    }
}
