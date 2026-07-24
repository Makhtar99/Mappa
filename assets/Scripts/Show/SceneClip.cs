using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public enum ShowScene
{
    Beams, Tunnel, Lasers, Starburst, Kaleido, Grid, Wave, Bars, Spiral, Orbit, Vortex, Strobe, Rings, Snake
}

[System.Serializable]
public class SceneBehaviour : PlayableBehaviour
{
    public ShowScene scene;

    private VideoShowDirector _dir;
    private bool _resolved;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!_resolved) { _dir = Object.FindFirstObjectByType<VideoShowDirector>(); _resolved = true; }
        if (_dir != null) _dir.Request((int)scene, info.weight);
    }
}

[System.Serializable]
public class SceneClip : PlayableAsset, ITimelineClipAsset
{
    public SceneBehaviour template = new SceneBehaviour();

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<SceneBehaviour>.Create(graph, template);
}
