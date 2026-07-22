#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public static class MotionSceneBuilder
{
    private const string AudioAsset = "Assets/Audio/Sweden - My_System.mp3";
    private const string TimelineAsset = "Assets/MotionShow_Timeline.playable";

    [MenuItem("Tools/Mappa/Build Motion Show Scene")]
    public static void Build()
    {
        int stage = EnsureLayer("Stage");

        foreach (var r in Object.FindObjectsByType<AudioReactive>(FindObjectsSortMode.None)) r.enabled = false;
        foreach (var r in Object.FindObjectsByType<IlluminatorRig>(FindObjectsSortMode.None)) r.enabled = false;

        var field = Object.FindFirstObjectByType<EntityField>();
        if (field == null) { EditorUtility.DisplayDialog("Mappa", "Pas de mur (EntityField) dans la scene.", "OK"); return; }
        var wall = field.gameObject;

        var stageCam = GameObject.Find("StageCam");
        if (stageCam == null) stageCam = new GameObject("StageCam");
        var cam = stageCam.GetComponent<Camera>();
        if (cam == null) cam = stageCam.AddComponent<Camera>();
        stageCam.transform.position = new Vector3(0, 0, -10);
        stageCam.transform.rotation = Quaternion.identity;
        cam.orthographic = true;
        cam.orthographicSize = 1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 1 << stage;
        var listener = stageCam.GetComponent<AudioListener>();
        if (listener != null) Object.DestroyImmediate(listener);

        var sampler = wall.GetComponent<CameraSampler>();
        if (sampler == null) sampler = wall.AddComponent<CameraSampler>();
        sampler.source = cam;

        var main = Camera.main;
        if (main != null)
        {
            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = Color.black;
            main.cullingMask &= ~(1 << stage);
        }

        var red = LoadMat("Assets/Materials/Laser_Red.mat");
        var hot = LoadMat("Assets/Materials/Laser_HotRed.mat");
        var white = LoadMat("Assets/Materials/Flash_White.mat");

        var ball = MakePrimitive(PrimitiveType.Sphere, "Ball", stage, white);
        ball.transform.localScale = Vector3.one * 0.3f;

        var ring = MakePrimitive(PrimitiveType.Cylinder, "Ring", stage, red);
        ring.transform.rotation = Quaternion.Euler(90, 0, 0);
        ring.transform.localScale = new Vector3(0.6f, 0.03f, 0.6f);

        var rays = new GameObject("Rays");
        rays.layer = stage;
        rays.transform.position = Vector3.zero;
        for (int i = 0; i < 8; i++)
        {
            var bar = MakePrimitive(PrimitiveType.Cube, "Trait" + i, stage, hot);
            bar.transform.SetParent(rays.transform, false);
            bar.transform.localScale = new Vector3(0.02f, 0.5f, 0.02f);
            float a = i * 45f * Mathf.Deg2Rad;
            bar.transform.localPosition = new Vector3(Mathf.Sin(a) * 0.35f, Mathf.Cos(a) * 0.35f, 0f);
            bar.transform.localRotation = Quaternion.Euler(0, 0, -i * 45f);
        }

        BuildTimeline(ball, ring, rays);

        EditorUtility.DisplayDialog("Mappa",
            "Scene construite : StageCam + CameraSampler + Ball / Ring / Rays (primitives) + Timeline.\n" +
            "Ouvre la Timeline sur 'Show' et pose tes keyframes.", "OK");
    }

    private static GameObject MakePrimitive(PrimitiveType type, string name, int layer, Material mat)
    {
        var go = GameObject.Find(name);
        if (go == null) go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.layer = layer;
        go.transform.position = Vector3.zero;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private static Material LoadMat(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        return m != null ? m : new Material(Shader.Find("Unlit/Color"));
    }

    private static void BuildTimeline(GameObject ball, GameObject ring, GameObject rays)
    {
        var tl = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(tl, TimelineAsset);

        var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioAsset);
        if (audio != null)
        {
            var at = tl.CreateTrack<AudioTrack>(null, "Music");
            at.CreateClip(audio).start = 0;
        }

        tl.CreateTrack<AnimationTrack>(null, "Ball");
        tl.CreateTrack<AnimationTrack>(null, "Ring");
        tl.CreateTrack<AnimationTrack>(null, "Rays");
        AssetDatabase.SaveAssets();

        var showGo = GameObject.Find("Show");
        if (showGo == null) showGo = new GameObject("Show");
        var director = showGo.GetComponent<PlayableDirector>();
        if (director == null) director = showGo.AddComponent<PlayableDirector>();
        director.playableAsset = tl;

        int t = 0;
        foreach (var track in tl.GetOutputTracks())
        {
            if (!(track is AnimationTrack)) continue;
            director.SetGenericBinding(track, t == 0 ? ball : t == 1 ? ring : rays);
            t++;
        }
        EditorUtility.SetDirty(director);
    }

    private static int EnsureLayer(string name)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
        for (int i = 8; i < layers.arraySize; i++)
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }
        return 0;
    }
}
#endif
