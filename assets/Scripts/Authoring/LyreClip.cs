using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class LyreBehaviour : PlayableBehaviour
{
    public enum Pattern { Static, Sweep, Circle, Figure8 }

    public int baseEntityId = 20010;
    public Color color = new Color(0.9f, 0.05f, 0.2f);
    [Range(0f, 1f)] public float dimmer = 1f;
    [Range(0f, 1f)] public float white = 0f;
    [Range(0f, 1f)] public float strobe = 0f;

    public Pattern pattern = Pattern.Circle;
    public float speed = 0.4f;
    [Range(0f, 0.5f)] public float amplitude = 0.35f;
    [Range(0f, 1f)] public float centerPan = 0.5f;
    [Range(0f, 1f)] public float centerTilt = 0.5f;
    [Range(0f, 1f)] public float phase = 0f;

    private readonly byte[] _ch = new byte[13];
    private DeviceEmitter _emitter;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (_emitter == null) _emitter = Object.FindFirstObjectByType<DeviceEmitter>();
        var emitter = _emitter;
        if (emitter == null) return;

        float t = ((float)playable.GetTime() + phase * 10f) * speed * Mathf.PI * 2f;
        float pan = centerPan, tilt = centerTilt;
        switch (pattern)
        {
            case Pattern.Sweep: pan = centerPan + Mathf.Sin(t) * amplitude; break;
            case Pattern.Circle: pan = centerPan + Mathf.Cos(t) * amplitude; tilt = centerTilt + Mathf.Sin(t) * amplitude; break;
            case Pattern.Figure8: pan = centerPan + Mathf.Sin(t) * amplitude; tilt = centerTilt + Mathf.Sin(t * 2f) * amplitude * 0.5f; break;
        }

        int pan16 = (int)(Mathf.Clamp01(pan) * 65535f);
        int tilt16 = (int)(Mathf.Clamp01(tilt) * 65535f);
        float dim = dimmer * info.weight;

        _ch[0] = (byte)(pan16 >> 8);
        _ch[1] = (byte)(pan16 & 0xFF);
        _ch[2] = (byte)(tilt16 >> 8);
        _ch[3] = (byte)(tilt16 & 0xFF);
        _ch[4] = 0;
        _ch[5] = (byte)(dim * 255f);
        _ch[6] = (byte)(strobe * 255f);
        _ch[7] = (byte)(color.r * 255f);
        _ch[8] = (byte)(color.g * 255f);
        _ch[9] = (byte)(color.b * 255f);
        _ch[10] = (byte)(white * 255f);
        _ch[11] = 0;
        _ch[12] = 0;

        for (int e = 0; e < 4; e++)
        {
            int c = e * 4;
            emitter.Set(baseEntityId + e, Get(c), Get(c + 1), Get(c + 2), Get(c + 3));
        }
    }

    private byte Get(int i) => i < _ch.Length ? _ch[i] : (byte)0;
}

[System.Serializable]
public class LyreClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public LyreBehaviour template = new LyreBehaviour();

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<LyreBehaviour>.Create(graph, template);
}
