using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class LaserSweepBehaviour : PlayableBehaviour
{
    public Color color = new Color(1f, 0.06f, 0.19f);
    [Range(1, 16)] public int beams = 5;
    [Range(0.01f, 0.5f)] public float thickness = 0.08f;
    [Range(0f, 90f)] public float spread = 55f;
    public float pivotY = 1.2f;
    public float sweepSpeed = 0.5f;
    [Range(0f, 60f)] public float sweepAmplitude = 25f;
    [Range(0f, 2f)] public float brightness = 1f;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var f = EntityField.Instance;
        if (f == null) return;
        float w = info.weight * brightness;
        if (w <= 0f) return;

        float time = (float)playable.GetTime();
        float sweep = Mathf.Sin(time * sweepSpeed * Mathf.PI * 2f) * sweepAmplitude;
        Vector2 pivot = new Vector2(0f, pivotY);
        float inv = 1f / Mathf.Max(0.001f, thickness);

        for (int b = 0; b < beams; b++)
        {
            float frac = beams > 1 ? (float)b / (beams - 1) : 0.5f;
            float deg = sweep + Mathf.Lerp(-spread, spread, frac);
            float rad = deg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));

            for (int i = 0; i < f.World.Length; i++)
            {
                Vector2 p = new Vector2(f.World[i].x - pivot.x, f.World[i].y - pivot.y);
                float d = Mathf.Abs(p.x * dir.y - p.y * dir.x) * inv;
                if (d >= 1f) continue;
                f.AddColor(i, WallPaint.ToColor32(color, (1f - d) * w));
            }
        }
    }
}

[System.Serializable]
public class LaserSweepClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public LaserSweepBehaviour template = new LaserSweepBehaviour();

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<LaserSweepBehaviour>.Create(graph, template);
}
