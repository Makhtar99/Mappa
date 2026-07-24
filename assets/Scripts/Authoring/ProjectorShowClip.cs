using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// Clip Timeline qui rejoue la sequence "diagonales rouge/blanc" des
// projecteurs (4 lyres + projecteur statique) directement depuis la Timeline
// principale (PlayableDirector du GameObject "Show").
//
// Difference cle avec l'ancien MonoBehaviour ProjectorTimeline :
//   - L'horloge vient de la Timeline (playable.GetTime()), plus de _t interne.
//   - Pas de boucle infinie : la sequence dure exactement le temps du clip.
//   - Quand le clip se termine (donc quand la Timeline se termine si le clip
//     va jusqu'au bout), OnBehaviourPause ETEINT les projecteurs (dimmer 0 +
//     isOn false). C'est ce qui garantit que les projecteurs s'arretent avec
//     la Timeline.
//
// Les cibles (lyres / projector) sont resolues par nom sous le GameObject
// "Projecteurs" au premier ProcessFrame, pour rester serialisable dans un
// asset .playable (les references de scene ne se serialisent pas dans un
// PlayableAsset).
[System.Serializable]
public class ProjectorShowBehaviour : PlayableBehaviour
{
    [Header("Sequence")]
    [Tooltip("Nombre d'aller-retours de la diagonale sur toute la duree du clip.")]
    public float sweepCycles = 3f;

    [Tooltip("Amplitude horizontale de la diagonale (0..0.5).")]
    [Range(0f, 0.5f)] public float panAmplitude = 0.35f;

    [Tooltip("Amplitude verticale de la diagonale (0..0.5).")]
    [Range(0f, 0.5f)] public float tiltAmplitude = 0.35f;

    [Tooltip("Centre vertical du faisceau (0.5 = horizontal).")]
    [Range(0f, 1f)] public float tiltCenter = 0.5f;

    [Tooltip("Duree d'un cycle rouge<->blanc en secondes.")]
    public float colorPeriod = 4f;

    [Tooltip("Alterner rouge/blanc entre lyres paires et impaires.")]
    public bool alternateByLyre = true;

    [Tooltip("Duree du fondu d'entree et de sortie (secondes).")]
    public float fadeSeconds = 2f;

    private static readonly Color Red = new Color(1f, 0f, 0f, 1f);

    private LyreController[] _lyres;
    private ProjectorController _projector;
    private bool _resolved;

    // Resout les cibles dans la scene (sous le GameObject "Projecteurs").
    private void ResolveTargets()
    {
        _resolved = true;

        var found = Object.FindObjectsByType<LyreController>(FindObjectsSortMode.InstanceID);
        // Trie par baseEntityId pour un ordre stable gauche -> droite (10/30/50/70).
        System.Array.Sort(found, (a, b) => a.baseEntityId.CompareTo(b.baseEntityId));
        _lyres = found;

        _projector = Object.FindFirstObjectByType<ProjectorController>();

        // Met en pause les LyreMovement pendant le clip pour que la sequence
        // ne soit pas ecrasee par le pattern de mouvement automatique. On ne
        // les reactive pas a la fin (voir OnBehaviourPause / ProjectorShowStopper).
        for (int i = 0; i < _lyres.Length; i++)
        {
            if (_lyres[i] == null) continue;
            var mv = _lyres[i].GetComponent<LyreMovement>();
            if (mv != null) mv.enabled = false;
        }
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!_resolved) ResolveTargets();

        double duree = playable.GetDuration();
        double time = playable.GetTime();
        float t = (float)time;
        float progress = duree > 0.0001 ? (float)(time / duree) : 0f;

        // Couleur : onde carree rouge<->blanc (pas de teinte intermediaire).
        float wave = Mathf.Sin(t / Mathf.Max(0.1f, colorPeriod) * Mathf.PI * 2f);
        float timeMix = wave >= 0f ? 1f : 0f; // 1 = blanc, 0 = rouge

        // Fondu d'entree/sortie, module aussi par le weight du clip (blending).
        float fade = Mathf.Max(0.01f, fadeSeconds);
        float fadeIn = Mathf.Clamp01(t / fade);
        float fadeOut = Mathf.Clamp01((float)(duree - time) / fade);
        float dim = fadeIn * fadeOut * info.weight;

        // Diagonale : pan ET tilt bougent ensemble -> faisceau oblique.
        float sweep = Mathf.Sin(progress * sweepCycles * Mathf.PI * 2f);

        int n = _lyres != null ? _lyres.Length : 0;
        for (int i = 0; i < n; i++)
        {
            var lyre = _lyres[i];
            if (lyre == null) continue;

            float sign = (i % 2 == 0) ? 1f : -1f;
            float pan = 0.5f + sign * sweep * panAmplitude;
            float tilt = tiltCenter + sign * sweep * tiltAmplitude;

            bool isWhite = alternateByLyre ? (i % 2 != 0) : (timeMix >= 0.5f);

            lyre.pan = Mathf.Clamp01(pan);
            lyre.tilt = Mathf.Clamp01(tilt);
            lyre.dimmer = dim;
            lyre.speed = 0f;
            lyre.strobe = 0f;
            lyre.macro = 0f;

            if (isWhite)
            {
                lyre.color = Color.black;
                lyre.white = 1f;
            }
            else
            {
                lyre.color = Red;
                lyre.white = 0f;
            }
        }

        if (_projector != null)
        {
            _projector.isOn = true;
            _projector.dimmer = dim;
            if (timeMix >= 0.5f)
            {
                _projector.color = Color.black;
                _projector.white = 1f;
            }
            else
            {
                _projector.color = Red;
                _projector.white = 0f;
            }
        }
    }

    // Appele quand le clip se termine OU que la Timeline s'arrete/pause.
    // On eteint les projecteurs. On NE reactive PAS les LyreMovement : sinon
    // ils repartiraient en boucle sur Time.time et feraient "tourner" les
    // tetes apres la fin du show. L'extinction definitive et le maintien a 0
    // sont garantis par ProjectorShowStopper (abonne a director.stopped).
    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        TurnEverythingOff();
    }

    private void TurnEverythingOff()
    {
        if (_lyres != null)
        {
            for (int i = 0; i < _lyres.Length; i++)
            {
                var lyre = _lyres[i];
                if (lyre == null) continue;
                lyre.dimmer = 0f;
                lyre.white = 0f;
                lyre.strobe = 0f;
                lyre.color = Color.black;
            }
        }

        if (_projector != null)
        {
            _projector.dimmer = 0f;
            _projector.white = 0f;
            _projector.color = Color.black;
            _projector.isOn = false;
        }
    }
}

[System.Serializable]
public class ProjectorShowClip : PlayableAsset, ITimelineClipAsset
{
    public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Extrapolation;

    public ProjectorShowBehaviour template = new ProjectorShowBehaviour();

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        => ScriptPlayable<ProjectorShowBehaviour>.Create(graph, template);
}
