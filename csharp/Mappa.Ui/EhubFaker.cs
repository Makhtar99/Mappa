using Mappa;

namespace Mappa.Ui
{
    /// <summary>
    /// Adapte un <see cref="EhubReceiver"/> en source d'animation pour le moteur
    /// de routage. Le recepteur est FOURNI et n'est pas possede : un seul socket
    /// peut ecouter un port UDP donne, et l'onglet Debogage ecoute deja le meme.
    /// C'est donc MainWindow qui detient le recepteur et le partage.
    /// </summary>
    public sealed class EhubFaker : IFaker
    {
        private readonly EhubReceiver _rx;

        public EhubFaker(EhubReceiver receiver) => _rx = receiver;

        public long PacketsReceived => _rx.PacketsReceived;

        public void Fill(State state) => _rx.Fill(state);
    }
}
