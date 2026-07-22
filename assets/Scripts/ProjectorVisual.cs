using UnityEngine;

// Aperçu visuel d'un projecteur ou d'une lyre dans Unity : un cône coloré
// qui sort du GameObject, avec la teinte et l'intensité fournies par le
// ProjectorController ou le LyreController attaché au même GameObject.
//
// Purement décoratif : n'affecte pas ce qui est envoyé sur le réseau DMX.
// Le cône est visible dans les vues Scene ET Game.
//
// Usage :
//   1) Add Component -> "Projector Visual" sur le GameObject Projector ou Lyre X.
//   2) (Optionnel) régler la longueur, le rayon et l'opacité dans l'Inspector.
//   3) Passe en mode Play : le cône prend la couleur du controller.
[ExecuteAlways]
[DefaultExecutionOrder(50)]
public sealed class ProjectorVisual : MonoBehaviour
{
    [Header("Forme du cone")]
    [Tooltip("Longueur du faisceau (unites Unity).")]
    public float length = 1.5f;
    [Tooltip("Rayon a l'extremite du cone.")]
    public float radius = 0.35f;
    [Range(0f, 1f)] public float opacity = 0.7f;
    [Tooltip("Nombre de facettes du cone (plus = plus rond).")]
    [Range(8, 48)] public int segments = 20;

    [Header("Orientation")]
    [Tooltip("Direction locale du faisceau (par defaut vers +Y = haut).")]
    public Vector3 direction = Vector3.up;
    [Tooltip("Pour une lyre : true = tourne le cone selon pan/tilt du LyreController.")]
    public bool followLyreOrientation = true;
    [Tooltip("Amplitude max du pan en degres.")]
    public float panSwingDeg = 45f;
    [Tooltip("Amplitude max du tilt en degres.")]
    public float tiltSwingDeg = 45f;

    private ProjectorController _projector;
    private LyreController _lyre;
    private Transform _cone;
    private MeshRenderer _renderer;
    private Material _material;
    private Mesh _mesh;

    private void OnEnable()
    {
        _projector = GetComponent<ProjectorController>();
        _lyre = GetComponent<LyreController>();
        EnsureCone();
        Sync();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying) DestroyCone();
    }

    private void OnDestroy() => DestroyCone();

    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        EnsureCone();
        Sync();
    }

    private void Update() => Sync();

    private void Sync()
    {
        if (_cone == null || _material == null) return;

        // 1) Couleur = celle du controller.
        Color c = Color.white;
        float d = 1f;
        bool on = true;

        if (_projector != null)
        {
            c = _projector.color;
            d = _projector.dimmer;
            on = _projector.isOn;
            if (_projector.white > 0f)
                c = Color.Lerp(c, Color.white, _projector.white * 0.5f);
        }
        else if (_lyre != null)
        {
            c = _lyre.color;
            d = _lyre.dimmer;
            if (_lyre.white > 0f)
                c = Color.Lerp(c, Color.white, _lyre.white * 0.5f);
        }

        d = Mathf.Clamp01(d);
        Color lit = new Color(c.r * d, c.g * d, c.b * d, opacity * Mathf.Max(d, 0.05f));
        if (!on) lit.a = 0f;

        if (_material.HasProperty("_Color")) _material.SetColor("_Color", lit);
        if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", lit);
        _material.color = lit;
        _renderer.enabled = (on && d > 0.001f);

        // 2) Orientation : pour une lyre, oriente selon pan/tilt.
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        if (_lyre != null && followLyreOrientation)
        {
            float pan = (_lyre.pan - 0.5f) * 2f;    // -1..+1
            float tilt = (_lyre.tilt - 0.5f) * 2f;  // -1..+1
            Quaternion panQ = Quaternion.AngleAxis(pan * panSwingDeg, Vector3.forward);
            Quaternion tiltQ = Quaternion.AngleAxis(-tilt * tiltSwingDeg, Vector3.right);
            rot = rot * panQ * tiltQ;
        }
        _cone.localRotation = rot;
        _cone.localPosition = Vector3.zero;
        _cone.localScale = Vector3.one;
    }

    private void EnsureCone()
    {
        if (_cone != null && _material != null && _mesh != null) return;
        DestroyCone();

        var go = new GameObject("~ConeVisual");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetParent(transform, false);

        var mf = go.AddComponent<MeshFilter>();
        _renderer = go.AddComponent<MeshRenderer>();
        _mesh = BuildConeMesh(radius, length, Mathf.Clamp(segments, 8, 48));
        mf.sharedMesh = _mesh;

        var shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Standard");
        _material = new Material(shader) { color = Color.white };
        _material.hideFlags = HideFlags.HideAndDontSave;
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;

        _cone = go.transform;
    }

    private void DestroyCone()
    {
        if (_cone != null)
        {
            if (Application.isPlaying) Destroy(_cone.gameObject);
            else DestroyImmediate(_cone.gameObject);
            _cone = null;
        }
        if (_material != null)
        {
            if (Application.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
            _material = null;
        }
        if (_mesh != null)
        {
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }
        _renderer = null;
    }

    private static Mesh BuildConeMesh(float radius, float height, int segments)
    {
        // Cone dont la pointe est a l'origine et la base a (0, height, 0).
        int vCount = segments + 2;
        var verts = new Vector3[vCount];
        var norms = new Vector3[vCount];
        var uvs = new Vector2[vCount];

        verts[0] = Vector3.zero;
        norms[0] = Vector3.down;
        uvs[0] = new Vector2(0.5f, 0f);

        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * radius;
            float z = Mathf.Sin(a) * radius;
            verts[i + 1] = new Vector3(x, height, z);
            norms[i + 1] = new Vector3(x, radius, z).normalized;
            uvs[i + 1] = new Vector2((x / radius + 1f) * 0.5f, 1f);
        }

        verts[segments + 1] = new Vector3(0f, height, 0f);
        norms[segments + 1] = Vector3.up;
        uvs[segments + 1] = new Vector2(0.5f, 1f);

        var tris = new int[segments * 6];
        int t = 0;
        for (int i = 0; i < segments; i++)
        {
            int next = i + 1 < segments ? i + 2 : 1;
            tris[t++] = 0; tris[t++] = i + 1; tris[t++] = next;
        }
        for (int i = 0; i < segments; i++)
        {
            int next = i + 1 < segments ? i + 2 : 1;
            tris[t++] = segments + 1; tris[t++] = next; tris[t++] = i + 1;
        }

        var mesh = new Mesh { name = "ProjectorCone" };
        mesh.hideFlags = HideFlags.HideAndDontSave;
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}
