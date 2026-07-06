using System.Net;
using System.Net.Sockets;
using LedNetwork.Core.ArtNet;

namespace LedNetwork.Core.Transport;

/// <summary>
/// Émetteur UDP Art-Net. Envoie des paquets ArtDMX vers un contrôleur BC216
/// (unicast) ou vers tout le réseau (broadcast).
///
/// Gère automatiquement le numéro de séquence par univers, comme recommandé
/// par la spécification.
/// </summary>
public sealed class ArtNetSender : IDisposable
{
    private readonly UdpClient _udp;
    private readonly Dictionary<ushort, byte> _sequences = new();

    public ArtNetSender()
    {
        _udp = new UdpClient();
        _udp.EnableBroadcast = true;
    }

    /// <summary>Envoie une trame DMX vers un univers, en unicast vers un contrôleur précis.</summary>
    public void Send(IPAddress controllerAddress, ushort portAddress, byte[] dmxData)
        => SendTo(new IPEndPoint(controllerAddress, ArtNetConstants.UdpPort), portAddress, dmxData);

    /// <summary>Envoie une trame DMX en broadcast (diffusion réseau, port 6454).</summary>
    public void Broadcast(ushort portAddress, byte[] dmxData)
        => SendTo(new IPEndPoint(IPAddress.Broadcast, ArtNetConstants.UdpPort), portAddress, dmxData);

    private void SendTo(IPEndPoint endpoint, ushort portAddress, byte[] dmxData)
    {
        var packet = new ArtDmxPacket
        {
            PortAddress = portAddress,
            Sequence = NextSequence(portAddress),
            Data = dmxData
        };

        byte[] bytes = packet.Serialize();
        _udp.Send(bytes, bytes.Length, endpoint);
    }

    private byte NextSequence(ushort portAddress)
    {
        // La séquence tourne de 1 à 255 ; 0 est réservé pour « désactivé ».
        byte current = _sequences.TryGetValue(portAddress, out var s) ? s : (byte)0;
        byte next = current >= 255 ? (byte)1 : (byte)(current + 1);
        _sequences[portAddress] = next;
        return next;
    }

    public void Dispose() => _udp.Dispose();
}
