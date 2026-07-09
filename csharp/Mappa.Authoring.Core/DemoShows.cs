namespace Mappa.Authoring.Core
{
    public static class DemoShows
    {
        public static Show BuildDemo(string configPath = "")
        {
            var show = new Show
            {
                Name = "demo",
                ConfigPath = configPath,
                Fps = 40,
                Duration = 24,
            };

            var background = new Track { Name = "background" };
            background.Clips.Add(new Clip
            {
                Name = "sweep",
                Start = 0,
                Duration = 12,
                FadeIn = 1,
                FadeOut = 1,
                Effect = new GradientSweepEffect
                {
                    ColorA = new ColorF(255, 0, 80),
                    ColorB = new ColorF(0, 120, 255),
                    Speed = 0.4f,
                    Cycles = 2f,
                },
            });
            background.Clips.Add(new Clip
            {
                Name = "plasma",
                Start = 12,
                Duration = 12,
                FadeIn = 1,
                FadeOut = 1,
                Effect = new PlasmaEffect { Scale = 5f, Speed = 0.8f },
            });
            show.Tracks.Add(background);

            var accents = new Track { Name = "accents" };
            accents.Clips.Add(new Clip
            {
                Name = "strobe",
                Start = 8,
                Duration = 2,
                Effect = new StrobeEffect { Color = ColorF.White, Frequency = 10f, DutyCycle = 0.4f },
            });
            show.Tracks.Add(accents);

            return show;
        }
    }
}
