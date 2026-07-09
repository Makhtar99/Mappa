using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mappa;
using Mappa.Authoring.Core;

namespace Mappa.Authoring.App;

public partial class MainWindow : Window
{
    private readonly Transport _transport = new();
    private IPlaybackClock _clock = null!;
    private AudioPlayer? _audio;
    private Config _config = null!;
    private EntityLayout _layout = null!;
    private Show _show = null!;
    private State _previewState = null!;
    private ShowPlayer? _player;

    private DispatcherTimer _timer = null!;
    private EhubSender? _sender;
    private ShowRunner? _runner;

    private readonly Stopwatch _fpsSw = Stopwatch.StartNew();
    private int _frames;
    private double _fps;

    public MainWindow()
    {
        InitializeComponent();

        _clock = _transport;

        ApplyConfig(Mappa.Wall.BuildWallConfig());
        ApplyShow(DemoShows.BuildDemo());

        PlayButton.Click += (_, _) => TogglePlay();
        StopButton.Click += (_, _) => { _clock.Stop(); UpdatePlayButton(); };
        LoadConfigButton.Click += OnLoadConfig;
        LoadShowButton.Click += OnLoadShow;
        SaveShowButton.Click += OnSaveShow;
        LoadAudioButton.Click += OnLoadAudio;
        AddTrackButton.Click += (_, _) => OnAddTrack();
        AddClipButton.Click += (_, _) => OnAddClip();
        DeleteClipButton.Click += (_, _) => OnDeleteClip();
        EmitCheck.IsCheckedChanged += OnEmitToggle;
        Timeline.Seek += t => _clock.Seek(t);
        Timeline.SelectionChanged += BuildInspector;
        Timeline.ClipEdited += OnClipEdited;
        BuildInspector(null);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();

        Closed += (_, _) => { _timer.Stop(); StopEmit(); _audio?.Dispose(); };
    }

    private void ApplyConfig(Config c)
    {
        _config = c;
        _layout = EntityLayout.FromGrid(c);
        _previewState = new State(_layout.Ids);
        Wall.SetLayout(_layout);
        RebuildPlayer();
        RestartRunnerIfEmitting();
    }

    private void ApplyShow(Show s)
    {
        _show = s;
        _transport.Duration = s.Duration > 0 ? s.Duration : 60;
        Timeline.SetShow(s);
        RebuildPlayer();
        RestartRunnerIfEmitting();
    }

    private void RebuildPlayer()
    {
        if (_layout != null && _show != null)
            _player = new ShowPlayer(_show, _layout);
    }

    private void TogglePlay()
    {
        _clock.TogglePlay();
        UpdatePlayButton();
    }

    private void UpdatePlayButton()
    {
        PlayButton.Content = _clock.Playing ? "⏸ Pause" : "▶ Play";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double t = _clock.CurrentTime;
        _player?.RenderAt(t, _previewState);
        Wall.UpdateFrom(_previewState);
        Timeline.SetTime(t);

        _frames++;
        if (_fpsSw.Elapsed.TotalSeconds >= 0.5)
        {
            _fps = _frames / _fpsSw.Elapsed.TotalSeconds;
            _frames = 0;
            _fpsSw.Restart();
        }

        string emit = _runner != null ? $" · eHuB {_runner.FramesSent} f" : "";
        StatusText.Text = $"{_layout.Ids.Count} entités · preview {_fps:F0} fps{emit}";
    }

    private void OnEmitToggle(object? sender, RoutedEventArgs e)
    {
        if (EmitCheck.IsChecked == true) StartEmit();
        else StopEmit();
    }

    private void StartEmit()
    {
        StopEmit();
        try
        {
            string host = string.IsNullOrWhiteSpace(HostBox.Text) ? "127.0.0.1" : HostBox.Text!.Trim();
            int port = int.TryParse(PortBox.Text, out var p) ? p : Ehub.DefaultUdpPort;
            _sender = new EhubSender(host, port);
            _runner = new ShowRunner(_show, _layout, _sender) { TimeSource = () => _clock.CurrentTime };
            _runner.Start();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Erreur émission : {ex.Message}";
            StopEmit();
            EmitCheck.IsChecked = false;
        }
    }

    private void StopEmit()
    {
        _runner?.Dispose();
        _runner = null;
        _sender?.Dispose();
        _sender = null;
    }

    private void RestartRunnerIfEmitting()
    {
        if (_runner != null) StartEmit();
    }

    private async void OnLoadConfig(object? sender, RoutedEventArgs e)
    {
        var file = await PickOpen("Config d'installation (JSON)", JsonType);
        if (file == null) return;
        try { ApplyConfig(Persistence.LoadConfig(file)); }
        catch (Exception ex) { StatusText.Text = $"Config invalide : {ex.Message}"; }
    }

    private async void OnLoadShow(object? sender, RoutedEventArgs e)
    {
        var file = await PickOpen("Show (JSON)", JsonType);
        if (file == null) return;
        try { ApplyShow(ShowFile.Load(file)); }
        catch (Exception ex) { StatusText.Text = $"Show invalide : {ex.Message}"; }
    }

    private async void OnSaveShow(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer le show",
            SuggestedFileName = "show.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { JsonType },
        });
        if (file == null) return;
        try { ShowFile.Save(_show, file.Path.LocalPath); StatusText.Text = "Show enregistré."; }
        catch (Exception ex) { StatusText.Text = $"Échec sauvegarde : {ex.Message}"; }
    }

    private async void OnLoadAudio(object? sender, RoutedEventArgs e)
    {
        var file = await PickOpen("Fichier audio", AudioType);
        if (file == null) return;
        try
        {
            _audio ??= new AudioPlayer();
            await _audio.LoadAsync(file);
            _clock = _audio;
            _show.AudioPath = file;
            double d = _audio.Duration;
            if (d > 0)
            {
                _show.Duration = d;
                _transport.Duration = d;
            }
            RestartRunnerIfEmitting();
            UpdatePlayButton();
            StatusText.Text = $"Audio : {System.IO.Path.GetFileName(file)} ({_show.Duration:F0}s)";
        }
        catch (Exception ex)
        {
            _clock = _transport;
            StatusText.Text = $"Audio impossible : {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task<string?> PickOpen(string title, FilePickerFileType type)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { type },
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private static readonly FilePickerFileType JsonType = new("JSON") { Patterns = new[] { "*.json" } };
    private static readonly FilePickerFileType AudioType = new("Audio")
    {
        Patterns = new[] { "*.mp3", "*.wav", "*.flac", "*.ogg", "*.m4a", "*.aac" },
    };
}
