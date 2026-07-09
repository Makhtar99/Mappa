using System.Collections.Generic;
using System.Linq;

namespace Mappa
{
    /// <summary>
    /// Redeploiement / tolerance aux pannes (P1 "Exhaustivite").
    ///
    /// Gere le scenario : un controleur tombe en panne -> on en ajoute un et on
    /// redirige. La panne ne change PAS l'adressage logique des entites (les LED
    /// ne bougent pas) : seul le controleur qui pilote chaque univers change,
    /// donc l'IP de destination des paquets ArtNet. Le RoutingPlan reste
    /// identique apres un failover.
    /// </summary>
    public static class Failover
    {
        public static Controller AddController(
            Config config, string controllerId, string ip, int port = 6454, int outputs = 16)
        {
            if (config.Controllers.Any(c => c.Id == controllerId))
            {
                throw new System.ArgumentException($"Un controleur {controllerId} existe deja.");
            }
            var controller = new Controller { Id = controllerId, Ip = ip, Port = port, Outputs = outputs };
            config.Controllers.Add(controller);
            return controller;
        }

        /// <summary>
        /// Reaffecte des univers d'un controleur vers un autre. Si
        /// <paramref name="universes"/> est null, TOUS les univers de
        /// <paramref name="fromController"/> sont deplaces. Ne touche pas a
        /// l'EntityMap. Retourne les index d'univers reaffectes.
        /// </summary>
        public static List<int> ReassignUniverses(
            Config config, string fromController, string toController, IEnumerable<int>? universes = null)
        {
            if (!config.Controllers.Any(c => c.Id == toController))
            {
                throw new KeyNotFoundException($"Controleur cible inconnu : {toController}");
            }
            HashSet<int>? filter = universes != null ? new HashSet<int>(universes) : null;

            var moved = new List<int>();
            foreach (var u in config.Universes)
            {
                if (u.ControllerId != fromController) continue;
                if (filter != null && !filter.Contains(u.Index)) continue;
                u.ControllerId = toController;
                moved.Add(u.Index);
            }
            return moved;
        }

        /// <summary>
        /// Scenario complet : un controleur tombe en panne, on le remplace.
        /// 1. Ajoute le remplacant (s'il n'existe pas). 2. Reaffecte ses univers.
        /// 3. Retire (optionnel) le controleur en panne. L'EntityMap est intacte.
        /// </summary>
        public static List<int> ReplaceController(
            Config config, string failedController, string replacementId, string replacementIp,
            int port = 6454, int outputs = 16, bool removeFailed = true)
        {
            if (!config.Controllers.Any(c => c.Id == failedController))
            {
                throw new KeyNotFoundException($"Controleur en panne inconnu : {failedController}");
            }
            if (!config.Controllers.Any(c => c.Id == replacementId))
            {
                AddController(config, replacementId, replacementIp, port, outputs);
            }

            var moved = ReassignUniverses(config, failedController, replacementId);

            if (removeFailed)
            {
                var failed = config.Controllers.FirstOrDefault(c => c.Id == failedController);
                if (failed != null) config.Controllers.Remove(failed);
            }
            return moved;
        }

        /// <summary>Retourne le controleur qui pilote un univers donne (ou null).</summary>
        public static Controller? ControllerOfUniverse(Config config, int universeIndex)
        {
            foreach (var u in config.Universes)
            {
                if (u.Index == universeIndex)
                {
                    return config.Controllers.FirstOrDefault(c => c.Id == u.ControllerId);
                }
            }
            return null;
        }

        /// <summary>Retourne l'objet Universe (definition complete) d'un index global.</summary>
        public static Universe? UniverseOf(Config config, int universeIndex)
        {
            return config.Universes.FirstOrDefault(u => u.Index == universeIndex);
        }
    }
}
