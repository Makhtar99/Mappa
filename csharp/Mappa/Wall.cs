using System;

namespace Mappa
{
    /// <summary>
    /// Generateur du mapping du mur LED de test (P1/P2).
    ///
    /// BuildWallConfig genere programmatiquement une config parametree selon le
    /// schema decrit dans la doc (dimensions, bandes, univers). Aucun fichier
    /// externe requis.
    ///
    /// Schema du mur de test : 64 bandes de 259 LED (LED de fixation invisibles
    /// aux positions 1, 129, 259), 128 univers. Entites par colonnes (100->358,
    /// +300/colonne) et par quarts (offsets +5000 : 100 / 5100 / 10100 / 15100).
    /// </summary>
    public static class Wall
    {
        public static Config BuildWallConfig(
            string name = "wall-128x128",
            int columns = 64,
            int ledsPerColumn = 128,
            string controllerIp = "192.168.1.10",
            int controllerPort = 6454,
            int entityBase = 100,
            int columnStride = 300,
            int quarters = 4,
            int quarterOffset = 5000,
            LedType ledType = LedType.RGB)
        {
            int colsPerQuarter = (columns + quarters - 1) / quarters;
            if (ledsPerColumn > columnStride)
            {
                throw new ArgumentException(
                    $"ledsPerColumn ({ledsPerColumn}) > columnStride ({columnStride}) : chevauchement d'IDs.");
            }
            if (colsPerQuarter * columnStride > quarterOffset)
            {
                throw new ArgumentException(
                    $"Chevauchement de quarts : {colsPerQuarter} colonnes x {columnStride} > quarterOffset ({quarterOffset}).");
            }

            var config = new Config(name);
            config.Controllers.Add(new Controller
            {
                Id = "bc216-1",
                Ip = controllerIp,
                Port = controllerPort,
                Outputs = 16,
            });

            int universeIndex = 0;
            for (int col = 0; col < columns; col++)
            {
                int quarter = col / colsPerQuarter;
                int colInQuarter = col % colsPerQuarter;
                int entityStart = entityBase + quarter * quarterOffset + colInQuarter * columnStride;
                int entityEnd = entityStart + ledsPerColumn - 1;

                config.EntityMap.Add(new EntityMapping
                {
                    EntityStart = entityStart,
                    EntityEnd = entityEnd,
                    UniverseStart = universeIndex,
                    ChannelStart = 0,
                    LedType = ledType,
                });
                config.Strips.Add(new Strip
                {
                    Id = $"strip-{col:D3}",
                    LedCount = ledsPerColumn,
                    UniverseStart = universeIndex,
                    ChannelStart = 0,
                    LedType = ledType,
                });
                int output = universeIndex / Config.UniversesPerOutput;
                config.Universes.Add(new Universe
                {
                    Index = universeIndex,
                    ControllerId = "bc216-1",
                    Output = output,
                });
                universeIndex++;
            }
            return config;
        }
    }
}
