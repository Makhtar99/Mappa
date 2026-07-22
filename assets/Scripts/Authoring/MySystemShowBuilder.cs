using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

public sealed class MySystemShowBuilder : MonoBehaviour
{
    private const string AudioFile = "MySystem.mp3";

    private static readonly Color RedDeep = new Color(0.55f, 0f, 0.07f);
    private static readonly Color RedCrimson = new Color(0.85f, 0f, 0.17f);
    private static readonly Color RedHot = new Color(1f, 0.06f, 0.19f);
    private static readonly Color WarmWhite = new Color(1f, 0.95f, 0.9f);

    private const double Bar = 1.655; // 145 BPM, 4/4

    private AudioSource _audio;
    private PlayableDirector _director;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (SceneManager.GetActiveScene().name != "MySystem") return;
        if (FindFirstObjectByType<MySystemShowBuilder>() != null) return;
        new GameObject("MySystemShow").AddComponent<MySystemShowBuilder>();
    }

    private void Awake()
    {
        var reactive = FindFirstObjectByType<AudioReactive>();
        if (reactive != null) reactive.enabled = false;
        var rig = FindFirstObjectByType<IlluminatorRig>();
        if (rig != null) rig.enabled = false;

        if (FindFirstObjectByType<DeviceEmitter>() == null)
            new GameObject("Devices").AddComponent<DeviceEmitter>();

        _audio = FindFirstObjectByType<AudioSource>();
        if (_audio == null) _audio = new GameObject("Music").AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.Stop();
        _audio.clip = null;
    }

    private void Start()
    {
        var timeline = BuildTimeline();
        _director = gameObject.AddComponent<PlayableDirector>();
        _director.playableAsset = timeline;
        _director.timeUpdateMode = DirectorUpdateMode.GameTime;
        _director.extrapolationMode = DirectorWrapMode.None;
        StartCoroutine(LoadAndPlay());
    }

    private TimelineAsset BuildTimeline()
    {
        var tl = ScriptableObject.CreateInstance<TimelineAsset>();

        var baseTrack = tl.CreateTrack<WallTrack>(null, "Base");
        var fxTrack = tl.CreateTrack<WallTrack>(null, "FX");
        var flashTrack = tl.CreateTrack<WallTrack>(null, "Flash");
        var devTrack = tl.CreateTrack<DeviceTrack>(null, "Lyres");

        // Nappe rouge de fond sur tout le morceau
        var wash = Add<ColorWashClip>(baseTrack, 0, 182);
        var wa = ((ColorWashClip)wash.asset).template;
        wa.mode = ColorWashBehaviour.Mode.Radial;
        wa.colorA = RedDeep; wa.colorB = Color.black; wa.brightness = 0.45f;
        wash.easeInDuration = 4;

        // Sections (secondes, à ~145 BPM) : couplets = illuminateurs, refrains = lasers + strobes + lyres
        AddVerse(fxTrack, 13, 40, IlluminatorBehaviour.Path.Horizontal);
        AddChorus(fxTrack, flashTrack, devTrack, 53, 79);
        AddVerse(fxTrack, 79, 105, IlluminatorBehaviour.Path.Circle);
        AddChorus(fxTrack, flashTrack, devTrack, 118, 144);
        AddChorus(fxTrack, flashTrack, devTrack, 157, 182);

        return tl;
    }

    private void AddVerse(WallTrack fx, double from, double to, IlluminatorBehaviour.Path path)
    {
        var c = Add<IlluminatorClip>(fx, from, to - from);
        var t = ((IlluminatorClip)c.asset).template;
        t.color = RedCrimson; t.radius = 0.6f; t.path = path; t.speed = 0.25f; t.amplitude = 0.85f; t.brightness = 0.9f;
        c.easeInDuration = 2; c.easeOutDuration = 2;
    }

    private void AddChorus(WallTrack fx, WallTrack flash, DeviceTrack dev, double from, double to)
    {
        var laser = Add<LaserSweepClip>(fx, from, to - from);
        var lt = ((LaserSweepClip)laser.asset).template;
        lt.color = RedHot; lt.beams = 6; lt.spread = 55f; lt.thickness = 0.09f;
        lt.sweepSpeed = 0.35f; lt.sweepAmplitude = 22f; lt.brightness = 1f;
        laser.easeInDuration = 1;

        for (double s = from; s < to; s += Bar * 2)
        {
            var st = Add<StrobeClip>(flash, s, 0.18);
            var sb = ((StrobeClip)st.asset).template;
            sb.color = WarmWhite; sb.rateHz = 14f; sb.brightness = 1f;
        }

        var lyre = Add<LyreClip>(dev, from, to - from);
        var lb = ((LyreClip)lyre.asset).template;
        lb.baseEntityId = 20010; lb.color = RedCrimson; lb.pattern = LyreBehaviour.Pattern.Circle;
        lb.speed = 0.4f; lb.amplitude = 0.35f; lb.dimmer = 1f;
    }

    private static TimelineClip Add<T>(TrackAsset track, double start, double duration) where T : PlayableAsset
    {
        var clip = track.CreateClip<T>();
        clip.start = start;
        clip.duration = duration;
        return clip;
    }

    private IEnumerator LoadAndPlay()
    {
        string uri = new System.Uri(Path.Combine(Application.streamingAssetsPath, AudioFile)).AbsoluteUri;
        using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                _audio.clip = DownloadHandlerAudioClip.GetContent(req);
                _audio.loop = false;
            }
            else Debug.LogWarning("MySystemShow audio: " + req.error);
        }

        _director.time = 0;
        _director.Play();
        if (_audio.clip != null) _audio.Play();
    }
}
