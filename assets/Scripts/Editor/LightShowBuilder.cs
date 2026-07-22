#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public static class LightShowBuilder
{
    private const string AudioAsset = "Assets/Audio/Sweden - My_System.mp3";
    private const string TimelineAsset = "Assets/LightShow_Timeline.playable";
    private const double StartInSong = 40.0;
    private const double Duration = 30.0;

    [MenuItem("Tools/Mappa/Build LightShow (Timeline 40s-70s)")]
    public static void Build()
    {
        foreach (var r in Object.FindObjectsByType<AudioReactive>(FindObjectsSortMode.None)) r.enabled = false;
        foreach (var r in Object.FindObjectsByType<IlluminatorRig>(FindObjectsSortMode.None)) r.enabled = false;

        if (Object.FindFirstObjectByType<EntityField>() == null)
        {
            EditorUtility.DisplayDialog("Mappa", "Pas de mur (EntityField) dans la scene.", "OK");
            return;
        }

        var fx = GameObject.Find("Effects");
        if (fx == null) fx = new GameObject("Effects");
        Ensure<RadialBurstEffect>(fx);
        Ensure<StarSpinEffect>(fx);
        Ensure<PolygonTunnelEffect>(fx);
        Ensure<ShowAudioSource>(fx);

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

        AddEffect(tl, "Burst", ShowEffectKind.RadialBurst, 0, 12, 0, 2);
        AddEffect(tl, "Star", ShowEffectKind.Star, 9, 12, 2, 2);
        AddEffect(tl, "Tunnel", ShowEffectKind.PolygonTunnel, 18, 12, 2, 0);

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
            "LightShow construit : Effects (Burst/Star/Tunnel) + ShowAudioSource + Timeline.\n" +
            "Audio cale sur 40s -> 70s de la chanson (30s). Ouvre la Timeline sur 'Show' pour deplacer/allonger les clips.",
            "OK");
    }

    private static void AddEffect(TimelineAsset tl, string name, ShowEffectKind kind,
        double start, double dur, double easeIn, double easeOut)
    {
        var track = tl.CreateTrack<EffectTrack>(null, name);
        var clip = track.CreateClip<EffectClip>();
        clip.start = start;
        clip.duration = dur;
        clip.easeInDuration = easeIn;
        clip.easeOutDuration = easeOut;
        ((EffectClip)clip.asset).template.kind = kind;
        clip.displayName = name;
    }

    private static void Ensure<T>(GameObject go) where T : Component
    {
        if (go.GetComponent<T>() == null) go.AddComponent<T>();
    }
}
#endif
