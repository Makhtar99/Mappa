using System.Diagnostics;

namespace Mappa.Authoring.App;

public sealed class Transport
{
    private readonly Stopwatch _sw = new();
    private double _anchor;

    public bool Playing { get; private set; }
    public double Duration { get; set; } = 60.0;
    public bool Loop { get; set; } = true;

    public double CurrentTime
    {
        get
        {
            double t = Playing ? _anchor + _sw.Elapsed.TotalSeconds : _anchor;
            if (Loop && Duration > 0) t %= Duration;
            return t;
        }
    }

    public void Play()
    {
        if (Playing) return;
        _sw.Restart();
        Playing = true;
    }

    public void Pause()
    {
        if (!Playing) return;
        _anchor = CurrentTime;
        _sw.Reset();
        Playing = false;
    }

    public void TogglePlay()
    {
        if (Playing) Pause(); else Play();
    }

    public void Stop()
    {
        Playing = false;
        _sw.Reset();
        _anchor = 0;
    }

    public void Seek(double t)
    {
        _anchor = t;
        if (Playing) _sw.Restart();
    }
}
