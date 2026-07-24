using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Video;

[DefaultExecutionOrder(-20000)]
public sealed class PauseControl : MonoBehaviour
{
    public KeyCode key = KeyCode.Space;
    public static bool Paused { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindFirstObjectByType<PauseControl>() != null) return;
        new GameObject("PauseControl").AddComponent<PauseControl>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(key)) Toggle();
    }

    public void Toggle() => SetPaused(!Paused);

    public void SetPaused(bool p)
    {
        Paused = p;
        Time.timeScale = p ? 0f : 1f;
        AudioListener.pause = p;

        foreach (var vp in FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None))
        {
            if (p) vp.Pause(); else vp.Play();
        }
        foreach (var d in FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None))
        {
            if (p) d.Pause(); else d.Resume();
        }
    }

    private void OnDisable()
    {
        if (Paused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Paused = false;
        }
    }
}
