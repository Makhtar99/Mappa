using System.Net;
using LedNetwork.Core.ArtNet;
using LedNetwork.Core.DesignProtocol;
using LedNetwork.Core.Transport;

namespace LedNetwork.Core.Routing;

/// <summary>
/// Cœur de l'outil de routage : transforme l'« état souhaité » (entités RGBW)
/// en trames DMX512, les regroupe par univers Art-Net, puis les envoie aux
/// contrôleurs BC216 via <see cref="ArtNetSender"/>.
///
/// Le mapping entité -> (univers, canal de départ) est décrit par une table de
/// patch (voir <see cref="FixturePatch"/>), équivalent des « outils de patching »
/// du schéma.
/// </summary>
public sealed class DmxRouter
{
    private readonly ArtNetSender _sender;
    private readonly IReadOnlyDictionary<ushort, FixturePatch> _patchByEntity;
    private readonly IReadOnlyDictionary<ushort, IPAddress> _controllerByUniverse;

    public DmxRouter(
        ArtNetSender sender,
        IEnumerable<FixturePatch> patches,
        IReadOnlyDictionary<ushort, IPAddress> controllerByUniverse)
    {
        _sender = sender;
        _patchByEntity = patches.ToDictionary(p => p.EntityId);
        _controllerByUniverse = controllerByUniverse;
    }

    /// <summary>Traite un message d'état et émet les paquets Art-Net correspondants.</summary>
    public void Route(DesignStateMessage state)
    {
        // Univers -> tampon DMX de 512 canaux.
        var universes = new Dictionary<ushort, byte[]>();

        foreach (var entity in state.Entities)
        {
            if (!_patchByEntity.TryGetValue(entity.EntityId, out var patch))
                continue; // entité non patchée : ignorée

            if (!universes.TryGetValue(patch.Universe, out var dmx))
            {
                dmx = new byte[ArtNetConstants.DmxChannelsPerUniverse];
                universes[patch.Universe] = dmx;
            }

            patch.Write(dmx, entity);
        }

        // Émission : un paquet ArtDMX par univers, vers le bon contrôleur.
        foreach (var (universe, dmx) in universes)
        {
            if (_controllerByUniverse.TryGetValue(universe, out var address))
                _sender.Send(address, universe, dmx);
            else
                _sender.Broadcast(universe, dmx); // pas de contrôleur mappé : diffusion
        }
    }
}
