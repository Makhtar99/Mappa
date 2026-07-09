using System;
using Mappa;

namespace Mappa.Ui
{
    public sealed class EhubFaker : IFaker, IDisposable
    {
        private readonly EhubReceiver _rx;

        public EhubFaker(int port) => _rx = new EhubReceiver(port);

        public long PacketsReceived => _rx.PacketsReceived;

        public void Fill(State state) => _rx.Fill(state);

        public void Dispose() => _rx.Dispose();
    }
}
