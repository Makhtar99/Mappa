using UnityEngine;

[DefaultExecutionOrder(-200)]
public sealed class ShowAudioSource : MonoBehaviour
{
    public float levelGain = 7f;
    public float bassGain = 45f;

    private readonly float[] _samples = new float[256];
    private readonly float[] _spectrum = new float[512];

    private void Update()
    {
        AudioListener.GetOutputData(_samples, 0);
        float sum = 0f;
        for (int i = 0; i < _samples.Length; i++) sum += _samples[i] * _samples[i];
        float rms = Mathf.Sqrt(sum / _samples.Length);
        float lvl = Mathf.Clamp01(rms * levelGain);
        ShowAudio.Level = Mathf.Lerp(ShowAudio.Level, lvl, Time.deltaTime * (lvl > ShowAudio.Level ? 20f : 6f));

        AudioListener.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
        float low = 0f;
        for (int i = 0; i < 8; i++) low += _spectrum[i];
        ShowAudio.Bass = Mathf.Clamp01(low * bassGain);
    }
}
