using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class ColorWashBehaviour : PlayableBehaviour
{
    public enum Mode { Solid, Horizontal, Vertical, Radial }

    public Mode mode = Mode.Solid;
    public Color colorA = new Color(0.85f, 0f, 0.17f);
    public Color colorB = Color.black;
    [Range(0f, 2f)] public float brightness = 1f;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var f = EntityField.Instance;
        if (f == null) return;
        float w = info.weight * brightness;
        if (w <= 0f) return;

        for (int i = 0; i < f.Norm.Length; i++)
        {
            float t;
            switch (mode)
            {
                case Mode.Horizontal: t = f.Norm[i].x; break;
                case Mode.Vertical: t = f.Norm[i].y; break;
                case Mode.Radial:
                    t = Mathf.Clamp01((f.Norm[i] - new Vector2(0.5f, 0.5f)).magnitude * 2f);
                    break;
                default: t = 0f; break;
            }
            f.AddColor(i, WallPaint.ToColor32(Color.Lerp(colorA, colorB, t), w));
        }
    }
}

[System.Serializable]
public class ColorWashClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public ColorWashBehaviour template = new ColorWashBehaviour();

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<ColorWashBehaviour>.Create(graph, template);
}
