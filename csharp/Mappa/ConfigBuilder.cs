using System.Collections.Generic;

namespace Mappa
{
    /// <summary>
    /// Une ligne du tableau d'adressage "eHuB" (tel que fourni dans les fichiers
    /// Excel des panneaux) : une plage d'entites rattachee a un univers ArtNet
    /// d'un controleur (identifie par son IP).
    /// </summary>
    public sealed class EhubRow
    {
        public string Name { get; set; } = "";
        public int EntityStart { get; set; }
        public int EntityEnd { get; set; }
        public string Ip { get; set; } = "";
        public int Universe { get; set; }       // univers ArtNet LOCAL du controleur (0..)
        public LedType LedType { get; set; } = LedType.RGB;

        public int LedCount => EntityEnd - EntityStart + 1;
    }

    /// <summary>
    /// Construit une <see cref="Config"/> a partir de donnees d'adressage brutes
    /// (lignes eHuB). Ce code est PUR (aucune dependance, aucune E/S) : il vit
    /// dans le coeur Mappa pour etre reutilise aussi bien par le CLI que par
    /// Unity. La lecture du fichier (Excel/CSV) est une couche separee.
    ///
    /// Points cles gerentes ici :
    ///  - chaque IP distincte devient un controleur ;
    ///  - chaque univers recoit un Index GLOBAL unique dans la config, mais
    ///    conserve son numero ArtNet LOCAL (0..31) dans <see cref="Universe.ArtNetUniverse"/>
    ///    (indispensable : sur le fil on envoie l'univers local, pas l'index global) ;
    ///  - si plusieurs lignes partagent le meme univers, leurs canaux sont
    ///    empiles automatiquement (channel_start sequentiel) pour eviter les
    ///    collisions.
    /// </summary>
    public static class ConfigBuilder
    {
        public static Config BuildFromEhub(
            IEnumerable<EhubRow> rows, string name = "imported", int port = 6454)
        {
            var config = new Config(name);

            // 1) IPs distinctes, dans l'ordre d'apparition -> controleurs.
            var ipOrder = new List<string>();
            int maxUniverse = 0;
            var rowList = new List<EhubRow>();
            foreach (var row in rows)
            {
                rowList.Add(row);
                if (!ipOrder.Contains(row.Ip)) ipOrder.Add(row.Ip);
                if (row.Universe > maxUniverse) maxUniverse = row.Universe;
            }

            var ipToId = new Dictionary<string, string>();
            for (int i = 0; i < ipOrder.Count; i++)
            {
                string id = $"ctrl-{i + 1}";
                ipToId[ipOrder[i]] = id;
                config.Controllers.Add(new Controller { Id = id, Ip = ipOrder[i], Port = port, Outputs = 16 });
            }

            // 2) Index d'univers GLOBAL unique = ipIndex * span + universe.
            //    span > plus grand numero d'univers pour garantir l'unicite.
            int span = maxUniverse + 1;

            var universeSeen = new Dictionary<int, Universe>();
            var channelCursor = new Dictionary<int, int>();

            foreach (var row in rowList)
            {
                int ipIndex = ipOrder.IndexOf(row.Ip);
                int gIndex = ipIndex * span + row.Universe;

                if (!universeSeen.ContainsKey(gIndex))
                {
                    var u = new Universe
                    {
                        Index = gIndex,
                        ControllerId = ipToId[row.Ip],
                        Output = row.Universe / Config.UniversesPerOutput,
                        ArtNetUniverse = row.Universe,
                    };
                    universeSeen[gIndex] = u;
                    config.Universes.Add(u);
                    channelCursor[gIndex] = 0;
                }

                int ch = channelCursor[gIndex];
                config.EntityMap.Add(new EntityMapping
                {
                    EntityStart = row.EntityStart,
                    EntityEnd = row.EntityEnd,
                    UniverseStart = gIndex,
                    ChannelStart = ch,
                    LedType = row.LedType,
                });
                config.Strips.Add(new Strip
                {
                    Id = row.Name,
                    LedCount = row.LedCount,
                    UniverseStart = gIndex,
                    ChannelStart = ch,
                    LedType = row.LedType,
                });
                channelCursor[gIndex] = ch + row.LedCount * row.LedType.Channels();
            }

            return config;
        }
    }
}
