using System.Net;
using System.Net.Sockets;

namespace LedNetwork.Core.DesignProtocol;

/// <summary>
/// Récepteur UDP du protocole personnalisé : écoute les messages « état souhaité »
/// envoyés par l'outil de conception artistique. C'est l'entrée de l'outil de routage.
/// </summary>
public sealed class DesignStateReceiver : IDisposable
{
    public const int DefaultPort = 7000;

    private readonly UdpClient _udp;
    private CancellationTokenSource? _cts;

    /// <summary>Déclenché à chaque message d'état valide reçu.</summary>
    public event Action<DesignStateMessage, IPEndPoint>? StateReceived;

    public DesignStateReceiver(int port = DefaultPort, IPAddress? bindAddress = null)
    {
        _udp = new UdpClient(new IPEndPoint(bindAddress ?? IPAddress.Any, port));
    }

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
                if (DesignStateMessage.TryParse(result.Buffer, out var msg) && msg is not null)
                    StateReceived?.Invoke(msg, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp.Dispose();
    }
}
