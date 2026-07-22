#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public static class VideoShowBuilder
{
    private const string AudioAsset = "Assets/Audio/Sweden - My_System.mp3";
    private const string TimelineAsset = "Assets/VideoShow_Timeline.playable";
    private const double StartInSong = 40.0;
    private const double Duration = 30.0;

    [MenuItem("Tools/Mappa/Build VideoShow (Timeline 40s-70s)")]
    public static void Build()
    {
        foreach (var r in Object.FindObjectsByType<AudioReactive>(FindObjectsSortMode.None)) r.enabled = false;
        foreach (var r in Object.FindObjectsByType<IlluminatorRig>(FindObjectsSortMode.None)) r.enabled = false;

        if (Object.FindFirstObjectByType<EntityField>() == null)
        {
            EditorUtility.DisplayDialog("Mappa", "Pas de mur (EntityField) dans la scene.", "OK");
            return;
        }

        var vs = GameObject.Find("VideoShow");
        if (vs == null) vs = new GameObject("VideoShow");
        if (vs.GetComponent<VideoShowDirector>() == null) vs.AddComponent<VideoShowDirector>();

        var main = Camera.main;
        if (main != null)
        {
            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = Color.black;
        }

        var tl = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(tl, TimelineAsset);

        var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioAsset);
        if (audio != null)
        {
            var at = tl.CreateTrack<AudioTrack>(null, "Music");
            var ac = at.CreateClip(audio);
            ac.start = 0;
            ac.duration = Duration;
            ac.clipIn = StartInSong;
        }

        var scenes = tl.CreateTrack<SceneTrack>(null, "Scenes");
        ShowScene[] seq = { ShowScene.Beams, ShowScene.Tunnel, ShowScene.Starburst, ShowScene.Lasers, ShowScene.Rings, ShowScene.Spiral };
        double pos = 0;
        double slot = Duration / seq.Length;
        foreach (var s in seq)
        {
            var clip = scenes.CreateClip<SceneClip>();
            clip.start = pos;
            clip.duration = slot;
            clip.displayName = s.ToString();
            ((SceneClip)clip.asset).template.scene = s;
            pos += slot;
        }

        AssetDatabase.SaveAssets();

        var show = GameObject.Find("Show");
        if (show == null) show = new GameObject("Show");
        var director = show.GetComponent<PlayableDirector>();
        if (director == null) director = show.AddComponent<PlayableDirector>();
        director.playableAsset = tl;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.None;
        EditorUtility.SetDirty(director);

        EditorUtility.DisplayDialog("Mappa",
            "VideoShow construit : VideoShowDirector + Timeline (Music 40s-70s + piste Scenes).\n" +
            "Ouvre la Timeline sur 'Show' : deplace/allonge les clips d'effet et cale-les sur la waveform.",
            "OK");
    }
}
#endif
