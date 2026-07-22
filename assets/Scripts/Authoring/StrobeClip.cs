using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class StrobeBehaviour : PlayableBehaviour
{
    public Color color = Color.white;
    [Range(0.5f, 30f)] public float rateHz = 8f;
    [Range(0.05f, 0.95f)] public float dutyCycle = 0.5f;
    [Range(0f, 2f)] public float brightness = 1f;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var f = EntityField.Instance;
        if (f == null) return;

        float time = (float)playable.GetTime();
        float phase = time * rateHz;
        bool on = (phase - Mathf.Floor(phase)) < dutyCycle;
        float w = info.weight * brightness * (on ? 1f : 0f);
        if (w <= 0f) return;

        var c = WallPaint.ToColor32(color, w);
        for (int i = 0; i < f.Colors.Length; i++) f.AddColor(i, c);
    }
}

[System.Serializable]
public class StrobeClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public StrobeBehaviour template = new StrobeBehaviour();

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<StrobeBehaviour>.Create(graph, template);
}
