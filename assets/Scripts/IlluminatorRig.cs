using UnityEngine;

[DefaultExecutionOrder(-50)]
public sealed class IlluminatorRig : MonoBehaviour
{
    public int count = 6;
    public float radius = 0.5f;
    public float radiusJitter = 0.15f;
    [Range(0f, 1f)] public float intensity = 1f;
    public float speed = 0.25f;
    public float areaX = 0.9f;
    public float areaY = 0.9f;
    public float hueCycleSpeed = 0.05f;
    public bool smooth = true;

    [Header("Audio")]
    public bool audioReactive = true;
    public float audioGain = 6f;
    [Range(0f, 1f)] public float audioPulse = 0.6f;

    private Illuminator[] _lights;
    private float[] _phase;
    private float[] _hue;
    private float[] _rad;
    private int[] _mode;
    private AudioSource _audio;
    private readonly float[] _samples = new float[256];
    private float _level;

    private void Start()
    {
        _audio = FindFirstObjectByType<AudioSource>();
        _lights = new Illuminator[count];
        _phase = new float[count];
        _hue = new float[count];
        _rad = new float[count];
        _mode = new int[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Illuminator " + i);
            go.transform.SetParent(transform, false);
            var il = go.AddComponent<Illuminator>();
            il.smooth = smooth;
            _lights[i] = il;

            float f = (float)i / Mathf.Max(1, count);
            _phase[i] = f;
            _hue[i] = f;
            _rad[i] = radius + Mathf.Lerp(-radiusJitter, radiusJitter, f);
            _mode[i] = i % 3;
        }
    }

    private void Update()
    {
        if (_lights == null) return;

        float lvl = SampleAudio();
        float t = Time.time * speed * Mathf.PI * 2f;

        for (int i = 0; i < _lights.Length; i++)
        {
            var il = _lights[i];
            if (il == null) continue;

            float ph = _phase[i] * Mathf.PI * 2f;
            float x, y;
            switch (_mode[i])
            {
                case 0:
                    x = Mathf.Cos(t + ph) * areaX;
                    y = Mathf.Sin(t + ph) * areaY;
                    break;
                case 1:
                    x = Mathf.Sin(t * 0.8f + ph) * areaX;
                    y = Mathf.Sin(t * 1.6f + ph) * areaY * 0.6f;
                    break;
                default:
                    x = (Mathf.PerlinNoise(t * 0.1f + i, 0f) - 0.5f) * 2f * areaX;
                    y = (Mathf.PerlinNoise(0f, t * 0.1f + i) - 0.5f) * 2f * areaY;
                    break;
            }
            il.transform.position = new Vector3(x, y, 0f);

            _hue[i] = Mathf.Repeat(_hue[i] + Time.deltaTime * hueCycleSpeed, 1f);
            il.color = Color.HSVToRGB(_hue[i], 1f, 1f);

            float pulse = audioReactive ? 1f + lvl * audioPulse * 2f : 1f;
            il.radius = _rad[i] * pulse;
            il.intensity = Mathf.Clamp01(intensity * (audioReactive ? Mathf.Lerp(1f - audioPulse, 1f, lvl) : 1f));
        }
    }

    private float SampleAudio()
    {
        if (!audioReactive || _audio == null || !_audio.isPlaying) return 0f;
        _audio.GetOutputData(_samples, 0);
        float sum = 0f;
        for (int i = 0; i < _samples.Length; i++) sum += _samples[i] * _samples[i];
        float rms = Mathf.Sqrt(sum / _samples.Length);
        float v = Mathf.Clamp01(rms * audioGain);
        _level = Mathf.Lerp(_level, v, Time.deltaTime * (v > _level ? 20f : 6f));
        return _level;
    }
}
