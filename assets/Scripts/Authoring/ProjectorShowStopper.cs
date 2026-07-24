using UnityEngine;
using UnityEngine.Playables;

// Garantit que les projecteurs (lyres + projecteur) s'eteignent quand la
// Timeline du show se termine, exactement comme le panneau LED.
//
// Pourquoi ce composant plutot que juste OnBehaviourPause dans le clip :
//   - Les LyreController / ProjectorController ont un Update() qui reemet leur
//     etat A CHAQUE frame. Meme si le clip remet dimmer=0 sur sa derniere
//     frame, il faut EMPECHER que quoi que ce soit (LyreMovement, une autre
//     timeline, l'inertie du dernier etat) rallume ou fasse bouger les tetes.
//   - PlayableDirector.stopped est l'evenement fiable de "fin de Timeline".
//     On y coupe tout et on desactive les mouvements pour de bon.
//
// A placer sur le meme GameObject que le PlayableDirector ("Show"), en lui
// donnant la racine du rig projecteurs ("Projecteurs").
[RequireComponent(typeof(PlayableDirector))]
public sealed class ProjectorShowStopper : MonoBehaviour
{
    [Tooltip("Racine des projecteurs (GameObject 'Projecteurs'). Si vide, recherche par nom.")]
    public GameObject projectorsRoot;

    private PlayableDirector _director;

    private void Awake()
    {
        _director = GetComponent<PlayableDirector>();
    }

    private void OnEnable()
    {
        if (_director == null) _director = GetComponent<PlayableDirector>();
        if (_director != null) _director.stopped += OnDirectorStopped;
    }

    private void OnDisable()
    {
        if (_director != null) _director.stopped -= OnDirectorStopped;
    }

    private void OnDirectorStopped(PlayableDirector d)
    {
        ShutdownProjectors();
    }

    // Coupe tout le rig projecteurs et empeche tout re-allumage / mouvement.
    public void ShutdownProjectors()
    {
        var root = ResolveRoot();

        // 1) Desactive definitivement les mouvements automatiques : sinon
        //    LyreMovement continue de faire tourner pan/tilt via Time.time.
        var moves = root != null
            ? root.GetComponentsInChildren<LyreMovement>(true)
            : Object.FindObjectsByType<LyreMovement>(FindObjectsSortMode.None);
        foreach (var m in moves)
            if (m != null) m.enabled = false;

        // 2) Eteint chaque lyre (dimmer 0, couleur noire, pas de strobe/pan/tilt
        //    parasite). L'Update() du controller continuera d'emettre 0 -> le
        //    materiel reste eteint, comme le panneau LED.
        var lyres = root != null
            ? root.GetComponentsInChildren<LyreController>(true)
            : Object.FindObjectsByType<LyreController>(FindObjectsSortMode.None);
        foreach (var l in lyres)
        {
            if (l == null) continue;
            l.dimmer = 0f;
            l.white = 0f;
            l.strobe = 0f;
            l.speed = 0f;
            l.macro = 0f;
            l.color = Color.black;
        }

        // 3) Eteint le(s) projecteur(s) statique(s).
        var projectors = root != null
            ? root.GetComponentsInChildren<ProjectorController>(true)
            : Object.FindObjectsByType<ProjectorController>(FindObjectsSortMode.None);
        foreach (var p in projectors)
        {
            if (p == null) continue;
            p.dimmer = 0f;
            p.white = 0f;
            p.color = Color.black;
            p.isOn = false;
        }
    }

    private GameObject ResolveRoot()
    {
        if (projectorsRoot != null) return projectorsRoot;
        projectorsRoot = GameObject.Find("Projecteurs");
        return projectorsRoot;
    }
}
