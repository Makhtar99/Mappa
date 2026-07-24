using UnityEngine;

// Timeline automatique dediee aux PROJECTEURS (les 4 lyres + le projo statique).
//
// Objectif demo : en mode Play, joue une sequence de ~30 secondes ou les
// projecteurs s'allument uniquement en ROUGE et BLANC et font des DIAGONALES
// (les faisceaux balaient en oblique : pan et tilt bougent ensemble, et une
// lyre sur deux prend la diagonale opposee -> motif en croix diagonale).
//
// Utilisation :
//   1) Cree un GameObject vide (ex: "Timeline Projecteurs").
//   2) Add Component -> "Projector Timeline".
//   3) Glisse les 4 LyreController (Lyre 1..4) dans "lyres" (ordre gauche->droite).
//   4) (Optionnel) glisse le ProjectorController statique dans "projector".
//   5) Passe en mode Play : la sequence demarre automatiquement.
//
// La timeline prend la main sur pan/tilt/color/dimmer des lyres. Elle desactive
// temporairement les LyreMovement pendant qu'elle tourne (pour eviter que le
// pattern de mouvement ecrase les croisements), puis les reactive a la fin.
[DefaultExecutionOrder(100)] // apres LyreMovement (-100) : la timeline a le dernier mot.
public sealed class ProjectorTimeline : MonoBehaviour
{
    [Header("Cibles (ordre gauche -> droite)")]
    [Tooltip("Les 4 lyres, de la gauche vers la droite (Lyre 1..4).")]
    public LyreController[] lyres = new LyreController[0];

    [Tooltip("Projecteur statique du centre (optionnel).")]
    public ProjectorController projector;

    [Header("Sequence")]
    [Tooltip("Duree totale de la sequence en secondes.")]
    public float duration = 30f;

    [Tooltip("Rejouer la sequence en boucle une fois terminee.")]
    public bool loop = true;

    [Tooltip("Duree du fondu d'extinction en fin de sequence (secondes).")]
    public float fadeOutTime = 4f;

    [Tooltip("Demarrer automatiquement en mode Play.")]
    public bool playOnStart = true;

    [Header("Diagonales")]
    [Tooltip("Nombre d'aller-retours de la diagonale sur toute la duree.")]
    public float sweepCycles = 3f;

    [Tooltip("Amplitude horizontale de la diagonale (0..0.5).")]
    [Range(0f, 0.5f)] public float panAmplitude = 0.35f;

    [Tooltip("Amplitude verticale de la diagonale (0..0.5). Egale au pan = 45 deg.")]
    [Range(0f, 0.5f)] public float tiltAmplitude = 0.35f;

    [Tooltip("Centre vertical du faisceau (0.5 = horizontal).")]
    [Range(0f, 1f)] public float tiltCenter = 0.5f;

    [Header("Couleurs (rouge / blanc uniquement)")]
    [Tooltip("Duree d'un cycle rouge<->blanc en secondes.")]
    public float colorPeriod = 4f;

    [Tooltip("Alterner rouge/blanc entre lyres paires et impaires (au lieu du temps).")]
    public bool alternateByLyre = true;

    private static readonly Color Red = new Color(1f, 0f, 0f, 1f);
    private static readonly Color White = new Color(1f, 1f, 1f, 1f);

    private float _t;
    private bool _running;
    private LyreMovement[] _movements;
    private bool[] _movementWasEnabled;

    private void Start()
    {
        CacheMovements();
        if (playOnStart) Play();
    }

    private void OnDisable() => RestoreMovements();

    /// <summary>Demarre (ou redemarre) la sequence depuis le debut.</summary>
    public void Play()
    {
        _t = 0f;
        _running = true;
        DisableMovements();
    }

    /// <summary>Arrete la sequence et rend la main aux LyreMovement.</summary>
    public void Stop()
    {
        _running = false;
        AllOff();
        RestoreMovements();
    }

    private void AllOff()
    {
        int n = lyres != null ? lyres.Length : 0;
        for (int i = 0; i < n; i++)
        {
            if (lyres[i] == null) continue;
            lyres[i].dimmer = 0f;
            lyres[i].strobe = 0f;
            lyres[i].white = 0f;
            lyres[i].color = Color.black;
        }
        if (projector != null)
        {
            projector.dimmer = 0f;
            projector.white = 0f;
            projector.color = Color.black;
            projector.isOn = false;
        }
    }

    private void Update()
    {
        if (!_running) return;

        _t += Time.deltaTime;
        float duree = Mathf.Max(0.01f, duration);

        if (_t >= duree)
        {
            if (loop) { _t -= duree; }
            else { Stop(); return; }
        }

        // Progression 0..1 sur toute la sequence.
        float progress = _t / duree;

        // --- Couleur : uniquement ROUGE ou BLANC ------------------------ //
        // Onde carree "douce" -> on reste sur rouge ou blanc, pas de teinte
        // intermediaire orangee/rose. mix = 0 (rouge) ou 1 (blanc).
        float wave = Mathf.Sin(_t / Mathf.Max(0.1f, colorPeriod) * Mathf.PI * 2f);
        float timeMix = wave >= 0f ? 1f : 0f; // 1 = blanc, 0 = rouge

        // --- Fondu d'entree/sortie sur les 2 premieres/dernieres sec ---- //
        float fadeIn = Mathf.Clamp01(_t / 2f);
        float fadeOut = loop ? 1f : Mathf.Clamp01((duree - _t) / Mathf.Max(0.1f, fadeOutTime));
        float dim = fadeIn * fadeOut;

        // --- Diagonale : pan ET tilt bougent ENSEMBLE ------------------- //
        // sweep va de -1 a +1. En liant pan et tilt, le faisceau balaie en
        // oblique (diagonale) au lieu d'un simple aller-retour horizontal.
        float sweep = Mathf.Sin(progress * sweepCycles * Mathf.PI * 2f);

        int n = lyres != null ? lyres.Length : 0;
        for (int i = 0; i < n; i++)
        {
            var lyre = lyres[i];
            if (lyre == null) continue;

            // Une lyre sur deux prend la diagonale OPPOSEE : les faisceaux
            // forment une croix diagonale (X) qui balaie l'espace.
            float sign = (i % 2 == 0) ? 1f : -1f;
            float pan  = 0.5f + sign * sweep * panAmplitude;
            float tilt = tiltCenter + sign * sweep * tiltAmplitude; // lie au pan -> diagonale

            // Cette lyre est-elle "blanche" ou "rouge" ?
            //   alternateByLyre : paires = rouge, impaires = blanc.
            //   sinon           : tout le monde suit le temps (timeMix).
            bool isWhite = alternateByLyre ? (i % 2 != 0) : (timeMix >= 0.5f);

            lyre.pan = Mathf.Clamp01(pan);
            lyre.tilt = Mathf.Clamp01(tilt);
            lyre.dimmer = dim;
            lyre.speed = 0f;   // pas de rotation motorisee
            lyre.strobe = 0f;
            lyre.macro = 0f;   // pas de macro couleur (evite les cycles RGB parasites)

            if (isWhite)
            {
                // BLANC via le canal W dedie (les lyres RGBW ne font pas un
                // vrai blanc en additionnant R+V+B : ca donne un cycle/melange
                // sale). On coupe R,V,B et on ouvre le canal blanc.
                lyre.color = Color.black;   // R=V=B=0
                lyre.white = 1f;            // canal W plein
            }
            else
            {
                // ROUGE pur via le canal R.
                lyre.color = Red;           // R=1, V=0, B=0
                lyre.white = 0f;
            }
        }

        // --- Projecteur statique : rouge/blanc dans le temps ------------ //
        if (projector != null)
        {
            projector.isOn = true;
            projector.dimmer = dim;
            if (timeMix >= 0.5f)
            {
                // Blanc via le canal W dedie du projecteur (canal 4).
                projector.color = Color.black;
                projector.white = 1f;
            }
            else
            {
                projector.color = Red;
                projector.white = 0f;
            }
        }
    }

    // ------------------------------------------------------------------ //
    // Gestion des LyreMovement : on les met en pause pendant la timeline.
    // ------------------------------------------------------------------ //

    private void CacheMovements()
    {
        int n = lyres != null ? lyres.Length : 0;
        _movements = new LyreMovement[n];
        _movementWasEnabled = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (lyres[i] == null) continue;
            _movements[i] = lyres[i].GetComponent<LyreMovement>();
        }
    }

    private void DisableMovements()
    {
        if (_movements == null) CacheMovements();
        for (int i = 0; i < _movements.Length; i++)
        {
            if (_movements[i] == null) continue;
            _movementWasEnabled[i] = _movements[i].enabled;
            _movements[i].enabled = false;
        }
    }

    private void RestoreMovements()
    {
        if (_movements == null) return;
        for (int i = 0; i < _movements.Length; i++)
        {
            if (_movements[i] == null) continue;
            _movements[i].enabled = _movementWasEnabled[i];
        }
    }
}
