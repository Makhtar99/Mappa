using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Mappa;
using Xunit;

namespace Mappa.Tests
{
    public class EhubBridgeTests
    {
        [Fact]
        public void EhubReceiver_ReceivesUpdate_FillsState()
        {
            using var rx = new EhubReceiver(0);
            int port = rx.Port;

            var ids = new List<int> { 100, 101, 102 };
            var src = new State(ids);
            src.Set(100, 255, 0, 0);
            src.Set(101, 0, 255, 0);
            src.Set(102, 0, 0, 255, 64);

            byte[] msg = Ehub.EncodeUpdate(0, ids, src);
            using (var udp = new UdpClient())
                udp.Send(msg, msg.Length, "127.0.0.1", port);

            for (int i = 0; i < 100 && rx.PacketsReceived == 0; i++)
                Thread.Sleep(10);

            Assert.True(rx.PacketsReceived > 0);

            var dst = new State(ids);
            rx.Fill(dst);

            Assert.Equal(255, dst.Get(100).R);
            Assert.Equal(255, dst.Get(101).G);
            Assert.Equal(255, dst.Get(102).B);
            Assert.Equal(64, dst.Get(102).W);
        }
    }
}
