using System.Collections.Generic;
using Mappa;
using Mappa.Authoring.Core;
using Xunit;

namespace Mappa.Authoring.Tests
{
    public class EhubTests
    {
        [Fact]
        public void ComputeRanges_MatchesSpiderExample()
        {
            var ids = new List<int>();
            for (int i = 1; i <= 170; i++) ids.Add(i);
            for (int i = 201; i <= 370; i++) ids.Add(i);

            var ranges = Ehub.ComputeRanges(ids);

            Assert.Equal(2, ranges.Count);

            Assert.Equal(0, ranges[0].SextuorStart);
            Assert.Equal(1, ranges[0].EntityStart);
            Assert.Equal(169, ranges[0].SextuorEnd);
            Assert.Equal(170, ranges[0].EntityEnd);

            Assert.Equal(170, ranges[1].SextuorStart);
            Assert.Equal(201, ranges[1].EntityStart);
            Assert.Equal(339, ranges[1].SextuorEnd);
            Assert.Equal(370, ranges[1].EntityEnd);
        }

        [Fact]
        public void EncodeUpdate_RoundTrips()
        {
            var ids = new List<int> { 5, 6, 7 };
            var state = new State(ids);
            state.Set(5, 255, 0, 0);
            state.Set(6, 0, 255, 0);
            state.Set(7, 0, 0, 255, 128);

            byte[] msg = Ehub.EncodeUpdate(3, ids, state);
            var decoded = Ehub.Decode(msg);

            Assert.Equal(Ehub.TypeUpdate, decoded.Type);
            Assert.Equal(3, decoded.Universe);
            Assert.Equal(3, decoded.Count);
            Assert.Equal(3 * Ehub.SextuorSize, decoded.Payload.Length);

            Assert.Equal(5, Ehub.ReadU16(decoded.Payload, 0));
            Assert.Equal(255, decoded.Payload[2]);
            Assert.Equal(0, decoded.Payload[3]);

            Assert.Equal(7, Ehub.ReadU16(decoded.Payload, 12));
            Assert.Equal(255, decoded.Payload[16]);
            Assert.Equal(128, decoded.Payload[17]);
        }

        [Fact]
        public void EncodeConfig_RoundTrips()
        {
            var ids = new List<int>();
            for (int i = 1; i <= 170; i++) ids.Add(i);
            for (int i = 201; i <= 370; i++) ids.Add(i);
            var ranges = Ehub.ComputeRanges(ids);

            byte[] msg = Ehub.EncodeConfig(7, ranges);
            var decoded = Ehub.Decode(msg);
            var back = Ehub.DecodeRanges(decoded);

            Assert.Equal(Ehub.TypeConfig, decoded.Type);
            Assert.Equal(7, decoded.Universe);
            Assert.Equal(ranges.Count, back.Count);
            Assert.Equal(ranges[1].EntityStart, back[1].EntityStart);
            Assert.Equal(ranges[1].SextuorStart, back[1].SextuorStart);
        }
    }
}
