using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Mappa.Authoring.Core;

namespace Mappa.Authoring.App;

public sealed class TimelineView : Control
{
    private Show? _show;
    private double _time;

    public event Action<double>? Seek;

    private const double HeaderHeight = 18;
    private const double LaneGap = 4;

    private static Avalonia.Media.Color Rgb(byte r, byte g, byte b) => Avalonia.Media.Color.FromRgb(r, g, b);

    private static readonly Dictionary<string, Avalonia.Media.Color> KindColors = new()
    {
        ["solid"] = Rgb(90, 140, 220),
        ["gradient"] = Rgb(220, 90, 140),
        ["plasma"] = Rgb(150, 90, 220),
        ["strobe"] = Rgb(220, 200, 90),
        ["image"] = Rgb(90, 200, 150),
    };

    public void SetShow(Show show)
    {
        _show = show;
        InvalidateVisual();
    }

    public void SetTime(double t)
    {
        _time = t;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_show == null || _show.Duration <= 0) return;
        double x = e.GetPosition(this).X;
        double t = Math.Clamp(x / Bounds.Width, 0, 1) * _show.Duration;
        Seek?.Invoke(t);
    }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        context.FillRectangle(new SolidColorBrush(Rgb(28, 28, 32)), new Rect(b.Size));
        if (_show == null || _show.Duration <= 0) return;

        double width = b.Width;
        double dur = _show.Duration;

        DrawGrid(context, width, b.Height, dur);

        int trackCount = Math.Max(1, _show.Tracks.Count);
        double laneArea = b.Height - HeaderHeight;
        double laneHeight = (laneArea - LaneGap * (trackCount + 1)) / trackCount;

        for (int ti = 0; ti < _show.Tracks.Count; ti++)
        {
            var track = _show.Tracks[ti];
            double y = HeaderHeight + LaneGap + ti * (laneHeight + LaneGap);

            context.FillRectangle(new SolidColorBrush(Rgb(38, 38, 44)),
                new Rect(0, y, width, laneHeight), 3);

            foreach (var clip in track.Clips)
            {
                double x1 = clip.Start / dur * width;
                double x2 = clip.End / dur * width;
                var rect = new Rect(x1, y + 2, Math.Max(2, x2 - x1), laneHeight - 4);
                var color = KindColors.TryGetValue(clip.Effect.Kind, out var c) ? c : Colors.Gray;
                var brush = new SolidColorBrush(color, track.Enabled ? 0.9 : 0.35);
                context.DrawRectangle(brush, null, rect, 4, 4);

                var label = $"{clip.Name} · {clip.Effect.Kind}";
                var ft = new FormattedText(label, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
                if (ft.Width < rect.Width - 8)
                    context.DrawText(ft, new Point(rect.X + 4, rect.Y + (rect.Height - ft.Height) / 2));
            }
        }

        double px = _time / dur * width;
        var pen = new Pen(Brushes.OrangeRed, 1.5);
        context.DrawLine(pen, new Point(px, 0), new Point(px, b.Height));

        var tlabel = new FormattedText($"{_time:F2}s / {dur:F0}s", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.OrangeRed);
        context.DrawText(tlabel, new Point(Math.Min(px + 4, width - tlabel.Width - 2), 2));
    }

    private static void DrawGrid(DrawingContext context, double width, double height, double dur)
    {
        var pen = new Pen(new SolidColorBrush(Rgb(55, 55, 62)), 1);
        double step = dur <= 20 ? 1 : (dur <= 60 ? 5 : 10);
        for (double s = 0; s <= dur; s += step)
        {
            double x = s / dur * width;
            context.DrawLine(pen, new Point(x, HeaderHeight), new Point(x, height));
            var ft = new FormattedText($"{s:F0}", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 9,
                new SolidColorBrush(Rgb(140, 140, 150)));
            context.DrawText(ft, new Point(x + 2, 3));
        }
    }
}
