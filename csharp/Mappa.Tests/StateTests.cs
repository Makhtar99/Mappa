using System.Linq;
using Mappa;
using Xunit;

namespace Mappa.Tests
{
    public class StateTests
    {
        [Fact]
        public void DefaultIsBlack()
        {
            var s = new State(new[] { 1, 2, 3 });
            var c = s.Get(1);
            Assert.Equal(0, c.R);
            Assert.Equal(0, c.G);
            Assert.Equal(0, c.B);
            Assert.Equal(0, c.W);
        }

        [Fact]
        public void SetAndGet()
        {
            var s = new State(new[] { 10, 20 });
            s.Set(10, 255, 128, 0, 5);
            var c = s.Get(10);
            Assert.Equal((byte)255, c.R);
            Assert.Equal((byte)128, c.G);
            Assert.Equal((byte)0, c.B);
            Assert.Equal((byte)5, c.W);
            Assert.Equal(0, s.Get(20).R);
        }

        [Fact]
        public void ClampsValues()
        {
            var s = new State(new[] { 1 });
            s.Set(1, 300, -10, 256, 1000);
            var c = s.Get(1);
            Assert.Equal((byte)255, c.R);
            Assert.Equal((byte)0, c.G);
            Assert.Equal((byte)255, c.B);
            Assert.Equal((byte)255, c.W);
        }

        [Fact]
        public void UnknownIdIgnored()
        {
            var s = new State(new[] { 1 });
            s.Set(999, 255, 255, 255);
            Assert.False(s.Contains(999));
        }

        [Fact]
        public void FillAndClear()
        {
            var s = new State(new[] { 1, 2 });
            s.Fill(1, 2, 3, 4);
            Assert.Equal((byte)1, s.Get(1).R);
            Assert.Equal((byte)4, s.Get(2).W);
            s.Clear();
            Assert.Equal((byte)0, s.Get(1).R);
        }

        [Fact]
        public void BufferLayout()
        {
            var s = new State(new[] { 5, 6 });
            s.Set(6, 9, 8, 7, 6);
            Assert.Equal(new byte[] { 9, 8, 7, 6 }, s.Buffer.Skip(4).Take(4).ToArray());
        }
    }
}
