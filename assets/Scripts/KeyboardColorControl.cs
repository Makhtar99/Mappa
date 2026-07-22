using System;
using UnityEngine;

/// <summary>
/// Interactivite clavier : chaque touche declaree impose une couleur au
/// projecteur central (entite 10, cf. `mappa ramp configs/ecran.json --entity 10`).
/// La couleur part en eHuB via DeviceEmitter ; Mappa.Ui (source eHuB cochee,
/// port 8765) la route ensuite en ArtNet vers le controleur reel.
/// Un temoin visuel (sphere au centre de la scene) reflete la couleur envoyee.
/// </summary>
[DefaultExecutionOrder(-40)]
public sealed class KeyboardColorControl : MonoBehaviour
{
    [Serializable]
    public struct Binding
    {
        public KeyCode key;
        public Color color;
    }

    public Binding[] bindings =
    {
        new Binding { key = KeyCode.Space, color = Color.red },
        new Binding { key = KeyCode.B,     color = Color.blue },
        new Binding { key = KeyCode.G,     color = Color.green },
        new Binding { key = KeyCode.Y,     color = Color.yellow },
        new Binding { key = KeyCode.W,     color = Color.white },
    };

    [Tooltip("Duree du fondu vers la nouvelle couleur, 0 = instantane.")]
    public float fade = 0.15f;

    [Header("Projecteur central")]
    [Tooltip("Entite eHuB du projecteur central (meme id que `ramp --entity 10`).")]
    public int projectorEntityId = 10;

    [Tooltip("Cree un DeviceEmitter + ProjectorController si la scene n'en a pas (Demo).")]
    public bool autoCreateProjector = true;

    [Tooltip("Affiche une sphere temoin au centre de la scene.")]
    public bool showVisual = true;

    private ProjectorController[] _projectors;
    private Material _visualMat;
    private Color _current = Color.white;
    private Color _target = Color.white;
    private bool _active;
    private float _refresh;

    /// <summary>Se pose tout seul dans n'importe quelle scene, pour tester sans rien cabler.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindFirstObjectByType<KeyboardColorControl>() != null) return;
        new GameObject("KeyboardColorControl").AddComponent<KeyboardColorControl>();
    }

    private void Update()
    {
        Rescan();
        ReadKeys();
        if (!_active) return;

        if (_projectors != null)
            for (int i = 0; i < _projectors.Length; i++)
                if (_projectors[i] != null) _projectors[i].color = _current;

        if (_visualMat != null) _visualMat.color = _current;
    }

    private void ReadKeys()
    {
        for (int i = 0; i < bindings.Length; i++)
        {
            if (!Input.GetKeyDown(bindings[i].key)) continue;
            _target = bindings[i].color;
            _active = true;
            break;
        }

        if (!_active) return;
        _current = fade <= 0f
            ? _target
            : Color.Lerp(_current, _target, Mathf.Clamp01(Time.deltaTime / fade));
    }

    /// <summary>Le projecteur peut naitre a l'execution (EurovisionShow) : on re-scanne periodiquement.</summary>
    private void Rescan()
    {
        _refresh -= Time.deltaTime;
        if (_refresh > 0f) return;
        _refresh = 0.5f;

        _projectors = FindObjectsByType<ProjectorController>(FindObjectsSortMode.None);
        if (_projectors.Length == 0 && autoCreateProjector) CreateProjector();
    }

    private void CreateProjector()
    {
        var go = new GameObject("ProjecteurCentral");
        go.transform.SetParent(transform, false);
        var emitter = go.AddComponent<DeviceEmitter>();
        var proj = go.AddComponent<ProjectorController>();
        proj.emitter = emitter;
        proj.entityId = projectorEntityId;
        proj.color = _current;
        _projectors = new[] { proj };

        if (showVisual) CreateVisual(go.transform);
    }

    private void CreateVisual(Transform parent)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "Temoin";
        UnityEngine.Object.Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(parent, false);
        s.transform.localPosition = new Vector3(0f, 0f, -0.3f);
        s.transform.localScale = Vector3.one * 0.25f;

        var shader = Shader.Find("Unlit/Color");
        _visualMat = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        _visualMat.color = _current;
        s.GetComponent<MeshRenderer>().sharedMaterial = _visualMat;
    }

    private void OnDestroy()
    {
        if (_visualMat != null) Destroy(_visualMat);
    }
}
