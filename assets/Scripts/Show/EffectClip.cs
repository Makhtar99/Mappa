using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public enum ShowEffectKind { RadialBurst, Star, PolygonTunnel }

[System.Serializable]
public class EffectBehaviour : PlayableBehaviour
{
    public ShowEffectKind kind;
    public float strength = 1f;

    private IShowEffect _target;
    private bool _resolved;

    private void Resolve()
    {
        _resolved = true;
        switch (kind)
        {
            case ShowEffectKind.RadialBurst: _target = Object.FindFirstObjectByType<RadialBurstEffect>(); break;
            case ShowEffectKind.Star: _target = Object.FindFirstObjectByType<StarSpinEffect>(); break;
            case ShowEffectKind.PolygonTunnel: _target = Object.FindFirstObjectByType<PolygonTunnelEffect>(); break;
        }
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!_resolved) Resolve();
        if (_target != null) _target.Intensity = info.weight * strength;
    }

    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (_target != null) _target.Intensity = 0f;
    }
}

[System.Serializable]
public class EffectClip : PlayableAsset, ITimelineClipAsset
{
    public EffectBehaviour template = new EffectBehaviour();

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<EffectBehaviour>.Create(graph, template);
}
