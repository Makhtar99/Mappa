using System.Net;
using LedNetwork.Core.DesignProtocol;
using LedNetwork.Core.Routing;
using LedNetwork.Core.Transport;
using LedNetwork.Host;

// Mode auto-test : « dotnet run --project src/LedNetwork.Host -- --test-artdmx »
// Vérifie ArtDmxPacket (Serialize/TryParse) sans démarrer l'outil de routage.
if (args.Contains("--test-artdmx"))
{
    return ArtDmxSelfTest.Run();
}

// ---------------------------------------------------------------------------
// Démonstration de l'outil de routage (« Outil de routage » du schéma).
//
//   Outil de conception  --UDP perso (port 7000)-->  CET HÔTE  --Art-Net (6454)-->  Contrôleurs BC216
//
// Ce programme :
//   1. écoute l'« état souhaité » émis par l'outil de conception ;
//   2. mappe chaque entité vers un univers/canal DMX via la table de patch ;
//   3. envoie les paquets ArtDMX aux contrôleurs.
// ---------------------------------------------------------------------------

Console.WriteLine("=== Outil de routage LED — démarrage ===");

// 1) Table de patch : quelle entité -> quel univers / canal / ordre couleur.
//    (À charger depuis un fichier de configuration dans la vraie application.)
var patches = new[]
{
    new FixturePatch { EntityId = 1, Universe = 0, StartChannel = 1, ColorOrder = ColorOrder.Grb },
    new FixturePatch { EntityId = 2, Universe = 0, StartChannel = 4, ColorOrder = ColorOrder.Grb },
    new FixturePatch { EntityId = 3, Universe = 1, StartChannel = 1, ColorOrder = ColorOrder.Rgbw },
};

// 2) Mapping univers -> adresse IP du contrôleur BC216.
var controllers = new Dictionary<ushort, IPAddress>
{
    [0] = IPAddress.Parse("192.168.1.50"),
    [1] = IPAddress.Parse("192.168.1.51"),
};

using var sender = new ArtNetSender();
var router = new DmxRouter(sender, patches, controllers);

// 3) Réception de l'état depuis l'outil de conception + routage.
using var receiver = new DesignStateReceiver(port: DesignStateReceiver.DefaultPort);
receiver.StateReceived += (state, from) =>
{
    Console.WriteLine($"[Frame {state.FrameId}] {state.Entities.Count} entités reçues de {from}");
    router.Route(state);
};
receiver.Start();

Console.WriteLine($"En écoute du protocole de conception sur UDP {DesignStateReceiver.DefaultPort}.");
Console.WriteLine("Appuyez sur [Entrée] pour envoyer une frame de test locale, ou Ctrl+C pour quitter.");

// Petit émetteur de test : simule l'outil de conception en local.
using var loopbackSender = new System.Net.Sockets.UdpClient();
uint frame = 0;
while (true)
{
    Console.ReadLine();
    var msg = new DesignStateMessage
    {
        FrameId = frame++,
        Entities =
        {
            new EntityColor { EntityId = 1, R = 255, G = 0,   B = 0,   Intensity = 255 },
            new EntityColor { EntityId = 2, R = 0,   G = 255, B = 0,   Intensity = 128 },
            new EntityColor { EntityId = 3, R = 0,   G = 0,   B = 255, W = 64, Intensity = 255 },
        }
    };
    byte[] bytes = msg.Serialize();
    loopbackSender.Send(bytes, bytes.Length, "127.0.0.1", DesignStateReceiver.DefaultPort);
    Console.WriteLine($"Frame de test {msg.FrameId} envoyée.");
}
