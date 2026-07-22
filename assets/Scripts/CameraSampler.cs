using UnityEngine;

[DefaultExecutionOrder(500)]
public sealed class CameraSampler : MonoBehaviour
{
    public Camera source;
    [Tooltip("Resolution de capture. > largeur du mur = supersampling (bords plus doux).")]
    public int resolution = 256;
    [Tooltip("Anti-aliasing MSAA de la capture (1/2/4/8).")]
    public int msaa = 4;
    public float brightness = 1f;
    [Tooltip("Echantillonnage bilineaire (lisse) au lieu de nearest (crenele).")]
    public bool bilinear = true;

    private RenderTexture _rt;
    private Texture2D _tex;
    private Color32[] _px;
    private int _w, _h;

    private void Start()
    {
        if (source == null) source = GetComponent<Camera>();
        _rt = new RenderTexture(resolution, resolution, 16)
        {
            name = "WallStage",
            antiAliasing = Mathf.Clamp(Mathf.ClosestPowerOfTwo(msaa), 1, 8)
        };
        _tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear
        };
        _w = resolution; _h = resolution;
        if (source != null)
        {
            source.targetTexture = _rt;
            source.allowMSAA = true;
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

        _px = _tex.GetPixels32();

        for (int i = 0; i < f.Ids.Length; i++)
        {
            Vector2 uv = f.Norm[i];
            Color32 c = bilinear ? SampleBilinear(uv.x, uv.y) : SampleNearest(uv.x, uv.y);
            f.SetColor(i, new Color32(
                (byte)Mathf.Min(255, (int)(c.r * brightness)),
                (byte)Mathf.Min(255, (int)(c.g * brightness)),
                (byte)Mathf.Min(255, (int)(c.b * brightness)),
                255));
        }
    }

    private Color32 SampleNearest(float u, float v)
    {
        int x = Mathf.Clamp((int)(u * (_w - 1)), 0, _w - 1);
        int y = Mathf.Clamp((int)(v * (_h - 1)), 0, _h - 1);
        return _px[y * _w + x];
    }

    private Color32 SampleBilinear(float u, float v)
    {
        float fx = Mathf.Clamp01(u) * (_w - 1);
        float fy = Mathf.Clamp01(v) * (_h - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Mathf.Min(x0 + 1, _w - 1), y1 = Mathf.Min(y0 + 1, _h - 1);
        float tx = fx - x0, ty = fy - y0;

        Color32 c00 = _px[y0 * _w + x0], c10 = _px[y0 * _w + x1];
        Color32 c01 = _px[y1 * _w + x0], c11 = _px[y1 * _w + x1];

        float r = Lerp2(c00.r, c10.r, c01.r, c11.r, tx, ty);
        float g = Lerp2(c00.g, c10.g, c01.g, c11.g, tx, ty);
        float b = Lerp2(c00.b, c10.b, c01.b, c11.b, tx, ty);
        return new Color32((byte)r, (byte)g, (byte)b, 255);
    }

    private static float Lerp2(byte a, byte b, byte c, byte d, float tx, float ty)
    {
        float top = a + (b - a) * tx;
        float bot = c + (d - c) * tx;
        return top + (bot - top) * ty;
    }

    private void OnDestroy()
    {
        if (source != null) source.targetTexture = null;
        if (_rt != null) _rt.Release();
    }
}
