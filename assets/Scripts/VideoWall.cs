using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

[DefaultExecutionOrder(700)]
public sealed class VideoWall : MonoBehaviour
{
    public string videoFile = "my-system-light-show.mp4";
    public int resolution = 256;
    public float brightness = 1f;
    public bool flipY = true;

    private VideoPlayer _vp;
    private RenderTexture _rt;
    private Texture2D _tex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (SceneManager.GetActiveScene().name != "VideoWall") return;
        if (FindFirstObjectByType<VideoWall>() != null) return;
        new GameObject("VideoWall").AddComponent<VideoWall>();
    }

    private void Awake()
    {
        var r = FindFirstObjectByType<AudioReactive>();
        if (r != null) r.enabled = false;
        var rig = FindFirstObjectByType<IlluminatorRig>();
        if (rig != null) rig.enabled = false;
    }

    private void Start()
    {
        _rt = new RenderTexture(resolution, resolution, 0) { name = "VideoWall" };
        _tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

        _vp = gameObject.AddComponent<VideoPlayer>();
        _vp.playOnAwake = false;
        _vp.source = VideoSource.Url;
        _vp.url = new System.Uri(Path.Combine(Application.streamingAssetsPath, videoFile)).AbsoluteUri;
        _vp.renderMode = VideoRenderMode.RenderTexture;
        _vp.targetTexture = _rt;
        _vp.aspectRatio = VideoAspectRatio.Stretch;
        _vp.isLooping = true;
        _vp.audioOutputMode = VideoAudioOutputMode.Direct;
        _vp.Play();
    }

    private void Update()
    {
        var f = EntityField.Instance;
        if (f == null || _vp == null || !_vp.isPrepared || f.Ids.Length == 0) return;

        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
        _tex.Apply(false);
        RenderTexture.active = prev;

        var px = _tex.GetPixels32();
        int w = _rt.width, h = _rt.height;
        for (int i = 0; i < f.Ids.Length; i++)
        {
            Vector2 uv = f.Norm[i];
            int x = Mathf.Clamp((int)(uv.x * (w - 1)), 0, w - 1);
            float ny = flipY ? 1f - uv.y : uv.y;
            int y = Mathf.Clamp((int)(ny * (h - 1)), 0, h - 1);
            Color32 c = px[y * w + x];
            f.SetColor(i, new Color32(
                (byte)Mathf.Min(255, (int)(c.r * brightness)),
                (byte)Mathf.Min(255, (int)(c.g * brightness)),
                (byte)Mathf.Min(255, (int)(c.b * brightness)),
                255));
        }
    }

    private void OnDestroy()
    {
        if (_rt != null) _rt.Release();
    }
}
