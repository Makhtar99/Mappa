using System.IO;
using Mappa;
using Mappa.Authoring.Core;
using Xunit;

namespace Mappa.Authoring.Tests
{
    public class ShowTests
    {
        private static EntityLayout SmallGrid()
        {
            var config = Wall.BuildWallConfig(columns: 4, ledsPerColumn: 4);
            return EntityLayout.FromGrid(config);
        }

        [Fact]
        public void ShowPlayer_SolidClip_FillsTargets()
        {
            var layout = SmallGrid();
            var show = new Show { Duration = 10 };
            var track = new Track();
            track.Clips.Add(new Clip
            {
                Start = 0,
                Duration = 10,
                Effect = new SolidColorEffect { Color = new ColorF(255, 0, 0) },
            });
            show.Tracks.Add(track);

            var player = new ShowPlayer(show, layout);
            var state = new State(layout.Ids);
            player.RenderAt(5.0, state);

            foreach (int id in layout.Ids)
            {
                var c = state.Get(id);
                Assert.Equal(255, c.R);
                Assert.Equal(0, c.G);
            }
        }

        [Fact]
        public void Clip_FadeIn_ScalesIntensity()
        {
            var clip = new Clip { Start = 0, Duration = 10, FadeIn = 2 };
            Assert.Equal(0f, clip.IntensityAt(0), 3);
            Assert.Equal(0.5f, clip.IntensityAt(1), 3);
            Assert.Equal(1f, clip.IntensityAt(2), 3);
            Assert.Equal(1f, clip.IntensityAt(5), 3);
        }

        [Fact]
        public void ShowPlayer_InactiveClip_LeavesBlack()
        {
            var layout = SmallGrid();
            var show = new Show { Duration = 10 };
            var track = new Track();
            track.Clips.Add(new Clip
            {
                Start = 5,
                Duration = 2,
                Effect = new SolidColorEffect { Color = new ColorF(255, 255, 255) },
            });
            show.Tracks.Add(track);

            var player = new ShowPlayer(show, layout);
            var state = new State(layout.Ids);
            player.RenderAt(0.0, state);

            var c = state.Get(layout.Ids[0]);
            Assert.Equal(0, c.R);
            Assert.Equal(0, c.G);
            Assert.Equal(0, c.B);
        }

        [Fact]
        public void ShowFile_SaveLoad_RoundTrips()
        {
            var show = new Show { Name = "demo", Fps = 40, Duration = 30, ConfigPath = "wall.json" };
            var track = new Track { Name = "bg" };
            track.Clips.Add(new Clip
            {
                Name = "sweep",
                Start = 1,
                Duration = 20,
                FadeIn = 2,
                FadeOut = 3,
                Effect = new GradientSweepEffect { ColorA = new ColorF(255, 0, 0), ColorB = new ColorF(0, 0, 255), Speed = 0.5f },
                Targets = new[] { 100, 101, 102 },
            });
            show.Tracks.Add(track);

            string path = Path.Combine(Path.GetTempPath(), "mappa_show_test.json");
            ShowFile.Save(show, path);
            var loaded = ShowFile.Load(path);

            Assert.Equal("demo", loaded.Name);
            Assert.Equal(40, loaded.Fps);
            Assert.Single(loaded.Tracks);
            var clip = loaded.Tracks[0].Clips[0];
            Assert.Equal("sweep", clip.Name);
            Assert.Equal(20, clip.Duration);
            Assert.Equal(2, clip.FadeIn);
            var g = Assert.IsType<GradientSweepEffect>(clip.Effect);
            Assert.Equal(0.5f, g.Speed, 3);
            Assert.Equal(255, g.ColorA.R, 3);
            Assert.NotNull(clip.Targets);
            Assert.Equal(3, clip.Targets!.Length);
        }
    }
}
