using UnityEngine;

public sealed class WallDemoAnimator : MonoBehaviour
{
    public float speed = 0.5f;
    public float scale = 5f;

    private float _t;

    private void Update()
    {
        var f = EntityField.Instance;
        if (f == null) return;

        _t += Time.deltaTime * speed;
        for (int i = 0; i < f.Ids.Length; i++)
        {
            Vector2 uv = f.Norm[i];
            float x = uv.x * scale;
            float y = uv.y * scale;
            float v = Mathf.Sin(x + _t) + Mathf.Sin(y - _t) + Mathf.Sin((x + y) * 0.5f + _t);
            float h = Mathf.Repeat((v + 3f) / 6f, 1f);
            Color c = Color.HSVToRGB(h, 1f, 1f);
            f.SetColor(i, (Color32)c);
        }
    }
}
