using UnityEngine;
using UnityEngine.Timeline;

// Piste Timeline dediee a la sequence "projecteurs" (diagonales rouge/blanc).
// A ajouter sur la Timeline principale (Show). Le clip s'etend sur toute la
// duree du show : quand la Timeline se termine, OnBehaviourPause du clip
// eteint automatiquement les projecteurs.
[TrackColor(0.95f, 0.15f, 0.15f)]
[TrackClipType(typeof(ProjectorShowClip))]
public class ProjectorShowTrack : TrackAsset
{
}
