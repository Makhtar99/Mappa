using System;
using System.Collections.Generic;
using Mappa;

namespace Mappa.Authoring.Core
{
    public readonly struct EntityPos
    {
        public readonly float X, Y, Z;
        public readonly float Nx, Ny;
        public readonly int Col, Row;

        public EntityPos(float x, float y, float z, float nx, float ny, int col, int row)
        {
            X = x; Y = y; Z = z; Nx = nx; Ny = ny; Col = col; Row = row;
        }
    }

    public sealed class EntityLayout
    {
        private readonly Dictionary<int, EntityPos> _pos;

        public IReadOnlyList<int> Ids { get; }
        public int Cols { get; }
        public int Rows { get; }

        private EntityLayout(IReadOnlyList<int> ids, Dictionary<int, EntityPos> pos, int cols, int rows)
        {
            Ids = ids;
            _pos = pos;
            Cols = cols;
            Rows = rows;
        }

        public bool TryGet(int id, out EntityPos pos) => _pos.TryGetValue(id, out pos);

        public EntityPos this[int id] => _pos[id];

        /// <summary>
        /// Construit la grille d'authoring a partir de la geometrie REELLE du mur
        /// (<see cref="WallGeometry.Infer"/>) : serpentin, sens de montee/descente
        /// et LED de fixation compris.
        ///
        /// Ne pas rededuire la grille depuis l'entity_map : une bande physique est
        /// decoupee en plusieurs univers (170 + 89 LED sur le mur Glassworks), ce
        /// qui donnerait deux colonnes distinctes, et le rang d'une entite dans sa
        /// plage n'est pas sa ligne (sur une bande montante, la 1re LED est en BAS).
        ///
        /// Les entites hors grille (fixations invisibles, lyres, projecteur) sont
        /// conservees dans <see cref="Ids"/> pour rester pilotables par les effets
        /// globaux, et placees dans une colonne virtuelle au bord droit.
        /// </summary>
        public static EntityLayout FromGrid(Config config)
        {
            var geo = WallGeometry.Infer(config);
            int cols = Math.Max(1, geo.Columns);
            int rows = Math.Max(1, geo.Rows);
            float colDen = Math.Max(1, cols - 1);
            float rowDen = Math.Max(1, rows - 1);

            var pos = new Dictionary<int, EntityPos>();
            var ids = new List<int>();

            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    int id = geo.EntityId(col, row);
                    if (id < 0 || pos.ContainsKey(id)) continue;

                    float nx = col / colDen;
                    float ny = row / rowDen;
                    pos[id] = new EntityPos(nx, ny, 0f, nx, ny, col, row);
                    ids.Add(id);
                }
            }

            // Tout ce que la grille ne couvre pas reste adressable (choix explicite).
            var offGrid = new List<int>();
            foreach (var m in config.EntityMap)
                for (int id = m.EntityStart; id <= m.EntityEnd; id++)
                    if (!pos.ContainsKey(id)) offGrid.Add(id);

            offGrid.Sort();
            float offDen = Math.Max(1, offGrid.Count - 1);
            for (int i = 0; i < offGrid.Count; i++)
            {
                float ny = i / offDen;
                pos[offGrid[i]] = new EntityPos(1f, ny, 0f, 1f, ny, -1, -1);
                ids.Add(offGrid[i]);
            }

            ids.Sort();
            return new EntityLayout(ids, pos, cols, rows);
        }

        public static EntityLayout FromPositions(IReadOnlyDictionary<int, (float X, float Y, float Z)> positions)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (var p in positions.Values)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }
            float spanX = Math.Max(1e-6f, maxX - minX);
            float spanY = Math.Max(1e-6f, maxY - minY);

            var pos = new Dictionary<int, EntityPos>(positions.Count);
            var ids = new List<int>(positions.Count);
            foreach (var kv in positions)
            {
                var p = kv.Value;
                float nx = (p.X - minX) / spanX;
                float ny = (p.Y - minY) / spanY;
                pos[kv.Key] = new EntityPos(p.X, p.Y, p.Z, nx, ny, -1, -1);
                ids.Add(kv.Key);
            }
            ids.Sort();
            return new EntityLayout(ids, pos, 0, 0);
        }
    }
}
