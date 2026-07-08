namespace Mappa.Authoring.App;

public interface IPlaybackClock
{
    double CurrentTime { get; }
    double Duration { get; set; }
    bool Playing { get; }
    bool Loop { get; set; }

    void Play();
    void Pause();
    void TogglePlay();
    void Stop();
    void Seek(double t);
}
