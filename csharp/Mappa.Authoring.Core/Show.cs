using System;
using System.Collections.Generic;
using Mappa;

namespace Mappa.Authoring.Core
{
    public sealed class Clip
    {
        public string Name = "clip";
        public IEffect Effect = new SolidColorEffect();
        public double Start;
        public double Duration;
        public double FadeIn;
        public double FadeOut;
        public int[]? Targets;

        public double End => Start + Duration;

        public bool IsActiveAt(double t) => t >= Start && t < End;

        public float IntensityAt(double localTime)
        {
            float k = 1f;
            if (FadeIn > 0 && localTime < FadeIn)
                k = (float)(localTime / FadeIn);
            if (FadeOut > 0 && localTime > Duration - FadeOut)
            {
                float kOut = (float)((Duration - localTime) / FadeOut);
                k = Math.Min(k, kOut);
            }
            return k < 0 ? 0 : (k > 1 ? 1 : k);
        }
    }

    public sealed class Track
    {
        public string Name = "track";
        public bool Enabled = true;
        public List<Clip> Clips { get; } = new List<Clip>();
    }

    public sealed class Show
    {
        public string Name = "untitled-show";
        public string ConfigPath = "";
        public string AudioPath = "";
        public double Fps = 40.0;
        public double Duration = 60.0;
        public List<Track> Tracks { get; } = new List<Track>();
    }

    public sealed class ShowPlayer
    {
        private readonly Show _show;
        private readonly EntityLayout _layout;
        private readonly EffectContext _ctx = new EffectContext();

        public ShowPlayer(Show show, EntityLayout layout)
        {
            _show = show;
            _layout = layout;
        }

        public void RenderAt(double t, State state)
        {
            state.Clear();
            _ctx.State = state;
            _ctx.Layout = _layout;

            foreach (var track in _show.Tracks)
            {
                if (!track.Enabled) continue;
                foreach (var clip in track.Clips)
                {
                    if (!clip.IsActiveAt(t)) continue;
                    double local = t - clip.Start;
                    _ctx.LocalTime = local;
                    _ctx.Duration = clip.Duration;
                    _ctx.Intensity = clip.IntensityAt(local);
                    _ctx.Targets = clip.Targets;
                    clip.Effect.Render(_ctx);
                }
            }
            state.MarkUpdated();
        }
    }
}
