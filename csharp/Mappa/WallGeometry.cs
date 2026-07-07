using System.Collections.Generic;
using System.Linq;

namespace Mappa
{
    /// <summary>
    /// Geometrie d'un mur LED : la correspondance entre une position logique
    /// (col, row) et l'ID d'entite a allumer. Sert a l'affichage (texte, image)
    /// cote authoring — ce n'est PAS le contrat de routage.
    ///
    /// Le modele couvre le mur Glassworks (bandes verticales en serpentin) mais
    /// est parametrable, et peut etre DEDUIT d'une <see cref="Config"/> via
    /// <see cref="Infer"/> pour s'adapter dynamiquement a differents panneaux.
    ///
    /// Modele serpentin (Serpentine=true) : chaque bande physique monte puis
    /// descend, avec des LED de fixation invisibles aux extremites/milieu :
    ///   index 0             : fixation (bas)
    ///   index 1..Rows       : montee visible
    ///   index Rows+1        : fixation (haut)
    ///   index Rows+2..2*Rows+1 : descente visible
    ///   index 2*Rows+2      : fixation (bas)
    /// La montee et la descente forment deux colonnes logiques adjacentes
    /// (Columns = 2 * nombre de bandes). En mode simple (Serpentine=false) une
    /// bande = une colonne de Rows LED.
    /// </summary>
    public sealed class WallGeometry
    {
        public int Columns { get; set; } = 128;
        public int Rows { get; set; } = 128;
        public int BandsPerController { get; set; } = 16;
        public int BandStride { get; set; } = 300;      // ecart d'entites entre 2 bandes
        public int ControllerOffset { get; set; } = 5000; // ecart d'entites entre 2 controleurs
        public int EntityBase { get; set; } = 100;
        public bool Serpentine { get; set; } = true;

        /// <summary>Geometrie par defaut : le mur Glassworks 128x128.</summary>
        public static WallGeometry Default => new WallGeometry();

        /// <summary>Position logique (col,row), row 0 en haut, -> ID d'entite (ou -1 si hors mur/fixation).</summary>
        public int EntityId(int col, int row)
        {
            if (col < 0 || col >= Columns || row < 0 || row >= Rows) return -1;

            int band, ledIndex;
            if (Serpentine)
            {
                band = col / 2;
                bool descending = (col % 2) != 0;
                ledIndex = !descending
                    ? 1 + (Rows - 1 - row)   // montee : bas=1 .. haut=Rows
                    : (Rows + 2) + row;      // descente : haut=Rows+2 .. bas=2*Rows+1
            }
            else
            {
                band = col;
                ledIndex = row;
            }

            int bandInCtrl = band % BandsPerController;
            int controller = band / BandsPerController;
            int baseEntity = EntityBase + bandInCtrl * BandStride + controller * ControllerOffset;
            return baseEntity + ledIndex;
        }

        /// <summary>
        /// Deduit la geometrie du schema d'adressage d'une Config : base d'entites,
        /// pas entre bandes, offset entre controleurs, nombre de bandes/controleur,
        /// LED par bande (=> Rows en serpentin), nombre de colonnes.
        /// Retombe sur <see cref="Default"/> si la config est vide.
        /// </summary>
        public static WallGeometry Infer(Config config)
        {
            if (config.EntityMap.Count == 0) return Default;

            // universe global -> controleur (pour rattacher chaque bande a un ctrl)
            var universeCtrl = new Dictionary<int, string>();
            foreach (var u in config.Universes) universeCtrl[u.Index] = u.ControllerId;

            // Regroupe les entity_map en "bandes" : entrees a entites contigues.
            var entries = config.EntityMap.OrderBy(m => m.EntityStart).ToList();
            var bandStart = new List<int>();
            var bandTotal = new List<int>();
            var bandCtrl = new List<string>();

            int prevEnd = int.MinValue;
            foreach (var m in entries)
            {
                bool newBand = bandStart.Count == 0 || m.EntityStart != prevEnd + 1;
                if (newBand)
                {
                    bandStart.Add(m.EntityStart);
                    bandTotal.Add(m.EntityEnd - m.EntityStart + 1);
                    universeCtrl.TryGetValue(m.UniverseStart, out string? cid);
                    bandCtrl.Add(cid ?? "");
                }
                else
                {
                    bandTotal[bandTotal.Count - 1] += m.EntityEnd - m.EntityStart + 1;
                }
                prevEnd = m.EntityEnd;
            }

            var geo = new WallGeometry();

            // LED par bande dominante (le mur), afin d'ignorer les appareils
            // auxiliaires (lyres, projecteurs...) qui ont des tailles differentes
            // et fausseraient la geometrie.
            int lpb = MostCommon(bandTotal);
            geo.SetRowsFromLedsPerBand(lpb);

            // On ne garde que les bandes du mur (taille == taille dominante).
            var wallStart = new List<int>();
            var wallCtrl = new List<string>();
            for (int i = 0; i < bandStart.Count; i++)
            {
                if (bandTotal[i] != lpb) continue;
                wallStart.Add(bandStart[i]);
                wallCtrl.Add(bandCtrl[i]);
            }
            if (wallStart.Count == 0) { wallStart.AddRange(bandStart); wallCtrl.AddRange(bandCtrl); }

            geo.EntityBase = wallStart[0];

            // Pas entre bandes.
            geo.BandStride = wallStart.Count > 1 ? wallStart[1] - wallStart[0] : lpb;

            // Controleurs et bandes/controleur.
            int numCtrl = wallCtrl.Distinct().Count(c => c != "");
            if (numCtrl < 1) numCtrl = 1;
            geo.BandsPerController = System.Math.Max(1, wallStart.Count / numCtrl);

            // Offset entre controleurs = debut de la 1ere bande du 2e controleur.
            geo.ControllerOffset = wallStart.Count > geo.BandsPerController
                ? wallStart[geo.BandsPerController] - wallStart[0]
                : geo.BandStride * geo.BandsPerController;

            // Colonnes = bandes (x2 en serpentin).
            geo.Columns = geo.Serpentine ? wallStart.Count * 2 : wallStart.Count;
            return geo;
        }

        /// <summary>LED par bande utilisee lors de l'inference (info/diagnostic).</summary>
        public int LedsPerBand { get; private set; } = 259;

        /// <summary>
        /// Deduit Rows et le mode serpentin depuis le nombre de LED par bande.
        /// Bande "en serpentin" = 2*Rows + 3 LED (montee + descente + 3 fixations).
        /// Sinon bande simple = Rows LED.
        /// </summary>
        private void SetRowsFromLedsPerBand(int ledsPerBand)
        {
            LedsPerBand = ledsPerBand;
            if (ledsPerBand > 3 && (ledsPerBand - 3) % 2 == 0)
            {
                Serpentine = true;
                Rows = (ledsPerBand - 3) / 2;
            }
            else
            {
                Serpentine = false;
                Rows = ledsPerBand;
            }
        }

        private static int MostCommon(List<int> values)
        {
            var counts = new Dictionary<int, int>();
            foreach (int v in values) counts[v] = counts.TryGetValue(v, out int c) ? c + 1 : 1;
            int best = values[0], bestCount = 0;
            foreach (var kv in counts)
                if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
            return best;
        }
    }
}
