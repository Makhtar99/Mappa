using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class AudioReactive : MonoBehaviour
{
    public int bands = 32;
    public float gain = 220f;
    public float highBoost = 0.6f;
    public float attack = 20f;
    public float release = 6f;
    [Range(0f, 1f)] public float hueSpread = 0.7f;
    public bool playOnStart = true;

    private AudioSource _src;
    private readonly float[] _spectrum = new float[512];
    private float[] _level;

    private void Start()
    {
        _src = GetComponent<AudioSource>();
        _src.loop = true;
        _level = new float[bands];
        if (playOnStart) _src.Play();
    }

    private void Update()
    {
        var f = EntityField.Instance;
        if (f == null) return;

        _src.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        for (int b = 0; b < bands; b++)
        {
            int lo = Mathf.FloorToInt((float)b / bands * _spectrum.Length);
            int hi = Mathf.Max(lo + 1, Mathf.FloorToInt((float)(b + 1) / bands * _spectrum.Length));
            float sum = 0f;
            for (int k = lo; k < hi; k++) sum += _spectrum[k];
            float v = Mathf.Clamp01(sum * gain * (1f + b * highBoost));
            float rate = v > _level[b] ? attack : release;
            _level[b] = Mathf.Lerp(_level[b], v, Time.deltaTime * rate);
        }

        for (int i = 0; i < f.Ids.Length; i++)
        {
            Vector2 uv = f.Norm[i];
            int b = Mathf.Clamp((int)(uv.x * bands), 0, bands - 1);
            float lvl = _level[b];
            if (uv.y <= lvl)
            {
                Color c = Color.HSVToRGB(((float)b / bands) * hueSpread, 1f, 1f);
                f.SetColor(i, new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255));
            }
            else
            {
                f.SetColor(i, new Color32(0, 0, 0, 255));
            }
        }
    }
}
