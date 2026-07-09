using UnityEngine;

[DefaultExecutionOrder(500)]
public sealed class CameraSampler : MonoBehaviour
{
    public Camera source;
    public int resolution = 128;
    public float brightness = 1f;

    private RenderTexture _rt;
    private Texture2D _tex;

    private void Start()
    {
        if (source == null) source = GetComponent<Camera>();
        _rt = new RenderTexture(resolution, resolution, 16) { name = "WallStage" };
        _tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        if (source != null)
        {
            source.targetTexture = _rt;
            source.enabled = false;
        }
    }

    private void Update()
    {
        var f = EntityField.Instance;
        if (f == null || source == null || _rt == null) return;

        source.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
        _tex.Apply(false);
        RenderTexture.active = prev;

        var px = _tex.GetPixels32();
        int w = _rt.width, h = _rt.height;

        for (int i = 0; i < f.Ids.Length; i++)
        {
            Vector2 uv = f.Norm[i];
            int x = Mathf.Clamp((int)(uv.x * (w - 1)), 0, w - 1);
            int y = Mathf.Clamp((int)(uv.y * (h - 1)), 0, h - 1);
            Color32 c = px[y * w + x];
            f.SetColor(i, new Color32(
                (byte)Mathf.Min(255, (int)(c.r * brightness)),
                (byte)Mathf.Min(255, (int)(c.g * brightness)),
                (byte)Mathf.Min(255, (int)(c.b * brightness)),
                255));
        }
    }

    private void OnDestroy()
    {
        if (source != null) source.targetTexture = null;
        if (_rt != null) _rt.Release();
    }
}
