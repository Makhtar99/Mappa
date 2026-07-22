using UnityEngine;
using UnityEngine.Timeline;

[TrackColor(0.85f, 0.1f, 0.2f)]
[TrackClipType(typeof(ColorWashClip))]
[TrackClipType(typeof(LaserSweepClip))]
[TrackClipType(typeof(StrobeClip))]
[TrackClipType(typeof(IlluminatorClip))]
[TrackClipType(typeof(ImageProjectorClip))]
public class WallTrack : TrackAsset
{
}
