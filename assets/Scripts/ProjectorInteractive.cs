using System;
using UnityEngine;

// Interactivite clavier sur le PROJECTEUR CENTRAL (critere P6).
//
// La scene VideoShow fait tourner ProjectorTimeline, une sequence automatique
// de 30 s qui pilote deja le projecteur. Ce script ne la desactive pas : il
// s'execute APRES elle (ordre 200 contre 100) et ecrase ses valeurs uniquement
// quand l'operateur a pris la main. Tant qu'aucune touche n'a ete pressee, la
// timeline se deroule normalement.
//
//   1re touche pressee  -> mode MANUEL (la timeline continue mais n'a plus
//                          d'effet visible sur le projecteur)
//   Echap               -> retour en mode AUTO (la timeline reprend la main)
//
// Touches par defaut :
//   R V B J C M   couleurs (rouge, vert, bleu, jaune, cyan, magenta)
//   W             canal blanc dedie
//   N             noir (extinction douce, garde le mode manuel)
//   Espace        allume / eteint
//   Haut / Bas    dimmer +/- 10 %
//   Echap         rendre la main a la timeline
//   H             afficher / masquer l'aide a l'ecran
[DefaultExecutionOrder(200)] // apres ProjectorTimeline (100) : on a le dernier mot.
public sealed class ProjectorInteractive : MonoBehaviour
{
    [Serializable]
    public struct ColorBinding
    {
        public KeyCode key;
        public Color color;
        [Tooltip("Valeur du canal W dedie (0..1) pour cette touche.")]
        [Range(0f, 1f)] public float white;
    }

    [Header("Cible")]
    [Tooltip("Projecteur central. Laisse vide : cherche automatiquement dans la scene.")]
    public ProjectorController target;

    [Header("Couleurs")]
    public ColorBinding[] bindings =
    {
        new ColorBinding { key = KeyCode.R, color = Color.red,     white = 0f },
        new ColorBinding { key = KeyCode.V, color = Color.green,   white = 0f },
        new ColorBinding { key = KeyCode.B, color = Color.blue,    white = 0f },
        new ColorBinding { key = KeyCode.J, color = new Color(1f, 1f, 0f), white = 0f },
        new ColorBinding { key = KeyCode.C, color = Color.cyan,    white = 0f },
        new ColorBinding { key = KeyCode.M, color = Color.magenta, white = 0f },
        new ColorBinding { key = KeyCode.W, color = Color.black,   white = 1f },
        new ColorBinding { key = KeyCode.N, color = Color.black,   white = 0f },
    };

    [Header("Touches de controle")]
    public KeyCode toggleKey  = KeyCode.Space;
    public KeyCode dimUpKey   = KeyCode.UpArrow;
    public KeyCode dimDownKey = KeyCode.DownArrow;
    [Tooltip("Rend la main a ProjectorTimeline.")]
    public KeyCode releaseKey = KeyCode.Escape;
    public KeyCode helpKey    = KeyCode.H;

    [Header("Reglages")]
    [Tooltip("Duree du fondu vers la nouvelle couleur en secondes. 0 = instantane.")]
    [Range(0f, 2f)] public float fade = 0.15f;

    [Tooltip("Pas du dimmer a chaque appui sur Haut / Bas.")]
    [Range(0.01f, 0.5f)] public float dimmerStep = 0.1f;

    [Tooltip("Affiche le rappel des touches en haut a gauche de la vue Game.")]
    public bool showHelp = true;

    // Etat interne : tant que _manual est faux, on ne touche a rien.
    private bool _manual;
    private bool _isOn = true;
    private Color _targetColor = Color.white;
    private Color _currentColor = Color.white;
    private float _targetWhite;
    private float _currentWhite;
    private float _dimmer = 1f;

    /// <summary>Vrai quand l'operateur a pris la main sur la timeline.</summary>
    public bool IsManual => _manual;

    private void Awake()
    {
        if (target == null) target = FindFirstObjectByType<ProjectorController>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(helpKey)) showHelp = !showHelp;

        if (target == null) return;

        ReadKeys();
        if (!_manual) return;

        // Fondu vers la couleur visee.
        if (fade <= 0f)
        {
            _currentColor = _targetColor;
            _currentWhite = _targetWhite;
        }
        else
        {
            float t = Mathf.Clamp01(Time.deltaTime / fade);
            _currentColor = Color.Lerp(_currentColor, _targetColor, t);
            _currentWhite = Mathf.Lerp(_currentWhite, _targetWhite, t);
        }

        // On ecrase ce que la timeline vient d'ecrire (elle tourne a l'ordre 100).
        target.isOn = _isOn;
        target.color = _currentColor;
        target.white = _currentWhite;
        target.dimmer = _dimmer;
    }

    private void ReadKeys()
    {
        if (Input.GetKeyDown(releaseKey))
        {
            _manual = false; // la timeline reprend la main des la frame suivante.
            return;
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            if (!Input.GetKeyDown(bindings[i].key)) continue;
            TakeOver();
            _targetColor = bindings[i].color;
            _targetWhite = bindings[i].white;
            _isOn = true;
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            TakeOver();
            _isOn = !_isOn;
        }
        if (Input.GetKeyDown(dimUpKey))
        {
            TakeOver();
            _dimmer = Mathf.Clamp01(_dimmer + dimmerStep);
        }
        if (Input.GetKeyDown(dimDownKey))
        {
            TakeOver();
            _dimmer = Mathf.Clamp01(_dimmer - dimmerStep);
        }
    }

    /// <summary>Passe en manuel en repartant de l'etat courant du projecteur (pas de saut visuel).</summary>
    private void TakeOver()
    {
        if (_manual) return;
        _manual = true;
        _currentColor = target.color;
        _currentWhite = target.white;
        _targetColor = target.color;
        _targetWhite = target.white;
        _dimmer = target.dimmer;
        _isOn = target.isOn;
    }

    private GUIStyle _style; // OnGUI est appele plusieurs fois par frame : on ne realloue pas.

    private void OnGUI()
    {
        if (!showHelp) return;

        const int w = 260;
        const int h = 168;
        GUI.Box(new Rect(10, 10, w, h), GUIContent.none);

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
        }
        GUIStyle style = _style;

        string mode = _manual
            ? "<color=#7CFC7C>MANUEL</color>"
            : "<color=#9AA0A6>AUTO (timeline)</color>";

        GUI.Label(new Rect(20, 16, w - 20, 20), "Projecteur central : " + mode, style);
        GUI.Label(new Rect(20, 36, w - 20, 20), "R V B J C M  couleurs", style);
        GUI.Label(new Rect(20, 54, w - 20, 20), "W  blanc      N  noir", style);
        GUI.Label(new Rect(20, 72, w - 20, 20), "Espace  allumer / eteindre", style);
        GUI.Label(new Rect(20, 90, w - 20, 20), "Haut / Bas  dimmer", style);
        GUI.Label(new Rect(20, 108, w - 20, 20), "Echap  rendre la main", style);
        GUI.Label(new Rect(20, 126, w - 20, 20), "H  masquer cette aide", style);

        if (target != null)
        {
            GUI.Label(new Rect(20, 144, w - 20, 20),
                string.Format("dimmer {0:P0}   {1}", target.dimmer, target.isOn ? "ON" : "OFF"), style);
        }
    }
}
