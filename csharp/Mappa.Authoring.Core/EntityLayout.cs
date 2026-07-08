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

        public static EntityLayout FromGrid(Config config)
        {
            var mappings = new List<EntityMapping>(config.EntityMap);
            mappings.Sort((a, b) =>
            {
                int c = a.UniverseStart.CompareTo(b.UniverseStart);
                return c != 0 ? c : a.ChannelStart.CompareTo(b.ChannelStart);
            });

            int cols = mappings.Count;
            int rows = 0;
            foreach (var m in mappings) rows = Math.Max(rows, m.Count);

            var pos = new Dictionary<int, EntityPos>();
            var ids = new List<int>();
            float colDen = Math.Max(1, cols - 1);
            float rowDen = Math.Max(1, rows - 1);

            for (int col = 0; col < cols; col++)
            {
                var m = mappings[col];
                for (int row = 0; row < m.Count; row++)
                {
                    int id = m.EntityStart + row;
                    float nx = col / colDen;
                    float ny = row / rowDen;
                    pos[id] = new EntityPos(nx, ny, 0f, nx, ny, col, row);
                    ids.Add(id);
                }
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
