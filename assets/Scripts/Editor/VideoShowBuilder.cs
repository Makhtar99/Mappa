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

        // Ajoute les projecteurs (Projector + 4 Lyres + Timeline diagonales
        // rouge/blanc). Ils coexistent avec le design du mur : le
        // VideoShowDirector pilote le mur (EntityField), les projecteurs ont
        // leur propre DeviceEmitter et leur propre timeline 30 s.
        BuildProjecteurs();

        EditorUtility.DisplayDialog("Mappa",
            "VideoShow construit : VideoShowDirector + Timeline (Music 40s-70s + piste Scenes).\n" +
            "Projecteurs ajoutes : Projector + 4 Lyres + 'Timeline Projecteurs' (diagonales rouge/blanc, 30 s).\n" +
            "Ouvre la Timeline sur 'Show' : deplace/allonge les clips d'effet et cale-les sur la waveform.",
            "OK");
    }

    // ------------------------------------------------------------------ //
    // Cree le rig de projecteurs et sa timeline, cable sur un DeviceEmitter
    // dedie. Idempotent : ne recree rien si "Projecteurs" existe deja.
    // ------------------------------------------------------------------ //
    private static void BuildProjecteurs()
    {
        if (GameObject.Find("Projecteurs") != null) return; // deja construit

        var root = new GameObject("Projecteurs");

        // Emetteur reseau dedie aux projecteurs (univers 33, comme ecran.json).
        var emitterGo = new GameObject("DeviceEmitter");
        emitterGo.transform.SetParent(root.transform, false);
        var emitter = emitterGo.AddComponent<DeviceEmitter>();
        emitter.ip = "127.0.0.1";
        emitter.port = 8765;
        emitter.universe = 33;
        emitter.fps = 40f;

        // Projecteur statique au centre (entites 1..4).
        var projGo = new GameObject("Projector");
        projGo.transform.SetParent(root.transform, false);
        projGo.transform.localPosition = new Vector3(0f, -1.15f, 0f);
        var projector = projGo.AddComponent<ProjectorController>();
        projector.emitter = emitter;
        projector.baseEntityId = 1;
        projector.color = Color.white;
        projGo.AddComponent<ProjectorVisual>();

        // 4 lyres, gauche -> droite (entites 10/30/50/70).
        var lyres = new LyreController[4];
        int[] bases = { 10, 30, 50, 70 };
        float[] xs = { -1.0f, -0.4f, 0.4f, 1.0f };
        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject("Lyre " + (i + 1));
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(xs[i], -1.15f, 0f);
            var lyre = go.AddComponent<LyreController>();
            lyre.emitter = emitter;
            lyre.baseEntityId = bases[i];
            lyre.color = Color.red;
            var vis = go.AddComponent<ProjectorVisual>();
            vis.length = 2.5f; vis.radius = 0.3f;
            vis.panSwingDeg = 55f; vis.tiltSwingDeg = 55f;
            // Le mouvement pan/tilt est pilote par la timeline : on ajoute
            // LyreMovement (desactive par la timeline) juste pour rester
            // coherent avec les autres scenes.
            go.AddComponent<LyreMovement>();
            lyres[i] = lyre;
        }

        // Timeline des projecteurs : 30 s de diagonales rouge/blanc.
        var tlGo = new GameObject("Timeline Projecteurs");
        tlGo.transform.SetParent(root.transform, false);
        var pt = tlGo.AddComponent<ProjectorTimeline>();
        pt.lyres = lyres;
        pt.projector = projector;
        pt.duration = 30f;
        pt.loop = true;
        pt.playOnStart = true;
        pt.alternateByLyre = true;

        EditorUtility.SetDirty(root);
    }
}
#endif
