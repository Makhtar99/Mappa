using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mappa.Authoring.App;

public sealed class AudioPlayer : IPlaybackClock, IDisposable
{
    private readonly Stopwatch _sw = new();
    private readonly object _lock = new();
    private double _anchor;
    private double _duration;
    private string? _path;
    private Process? _proc;
    private volatile bool _playing;
    private volatile bool _stopping;

    public bool HasMedia => _path != null;
    public string? Path => _path;

    public Task<bool> LoadAsync(string path)
    {
        StopProcess();
        _path = path;
        _anchor = 0;
        _sw.Reset();
        _playing = false;
        _duration = ProbeDuration(path);
        return Task.FromResult(true);
    }

    public double CurrentTime
    {
        get
        {
            double t = _anchor + (_playing ? _sw.Elapsed.TotalSeconds : 0);
            if (Loop && _duration > 0) t %= _duration;
            return t;
        }
    }

    public double Duration { get => _duration; set { } }
    public bool Playing => _playing;
    public bool Loop { get; set; } = true;

    public void Play()
    {
        if (_path == null) return;
        lock (_lock)
        {
            if (_proc != null && !_proc.HasExited)
            {
                RunKill("-CONT", _proc.Id);
                _sw.Start();
                _playing = true;
                return;
            }
            Launch();
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_proc != null && !_proc.HasExited && _playing)
            {
                RunKill("-STOP", _proc.Id);
                _anchor += _sw.Elapsed.TotalSeconds;
                _sw.Reset();
                _playing = false;
            }
        }
    }

    public void TogglePlay()
    {
        if (_playing) Pause();
        else Play();
    }

    public void Stop()
    {
        StopProcess();
        _anchor = 0;
        _sw.Reset();
        _playing = false;
    }

    public void Seek(double t) { }

    private void Launch()
    {
        _stopping = false;
        var psi = new ProcessStartInfo("/usr/bin/afplay") { UseShellExecute = false };
        psi.ArgumentList.Add(_path!);
        _proc = Process.Start(psi);
        if (_proc != null)
        {
            _proc.EnableRaisingEvents = true;
            _proc.Exited += OnExited;
        }
        _anchor = 0;
        _sw.Restart();
        _playing = true;
    }

    private void OnExited(object? sender, EventArgs e)
    {
        if (_stopping) return;
        double lived = _sw.Elapsed.TotalSeconds;
        if (Loop && _path != null && lived > 0.25)
        {
            lock (_lock) { Launch(); }
        }
        else
        {
            _playing = false;
            _sw.Reset();
            _anchor = 0;
        }
    }

    private void StopProcess()
    {
        lock (_lock)
        {
            _stopping = true;
            if (_proc != null)
            {
                try { if (!_proc.HasExited) _proc.Kill(); } catch { }
                _proc.Dispose();
                _proc = null;
            }
        }
    }

    private static void RunKill(string signal, int pid)
    {
        try
        {
            var psi = new ProcessStartInfo("/bin/kill") { UseShellExecute = false };
            psi.ArgumentList.Add(signal);
            psi.ArgumentList.Add(pid.ToString());
            Process.Start(psi)?.WaitForExit(500);
        }
        catch { }
    }

    private static double ProbeDuration(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/afinfo")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add(path);
            var p = Process.Start(psi);
            if (p == null) return 0;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            var m = Regex.Match(outp, @"estimated duration:\s*([0-9.]+)");
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }
        catch { return 0; }
    }

    public void Dispose() => StopProcess();
}
