using System.IO;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class ImageProjectorBehaviour : PlayableBehaviour
{
    public string imageFile = "";
    [Range(0f, 2f)] public float brightness = 1f;
    public bool flipY = true;

    private Texture2D _tex;
    private Color32[] _px;
    private int _w, _h;
    private bool _tried;

    private void Load()
    {
        _tried = true;
        if (string.IsNullOrEmpty(imageFile)) return;
        string path = Path.Combine(Application.streamingAssetsPath, imageFile);
        if (!File.Exists(path)) { Debug.LogWarning("ImageProjector: introuvable " + path); return; }
        _tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!_tex.LoadImage(File.ReadAllBytes(path))) { _tex = null; return; }
        _w = _tex.width; _h = _tex.height;
        _px = _tex.GetPixels32();
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var f = EntityField.Instance;
        if (f == null) return;
        if (!_tried) Load();
        if (_px == null) return;
        float w = info.weight * brightness;
        if (w <= 0f) return;

        for (int i = 0; i < f.Ids.Length; i++)
        {
            Vector2 uv = f.Norm[i];
            int x = Mathf.Clamp((int)(uv.x * (_w - 1)), 0, _w - 1);
            float ny = flipY ? 1f - uv.y : uv.y;
            int y = Mathf.Clamp((int)(ny * (_h - 1)), 0, _h - 1);
            Color32 c = _px[y * _w + x];
            f.AddColor(i, new Color32(
                (byte)Mathf.Min(255f, c.r * w),
                (byte)Mathf.Min(255f, c.g * w),
                (byte)Mathf.Min(255f, c.b * w),
                255));
        }
    }
}

[System.Serializable]
public class ImageProjectorClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public ImageProjectorBehaviour template = new ImageProjectorBehaviour();

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<ImageProjectorBehaviour>.Create(graph, template);
}
