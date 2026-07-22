using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

[DefaultExecutionOrder(-50)]
public sealed class EurovisionShow : MonoBehaviour
{
    private static readonly string[] VideoClips = { "flames.mp4", "Swirls-1.mp4", "pyschadelic-1.mp4", "maze.mp4" };
    private const string AudioFile = "MySystem.mp3";

    private static readonly Color[] Palette =
    {
        new Color(0.85f, 0.0f, 0.17f),
        new Color(1.0f, 0.06f, 0.19f),
        new Color(0.55f, 0.0f, 0.07f),
        new Color(1.0f, 0.2f, 0.33f),
        new Color(0.85f, 0.0f, 0.17f),
        new Color(1.0f, 0.95f, 0.9f),
    };

    [Header("Video backdrop")]
    public int resolution = 256;
    [Range(0f, 1f)] public float videoBrightness = 0.5f;
    public bool flipY = true;

    [Header("Illuminators (red light layer)")]
    public int illuminatorCount = 6;
    public float illuminatorRadius = 0.7f;
    public float sweepSpeed = 0.5f;
    [Range(0f, 1f)] public float baseIntensity = 0.7f;

    [Header("Devices")]
    public bool spawnLyres = true;
    public int lyreCount = 4;

    [Header("Audio")]
    public float levelGain = 7f;
    public float bassGain = 45f;
    public float beatThreshold = 1.4f;
    public float beatCooldown = 0.16f;

    private AudioSource _audio;
    private VideoPlayer _vp;
    private RenderTexture _rt;
    private Texture2D _tex;
    private int _currentClip = -1;
    private float _segment = 30f;

    private Illuminator[] _illums;
    private LyreController[] _lyres;
    private ProjectorController _projector;

    private readonly float[] _samples = new float[256];
    private readonly float[] _spectrum = new float[512];
    private float _level, _bass, _bassAvg, _flash, _cool;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (SceneManager.GetActiveScene().name != "Eurovision") return;
        if (FindFirstObjectByType<EurovisionShow>() != null) return;
        new GameObject("EurovisionShow").AddComponent<EurovisionShow>();
    }

    private void Awake()
    {
        var reactive = FindFirstObjectByType<AudioReactive>();
        if (reactive != null) reactive.enabled = false;
        var rig = FindFirstObjectByType<IlluminatorRig>();
        if (rig != null) rig.enabled = false;

        _audio = FindFirstObjectByType<AudioSource>();
        if (_audio == null) _audio = new GameObject("Music").AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.Stop();
        _audio.clip = null;
    }

    private void Start()
    {
        _rt = new RenderTexture(resolution, resolution, 0) { name = "EuroVideo" };
        _tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

        _vp = gameObject.AddComponent<VideoPlayer>();
        _vp.playOnAwake = false;
        _vp.source = VideoSource.Url;
        _vp.renderMode = VideoRenderMode.RenderTexture;
        _vp.targetTexture = _rt;
        _vp.audioOutputMode = VideoAudioOutputMode.None;
        _vp.isLooping = true;
        _vp.aspectRatio = VideoAspectRatio.Stretch;
        PlayClip(0);

        BuildIlluminators();
        if (spawnLyres) BuildDevices();

        StartCoroutine(LoadMusic());
    }

    private void BuildIlluminators()
    {
        _illums = new Illuminator[illuminatorCount];
        for (int i = 0; i < illuminatorCount; i++)
        {
            var go = new GameObject("EuroIllum " + i);
            go.transform.SetParent(transform, false);
            var il = go.AddComponent<Illuminator>();
            il.smooth = true;
            il.radius = illuminatorRadius;
            il.color = Palette[i % Palette.Length];
            _illums[i] = il;
        }
    }

    private void BuildDevices()
    {
        var dev = new GameObject("Devices");
        dev.transform.SetParent(transform, false);
        var emitter = dev.AddComponent<DeviceEmitter>();

        _projector = dev.AddComponent<ProjectorController>();
        _projector.emitter = emitter;
        _projector.baseEntityId = 20000;
        _projector.color = new Color(0.85f, 0f, 0.17f);

        _lyres = new LyreController[lyreCount];
        for (int i = 0; i < lyreCount; i++)
        {
            var go = new GameObject("Lyre " + i);
            go.transform.SetParent(dev.transform, false);
            var lyre = go.AddComponent<LyreController>();
            lyre.emitter = emitter;
            lyre.baseEntityId = 20010 + i * 10;
            lyre.color = new Color(0.9f, 0.05f, 0.2f);
            var mv = go.AddComponent<LyreMovement>();
            mv.pattern = (i % 2 == 0) ? LyreMovement.Pattern.Circle : LyreMovement.Pattern.Figure8;
            mv.speed = 0.35f + 0.1f * i;
            mv.amplitude = 0.35f;
            mv.phase = (float)i / Mathf.Max(1, lyreCount);
            _lyres[i] = lyre;
        }
    }

    private IEnumerator LoadMusic()
    {
        string uri = new System.Uri(Path.Combine(Application.streamingAssetsPath, AudioFile)).AbsoluteUri;
        using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("EurovisionShow audio: " + req.error);
                yield break;
            }
            var clip = DownloadHandlerAudioClip.GetContent(req);
            _audio.clip = clip;
            _audio.loop = true;
            _audio.Play();
            if (clip.length > 1f) _segment = clip.length / VideoClips.Length;
        }
    }

    private void PlayClip(int i)
    {
        if (i == _currentClip) return;
        _currentClip = i;
        _vp.url = new System.Uri(Path.Combine(Application.streamingAssetsPath, VideoClips[i])).AbsoluteUri;
        _vp.Play();
    }

    private void Update()
    {
        var f = EntityField.Instance;
        if (f == null || f.World.Length == 0) return;

        Analyse();
        DetectBeat();
        DrawVideoBase(f);
        AnimateIlluminators();
        AnimateDevices();
    }

    private void Analyse()
    {
        if (_audio == null || !_audio.isPlaying) { _level = _bass = 0f; return; }
        _audio.GetOutputData(_samples, 0);
        float sum = 0f;
        for (int i = 0; i < _samples.Length; i++) sum += _samples[i] * _samples[i];
        float rms = Mathf.Sqrt(sum / _samples.Length);
        float lvl = Mathf.Clamp01(rms * levelGain);
        _level = Mathf.Lerp(_level, lvl, Time.deltaTime * (lvl > _level ? 20f : 6f));

        _audio.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
        float low = 0f;
        for (int i = 0; i < 8; i++) low += _spectrum[i];
        _bass = Mathf.Clamp01(low * bassGain);
    }

    private void DetectBeat()
    {
        _cool -= Time.deltaTime;
        _flash = Mathf.Max(0f, _flash - Time.deltaTime / 0.15f);
        _bassAvg = Mathf.Lerp(_bassAvg, _bass, Time.deltaTime * 3f);
        if (_cool <= 0f && _bass > _bassAvg * beatThreshold && _bass > 0.15f)
        {
            _flash = 1f;
            _cool = beatCooldown;
        }
    }

    private void DrawVideoBase(EntityField f)
    {
        if (_vp == null || !_vp.isPrepared) return;

        float clock = _audio != null && _audio.isPlaying ? _audio.time : Time.time;
        PlayClip(Mathf.Clamp((int)(clock / Mathf.Max(0.01f, _segment)), 0, VideoClips.Length - 1));

        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
        _tex.Apply(false);
        RenderTexture.active = prev;

        var px = _tex.GetPixels32();
        int w = _rt.width, h = _rt.height;
        float b = videoBrightness;

        for (int i = 0; i < f.Ids.Length; i++)
        {
            Vector2 uv = f.Norm[i];
            int x = Mathf.Clamp((int)(uv.x * (w - 1)), 0, w - 1);
            float ny = flipY ? 1f - uv.y : uv.y;
            int y = Mathf.Clamp((int)(ny * (h - 1)), 0, h - 1);
            Color32 c = px[y * w + x];
            f.SetColor(i, new Color32(
                (byte)(c.r * b), (byte)(c.g * b), (byte)(c.b * b), 255));
        }
    }

    private void AnimateIlluminators()
    {
        if (_illums == null) return;
        float t = Time.time;
        for (int i = 0; i < _illums.Length; i++)
        {
            var il = _illums[i];
            if (il == null) continue;
            float ph = (float)i / _illums.Length;
            float a = ph * Mathf.PI * 2f;
            float x, y;
            switch (i % 3)
            {
                case 0:
                    x = Mathf.Sin(t * sweepSpeed + a) * 0.9f;
                    y = Mathf.Lerp(-0.6f, 0.6f, ph);
                    break;
                case 1:
                    x = Mathf.Lerp(-0.9f, 0.9f, ph);
                    y = Mathf.Sin(t * sweepSpeed * 1.3f + a) * 0.7f;
                    break;
                default:
                    x = Mathf.Cos(t * sweepSpeed * 0.8f + a) * 0.7f;
                    y = Mathf.Sin(t * sweepSpeed * 0.8f + a) * 0.7f;
                    break;
            }
            il.transform.position = new Vector3(x, y, 0f);

            Color c = Palette[i % Palette.Length];
            if (_flash > 0.01f && i % 2 == 0) c = Color.Lerp(c, Color.white, _flash * 0.8f);
            il.color = c;
            il.intensity = Mathf.Clamp01(baseIntensity * Mathf.Lerp(0.4f, 1f, _level) + _flash * 0.5f);
        }
    }

    private void AnimateDevices()
    {
        float dim = Mathf.Lerp(0.55f, 1f, _level);
        if (_projector != null) _projector.dimmer = dim;
        if (_lyres == null) return;
        for (int i = 0; i < _lyres.Length; i++)
        {
            var lyre = _lyres[i];
            if (lyre == null) continue;
            lyre.dimmer = dim;
            lyre.strobe = _flash > 0.6f ? _flash : 0f;
        }
    }

    private void OnDestroy()
    {
        if (_rt != null) _rt.Release();
    }
}
