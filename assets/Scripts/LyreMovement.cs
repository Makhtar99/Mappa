using UnityEngine;

[RequireComponent(typeof(LyreController))]
[DefaultExecutionOrder(-100)]
public sealed class LyreMovement : MonoBehaviour
{
    public enum Pattern { Static, Sweep, Circle, Figure8, Bounce, Random }

    public Pattern pattern = Pattern.Circle;
    public float speed = 0.4f;
    [Range(0f, 0.5f)] public float amplitude = 0.3f;
    [Range(0f, 1f)] public float centerPan = 0.5f;
    [Range(0f, 1f)] public float centerTilt = 0.5f;
    [Range(0f, 1f)] public float phase = 0f;

    private LyreController _lyre;

    private void Awake() => _lyre = GetComponent<LyreController>();

    private void Update()
    {
        if (_lyre == null) return;

        float t = (Time.time + phase * 10f) * speed * Mathf.PI * 2f;
        float p = centerPan;
        float ti = centerTilt;

        switch (pattern)
        {
            case Pattern.Sweep:
                p = centerPan + Mathf.Sin(t) * amplitude;
                break;
            case Pattern.Circle:
                p = centerPan + Mathf.Cos(t) * amplitude;
                ti = centerTilt + Mathf.Sin(t) * amplitude;
                break;
            case Pattern.Figure8:
                p = centerPan + Mathf.Sin(t) * amplitude;
                ti = centerTilt + Mathf.Sin(t * 2f) * amplitude * 0.5f;
                break;
            case Pattern.Bounce:
                ti = centerTilt + Mathf.Abs(Mathf.Sin(t)) * amplitude;
                break;
            case Pattern.Random:
                p = centerPan + (Mathf.PerlinNoise(t * 0.3f, 0f) - 0.5f) * 2f * amplitude;
                ti = centerTilt + (Mathf.PerlinNoise(0f, t * 0.3f) - 0.5f) * 2f * amplitude;
                break;
        }

        _lyre.pan = Mathf.Clamp01(p);
        _lyre.tilt = Mathf.Clamp01(ti);
    }
}
