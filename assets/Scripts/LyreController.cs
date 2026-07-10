using UnityEngine;

public sealed class LyreController : MonoBehaviour
{
    public DeviceEmitter emitter;
    public int baseEntityId = 20010;
    public const int ChannelCount = 13;

    [Header("Controle")]
    [Range(0f, 1f)] public float pan = 0.5f;
    [Range(0f, 1f)] public float tilt = 0.5f;
    [Range(0f, 1f)] public float speed = 0f;
    [Range(0f, 1f)] public float dimmer = 1f;
    [Range(0f, 1f)] public float strobe = 0f;
    public Color color = Color.white;
    [Range(0f, 1f)] public float white = 0f;

    private readonly byte[] _ch = new byte[ChannelCount];

    private void Update()
    {
        if (emitter == null) return;

        int pan16 = (int)(Mathf.Clamp01(pan) * 65535f);
        int tilt16 = (int)(Mathf.Clamp01(tilt) * 65535f);

        _ch[0] = (byte)(pan16 >> 8);      // horizontal (pan)
        _ch[1] = (byte)(pan16 & 0xFF);    // pan fine
        _ch[2] = (byte)(tilt16 >> 8);     // vertical (tilt)
        _ch[3] = (byte)(tilt16 & 0xFF);   // tilt fine
        _ch[4] = (byte)(speed * 255f);    // speed
        _ch[5] = (byte)(dimmer * 255f);   // dimmer
        _ch[6] = (byte)(strobe * 255f);   // strobe
        _ch[7] = (byte)(color.r * 255f);  // red
        _ch[8] = (byte)(color.g * 255f);  // green
        _ch[9] = (byte)(color.b * 255f);  // blue
        _ch[10] = (byte)(white * 255f);   // white
        _ch[11] = 0;                      // auto/voice -> manuel
        _ch[12] = 0;                      // reset

        int entities = (ChannelCount + 3) / 4;
        for (int e = 0; e < entities; e++)
        {
            int c = e * 4;
            emitter.Set(baseEntityId + e, Get(c), Get(c + 1), Get(c + 2), Get(c + 3));
        }
    }

    private byte Get(int i) => i < _ch.Length ? _ch[i] : (byte)0;
}
