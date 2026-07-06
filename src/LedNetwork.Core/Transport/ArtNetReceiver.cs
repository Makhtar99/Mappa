using System.Net;
using System.Net.Sockets;
using LedNetwork.Core.ArtNet;

namespace LedNetwork.Core.Transport;

/// <summary>
/// Récepteur UDP Art-Net. Écoute le port 6454 et lève un événement à chaque
/// paquet ArtDMX reçu. Utile pour l'outil de surveillance des signaux d'entrée
/// (« Outils de surveillance des signaux d'entrée et de sortie » du schéma).
/// </summary>
public sealed class ArtNetReceiver : IDisposable
{
    private readonly UdpClient _udp;
    private CancellationTokenSource? _cts;

    /// <summary>Déclenché à chaque trame ArtDMX valide reçue.</summary>
    public event Action<ArtDmxPacket, IPEndPoint>? DmxReceived;

    public ArtNetReceiver(IPAddress? bindAddress = null)
    {
        _udp = new UdpClient(new IPEndPoint(bindAddress ?? IPAddress.Any, ArtNetConstants.UdpPort));
    }

    /// <summary>Démarre l'écoute en tâche de fond.</summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_cts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _udp.ReceiveAsync(token);
                if (ArtDmxPacket.TryParse(result.Buffer, out var packet) && packet is not null)
                    DmxReceived?.Invoke(packet, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { /* socket fermé pendant l'arrêt */ break; }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp.Dispose();
    }
}
