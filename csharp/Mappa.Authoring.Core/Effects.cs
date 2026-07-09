using System;
using System.Collections.Generic;
using Mappa;

namespace Mappa.Authoring.Core
{
    public sealed class EffectContext
    {
        public State State = null!;
        public EntityLayout Layout = null!;
        public double LocalTime;
        public double Duration;
        public float Intensity = 1f;
        public IReadOnlyList<int>? Targets;

        public double Progress => Duration > 0 ? Math.Min(1.0, Math.Max(0.0, LocalTime / Duration)) : 0.0;

        public IReadOnlyList<int> EffectiveTargets => Targets ?? Layout.Ids;
    }

    public interface IEffect
    {
        string Kind { get; }
        void Render(EffectContext ctx);
    }

    public sealed class SolidColorEffect : IEffect
    {
        public string Kind => "solid";
        public ColorF Color = ColorF.White;

        public void Render(EffectContext ctx)
        {
            var c = ctx.Intensity >= 1f ? Color : Color.Scale(ctx.Intensity);
            var col = c.ToColor();
            var st = ctx.State;
            foreach (int id in ctx.EffectiveTargets)
                st.SetColor(id, col);
        }
    }

    public sealed class GradientSweepEffect : IEffect
    {
        public string Kind => "gradient";
        public ColorF ColorA = new ColorF(255, 0, 0);
        public ColorF ColorB = new ColorF(0, 0, 255);
        public float Speed = 0.25f;
        public float Cycles = 1f;

        public void Render(EffectContext ctx)
        {
            float phase = (float)(ctx.LocalTime * Speed);
            var st = ctx.State;
            var layout = ctx.Layout;
            foreach (int id in ctx.EffectiveTargets)
            {
                if (!layout.TryGet(id, out var p)) continue;
                float f = p.Nx * Cycles - phase;
                f -= (float)Math.Floor(f);
                float tri = f < 0.5f ? f * 2f : (1f - f) * 2f;
                var c = ColorF.Lerp(ColorA, ColorB, tri).Scale(ctx.Intensity);
                st.SetColor(id, c.ToColor());
            }
        }
    }

    public sealed class PlasmaEffect : IEffect
    {
        public string Kind => "plasma";
        public float Scale = 4f;
        public float Speed = 0.5f;
        public float Saturation = 1f;
        public float Value = 1f;

        public void Render(EffectContext ctx)
        {
            float t = (float)ctx.LocalTime * Speed;
            var st = ctx.State;
            var layout = ctx.Layout;
            foreach (int id in ctx.EffectiveTargets)
            {
                if (!layout.TryGet(id, out var p)) continue;
                float x = p.Nx * Scale;
                float y = p.Ny * Scale;
                float v = (float)(
                    Math.Sin(x + t) +
                    Math.Sin(y - t) +
                    Math.Sin((x + y) * 0.5f + t) +
                    Math.Sin(Math.Sqrt(x * x + y * y) + t));
                float hue = (v + 4f) / 8f;
                var c = ColorF.FromHsv(hue, Saturation, Value).Scale(ctx.Intensity);
                st.SetColor(id, c.ToColor());
            }
        }
    }

    public sealed class StrobeEffect : IEffect
    {
        public string Kind => "strobe";
        public ColorF Color = ColorF.White;
        public float Frequency = 8f;
        public float DutyCycle = 0.5f;

        public void Render(EffectContext ctx)
        {
            float phase = (float)(ctx.LocalTime * Frequency);
            bool on = (phase - (float)Math.Floor(phase)) < DutyCycle;
            var col = (on ? Color.Scale(ctx.Intensity) : ColorF.Black).ToColor();
            var st = ctx.State;
            foreach (int id in ctx.EffectiveTargets)
                st.SetColor(id, col);
        }
    }

    public sealed class ImageEffect : IEffect
    {
        public string Kind => "image";
        public string Path = "";
        public bool FlipY = true;

        private PpmImage? _image;
        private string? _loadedPath;

        public void Render(EffectContext ctx)
        {
            if (_image == null || _loadedPath != Path)
            {
                _image = string.IsNullOrEmpty(Path) ? null : PpmImage.Load(Path);
                _loadedPath = Path;
            }
            if (_image == null) return;

            var st = ctx.State;
            var layout = ctx.Layout;
            float k = ctx.Intensity;
            foreach (int id in ctx.EffectiveTargets)
            {
                if (!layout.TryGet(id, out var p)) continue;
                float v = FlipY ? 1f - p.Ny : p.Ny;
                var (r, g, b) = _image.SampleNormalized(p.Nx, v);
                if (k >= 1f)
                    st.Set(id, r, g, b);
                else
                    st.Set(id, (int)(r * k), (int)(g * k), (int)(b * k));
            }
        }
    }
}
