using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class ProjectorBehaviour : PlayableBehaviour
{
    public int entityId = 20000;
    public Color color = new Color(0.85f, 0f, 0.17f);
    [Range(0f, 1f)] public float white = 0f;
    [Range(0f, 1f)] public float dimmer = 1f;

    private DeviceEmitter _emitter;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (_emitter == null) _emitter = Object.FindFirstObjectByType<DeviceEmitter>();
        var emitter = _emitter;
        if (emitter == null) return;
        float dim = dimmer * info.weight;
        emitter.Set(entityId,
            (byte)(color.r * dim * 255f),
            (byte)(color.g * dim * 255f),
            (byte)(color.b * dim * 255f),
            (byte)(white * dim * 255f));
    }
}

[System.Serializable]
public class ProjectorClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public ProjectorBehaviour template = new ProjectorBehaviour();

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<ProjectorBehaviour>.Create(graph, template);
}
