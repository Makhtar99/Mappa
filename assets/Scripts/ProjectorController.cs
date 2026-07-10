using UnityEngine;

public sealed class ProjectorController : MonoBehaviour
{
    public DeviceEmitter emitter;
    public int entityId = 20000;
    public Color color = Color.white;
    [Range(0f, 1f)] public float white = 0f;
    [Range(0f, 1f)] public float dimmer = 1f;

    private void Update()
    {
        if (emitter == null) return;
        emitter.Set(entityId,
            (byte)(color.r * dimmer * 255f),
            (byte)(color.g * dimmer * 255f),
            (byte)(color.b * dimmer * 255f),
            (byte)(white * dimmer * 255f));
    }
}
