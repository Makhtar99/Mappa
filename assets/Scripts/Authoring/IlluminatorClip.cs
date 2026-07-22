using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class IlluminatorBehaviour : PlayableBehaviour
{
    public enum Path { Static, Horizontal, Vertical, Circle }

    public Color color = new Color(1f, 0.06f, 0.19f);
    public Path path = Path.Horizontal;
    [Range(0.05f, 1.5f)] public float radius = 0.5f;
    public float speed = 0.5f;
    [Range(0f, 1f)] public float amplitude = 0.8f;
    public Vector2 center = new Vector2(0f, 0f);
    [Range(0f, 2f)] public float brightness = 1f;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var f = EntityField.Instance;
        if (f == null) return;
        float w = info.weight * brightness;
        if (w <= 0f) return;

        float t = (float)playable.GetTime() * speed * Mathf.PI * 2f;
        float x = center.x, y = center.y;
        switch (path)
        {
            case Path.Horizontal: x += Mathf.Sin(t) * amplitude; break;
            case Path.Vertical: y += Mathf.Sin(t) * amplitude; break;
            case Path.Circle: x += Mathf.Cos(t) * amplitude; y += Mathf.Sin(t) * amplitude; break;
        }

        float inv = 1f / Mathf.Max(0.001f, radius);
        for (int i = 0; i < f.World.Length; i++)
        {
            float dx = f.World[i].x - x;
            float dy = f.World[i].y - y;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * inv;
            if (d >= 1f) continue;
            float k = (1f - d);
            f.AddColor(i, WallPaint.ToColor32(color, k * k * w));
        }
    }
}

[System.Serializable]
public class IlluminatorClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public IlluminatorBehaviour template = new IlluminatorBehaviour();

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<IlluminatorBehaviour>.Create(graph, template);
}
