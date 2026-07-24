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

        // Piste projecteurs : un seul clip qui couvre toute la duree du show.
        // Quand la Timeline se termine, OnBehaviourPause du clip eteint les
        // projecteurs -> ils s'arretent en meme temps que le show.
        var projTrack = tl.CreateTrack<ProjectorShowTrack>(null, "Projecteurs");
        var projClip = projTrack.CreateClip<ProjectorShowClip>();
        projClip.start = 0;
        projClip.duration = Duration;
        projClip.displayName = "Diagonales rouge/blanc";

        AssetDatabase.SaveAssets();

        var show = GameObject.Find("Show");
        if (show == null) show = new GameObject("Show");
        var director = show.GetComponent<PlayableDirector>();
        if (director == null) director = show.AddComponent<PlayableDirector>();
        director.playableAsset = tl;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.None;
        EditorUtility.SetDirty(director);

        // Ajoute les projecteurs (Projector + 4 Lyres). Ils coexistent avec le
        // design du mur : le VideoShowDirector pilote le mur (EntityField), les
        // projecteurs ont leur propre DeviceEmitter et sont pilotes par la piste
        // "Projecteurs" de la Timeline principale.
        var projRoot = BuildProjecteurs();

        // Coupe-circuit : eteint les projecteurs quand la Timeline se termine
        // (comme le panneau LED). S'abonne a director.stopped.
        var stopper = show.GetComponent<ProjectorShowStopper>();
        if (stopper == null) stopper = show.AddComponent<ProjectorShowStopper>();
        stopper.projectorsRoot = projRoot;
        EditorUtility.SetDirty(stopper);

        EditorUtility.DisplayDialog("Mappa",
            "VideoShow construit : VideoShowDirector + Timeline (Music 40s-70s + piste Scenes + piste Projecteurs).\n" +
            "Projecteurs : Projector + 4 Lyres, pilotes par la piste 'Projecteurs' (diagonales rouge/blanc).\n" +
            "Les projecteurs s'eteignent automatiquement a la fin de la Timeline.\n" +
            "Ouvre la Timeline sur 'Show' : deplace/allonge les clips d'effet et cale-les sur la waveform.",
            "OK");
    }

    // ------------------------------------------------------------------ //
    // Cree le rig de projecteurs, cable sur un DeviceEmitter dedie. Idempotent :
    // renvoie le rig existant si "Projecteurs" existe deja. Retourne la racine
    // pour permettre au ProjectorShowStopper de la referencer.
    // ------------------------------------------------------------------ //
    private static GameObject BuildProjecteurs()
    {
        var existing = GameObject.Find("Projecteurs");
        if (existing != null)
        {
            // Deja construit : on nettoie tout ancien ProjectorTimeline autonome
            // (MonoBehaviour qui bouclait a l'infini et gardait les projecteurs
            // allumes apres la fin de la Timeline). Desormais la sequence est
            // pilotee par la piste "Projecteurs" + eteinte par le stopper.
            RemoveLegacyProjectorTimelines();
            return existing;
        }

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

        // La sequence des projecteurs (diagonales rouge/blanc) n'est PLUS un
        // MonoBehaviour autonome qui boucle : elle est desormais pilotee par la
        // piste "Projecteurs" (ProjectorShowTrack) de la Timeline principale.
        // Le clip resout lui-meme ces LyreController / ProjectorController dans
        // la scene, et les eteint quand la Timeline se termine.

        EditorUtility.SetDirty(root);
        return root;
    }

    // ------------------------------------------------------------------ //
    // Repare une scene VideoShow deja construite AVANT l'ajout de la piste
    // Timeline + stopper : supprime le(s) ProjectorTimeline autonome(s) et
    // garantit qu'un ProjectorShowStopper est cable sur le PlayableDirector.
    // A lancer sur la scene ouverte, sans tout reconstruire.
    // ------------------------------------------------------------------ //
    [MenuItem("Tools/Mappa/Fix Projecteurs (arret en fin de Timeline)")]
    public static void FixProjecteurs()
    {
        int removed = RemoveLegacyProjectorTimelines();

        var projRoot = GameObject.Find("Projecteurs");

        // Garantit le stopper sur le GameObject qui porte le PlayableDirector.
        var director = Object.FindFirstObjectByType<PlayableDirector>();
        bool stopperOk = false;
        if (director != null)
        {
            var stopper = director.GetComponent<ProjectorShowStopper>();
            if (stopper == null) stopper = director.gameObject.AddComponent<ProjectorShowStopper>();
            stopper.projectorsRoot = projRoot;
            EditorUtility.SetDirty(stopper);
            stopperOk = true;
        }

        if (director != null)
        {
            var scene = director.gameObject.scene;
            if (scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }

        EditorUtility.DisplayDialog("Mappa - Fix Projecteurs",
            $"ProjectorTimeline autonome(s) supprime(s) : {removed}\n" +
            (stopperOk
                ? "ProjectorShowStopper cable sur le PlayableDirector.\n"
                : "ATTENTION : aucun PlayableDirector trouve dans la scene, stopper non cable.\n") +
            "Pense a sauvegarder la scene (Ctrl+S).",
            "OK");
    }

    // Detruit tous les composants ProjectorTimeline de la scene (et le
    // GameObject "Timeline Projecteurs" s'il devient vide). Retourne le nombre
    // de composants supprimes.
    private static int RemoveLegacyProjectorTimelines()
    {
        var legacies = Object.FindObjectsByType<ProjectorTimeline>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        foreach (var pt in legacies)
        {
            if (pt == null) continue;
            var go = pt.gameObject;
            Object.DestroyImmediate(pt);
            count++;
            // Si le GameObject etait le conteneur dedie et n'a plus que son
            // Transform, on le supprime aussi pour ne pas laisser un objet vide.
            if (go != null && go.name == "Timeline Projecteurs"
                && go.GetComponents<Component>().Length <= 1)
            {
                Object.DestroyImmediate(go);
            }
        }
        return count;
    }
}
#endif
