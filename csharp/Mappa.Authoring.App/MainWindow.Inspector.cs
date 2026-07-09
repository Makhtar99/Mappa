using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mappa.Authoring.Core;

namespace Mappa.Authoring.App;

public partial class MainWindow
{
    private TextBox? _startBox;
    private TextBox? _durBox;
    private bool _suppress;

    private void OnAddTrack()
    {
        _show.Tracks.Add(new Track { Name = $"piste {_show.Tracks.Count + 1}" });
        Timeline.InvalidateVisual();
    }

    private void OnAddClip()
    {
        if (_show.Tracks.Count == 0) _show.Tracks.Add(new Track { Name = "piste 1" });
        var track = Selected() is { } sel ? TrackOf(sel) ?? _show.Tracks[0] : _show.Tracks[0];

        var clip = new Clip
        {
            Name = "clip",
            Start = _clock.CurrentTime,
            Duration = 5,
            Effect = new SolidColorEffect { Color = new ColorF(255, 255, 255) },
        };
        track.Clips.Add(clip);
        Timeline.Select(clip);
    }

    private void OnDeleteClip()
    {
        if (Selected() is not { } clip) return;
        TrackOf(clip)?.Clips.Remove(clip);
        Timeline.Select(null);
    }

    private Clip? Selected() => Timeline.Selected;

    private Track? TrackOf(Clip clip)
    {
        foreach (var t in _show.Tracks)
            if (t.Clips.Contains(clip)) return t;
        return null;
    }

    private void OnClipEdited()
    {
        if (Selected() is not { } clip) return;
        _suppress = true;
        if (_startBox != null) _startBox.Text = Fmt(clip.Start);
        if (_durBox != null) _durBox.Text = Fmt(clip.Duration);
        _suppress = false;
    }

    private void BuildInspector(Clip? clip)
    {
        Inspector.Children.Clear();
        _startBox = _durBox = null;

        if (clip == null)
        {
            Inspector.Children.Add(new TextBlock
            {
                Text = "Aucun clip sélectionné.\nClique un clip dans la timeline, ou « + Clip ».",
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        Inspector.Children.Add(Header("Clip"));
        Inspector.Children.Add(LabeledText("Nom", clip.Name, v => clip.Name = v));
        _startBox = LabeledNum("Début (s)", clip.Start, v => { clip.Start = Math.Max(0, v); Timeline.InvalidateVisual(); });
        _durBox = LabeledNum("Durée (s)", clip.Duration, v => { clip.Duration = Math.Max(0.1, v); Timeline.InvalidateVisual(); });
        Inspector.Children.Add(WrapLabel("Début (s)", _startBox));
        Inspector.Children.Add(WrapLabel("Durée (s)", _durBox));
        Inspector.Children.Add(LabeledNum2("Fade in (s)", clip.FadeIn, v => clip.FadeIn = Math.Max(0, v)));
        Inspector.Children.Add(LabeledNum2("Fade out (s)", clip.FadeOut, v => clip.FadeOut = Math.Max(0, v)));
        Inspector.Children.Add(LabeledText("Cibles (ex: 100-358,400)", TargetsToText(clip.Targets), v => clip.Targets = ParseTargets(v)));

        Inspector.Children.Add(Header("Effet"));
        var combo = new ComboBox
        {
            ItemsSource = new[] { "solid", "gradient", "plasma", "strobe", "image" },
            SelectedItem = clip.Effect.Kind,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string k && k != clip.Effect.Kind)
            {
                clip.Effect = CreateEffect(k);
                Timeline.InvalidateVisual();
                BuildInspector(clip);
            }
        };
        Inspector.Children.Add(combo);

        BuildEffectParams(clip);
    }

    private void BuildEffectParams(Clip clip)
    {
        switch (clip.Effect)
        {
            case SolidColorEffect s:
                Inspector.Children.Add(LabeledColor("Couleur", s.Color, c => s.Color = c));
                break;
            case GradientSweepEffect g:
                Inspector.Children.Add(LabeledColor("Couleur A", g.ColorA, c => g.ColorA = c));
                Inspector.Children.Add(LabeledColor("Couleur B", g.ColorB, c => g.ColorB = c));
                Inspector.Children.Add(LabeledNum2("Vitesse", g.Speed, v => g.Speed = (float)v));
                Inspector.Children.Add(LabeledNum2("Cycles", g.Cycles, v => g.Cycles = (float)v));
                break;
            case PlasmaEffect p:
                Inspector.Children.Add(LabeledNum2("Échelle", p.Scale, v => p.Scale = (float)v));
                Inspector.Children.Add(LabeledNum2("Vitesse", p.Speed, v => p.Speed = (float)v));
                Inspector.Children.Add(LabeledNum2("Saturation", p.Saturation, v => p.Saturation = (float)v));
                Inspector.Children.Add(LabeledNum2("Valeur", p.Value, v => p.Value = (float)v));
                break;
            case StrobeEffect st:
                Inspector.Children.Add(LabeledColor("Couleur", st.Color, c => st.Color = c));
                Inspector.Children.Add(LabeledNum2("Fréquence (Hz)", st.Frequency, v => st.Frequency = (float)v));
                Inspector.Children.Add(LabeledNum2("Rapport cyclique", st.DutyCycle, v => st.DutyCycle = (float)v));
                break;
            case ImageEffect im:
                Inspector.Children.Add(LabeledText("Chemin image (PPM)", im.Path, v => im.Path = v));
                var flip = new CheckBox { Content = "Retourner Y", IsChecked = im.FlipY };
                flip.IsCheckedChanged += (_, _) => im.FlipY = flip.IsChecked == true;
                Inspector.Children.Add(flip);
                break;
        }
    }

    private static Mappa.Authoring.Core.IEffect CreateEffect(string kind) => kind switch
    {
        "gradient" => new GradientSweepEffect(),
        "plasma" => new PlasmaEffect(),
        "strobe" => new StrobeEffect(),
        "image" => new ImageEffect(),
        _ => new SolidColorEffect(),
    };

    // ---- widget helpers ----

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Foreground = Brushes.White,
        Margin = new Avalonia.Thickness(0, 8, 0, 0),
    };

    private Control LabeledText(string label, string value, Action<string> setter)
    {
        var tb = new TextBox { Text = value };
        tb.TextChanged += (_, _) => { if (!_suppress) setter(tb.Text ?? ""); };
        return WrapLabel(label, tb);
    }

    private TextBox LabeledNum(string label, double value, Action<double> setter)
    {
        var tb = new TextBox { Text = Fmt(value) };
        tb.TextChanged += (_, _) => { if (!_suppress && TryNum(tb.Text, out double v)) setter(v); };
        return tb;
    }

    private Control LabeledNum2(string label, double value, Action<double> setter)
        => WrapLabel(label, LabeledNum(label, value, setter));

    private Control LabeledColor(string label, ColorF value, Action<ColorF> setter)
    {
        var tb = new TextBox { Text = ColorToText(value) };
        tb.TextChanged += (_, _) => { if (!_suppress && TryColor(tb.Text, out var c)) setter(c); };
        return WrapLabel(label + " (r,g,b,w)", tb);
    }

    private static Control WrapLabel(string label, Control field)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.DarkGray, FontSize = 11 });
        panel.Children.Add(field);
        return panel;
    }

    // ---- parsing ----

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryNum(string? s, out double v)
        => double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static string ColorToText(ColorF c)
        => $"{(int)c.R},{(int)c.G},{(int)c.B},{(int)c.W}";

    private static bool TryColor(string? s, out ColorF c)
    {
        c = ColorF.Black;
        var parts = (s ?? "").Split(',');
        if (parts.Length < 3) return false;
        float[] v = new float[4];
        for (int i = 0; i < 3; i++)
            if (!TryNum(parts[i], out double d)) return false;
            else v[i] = (float)d;
        if (parts.Length >= 4 && TryNum(parts[3], out double w)) v[3] = (float)w;
        c = new ColorF(v[0], v[1], v[2], v[3]);
        return true;
    }

    private static string TargetsToText(int[]? targets)
    {
        if (targets == null || targets.Length == 0) return "all";
        var ranges = new List<string>();
        int start = targets[0], prev = targets[0];
        for (int i = 1; i < targets.Length; i++)
        {
            if (targets[i] == prev + 1) { prev = targets[i]; continue; }
            ranges.Add(start == prev ? $"{start}" : $"{start}-{prev}");
            start = prev = targets[i];
        }
        ranges.Add(start == prev ? $"{start}" : $"{start}-{prev}");
        return string.Join(",", ranges);
    }

    private static int[]? ParseTargets(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0 || s.Equals("all", StringComparison.OrdinalIgnoreCase)) return null;
        var ids = new List<int>();
        foreach (var token in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim();
            int dash = t.IndexOf('-');
            if (dash > 0)
            {
                if (int.TryParse(t[..dash], out int a) && int.TryParse(t[(dash + 1)..], out int b))
                    for (int id = Math.Min(a, b); id <= Math.Max(a, b); id++) ids.Add(id);
            }
            else if (int.TryParse(t, out int single))
            {
                ids.Add(single);
            }
        }
        return ids.Count > 0 ? ids.Distinct().ToArray() : null;
    }
}
