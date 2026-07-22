using UnityEngine;
using UnityEngine.Timeline;

[TrackColor(0.2f, 0.35f, 0.85f)]
[TrackClipType(typeof(LyreClip))]
[TrackClipType(typeof(ProjectorClip))]
public class DeviceTrack : TrackAsset
{
}
