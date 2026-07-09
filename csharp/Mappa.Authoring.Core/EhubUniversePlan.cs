using System;
using System.Collections.Generic;

namespace Mappa.Authoring.Core
{
    public sealed class EhubChunk
    {
        public byte Universe;
        public int[] Ids = Array.Empty<int>();
        public List<Ehub.Range> Ranges = new List<Ehub.Range>();
    }

    public static class EhubUniversePlan
    {
        public const int DefaultMaxEntitiesPerMessage = 5000;

        public static List<EhubChunk> Build(IReadOnlyList<int> orderedIds, int maxPerMessage = DefaultMaxEntitiesPerMessage)
        {
            if (maxPerMessage <= 0) maxPerMessage = DefaultMaxEntitiesPerMessage;

            var chunks = new List<EhubChunk>();
            int total = orderedIds.Count;
            byte universe = 0;
            for (int start = 0; start < total; start += maxPerMessage)
            {
                int count = Math.Min(maxPerMessage, total - start);
                var ids = new int[count];
                for (int i = 0; i < count; i++) ids[i] = orderedIds[start + i];

                chunks.Add(new EhubChunk
                {
                    Universe = universe++,
                    Ids = ids,
                    Ranges = Ehub.ComputeRanges(ids),
                });
            }
            if (chunks.Count == 0)
                chunks.Add(new EhubChunk { Universe = 0, Ids = Array.Empty<int>(), Ranges = new List<Ehub.Range>() });
            return chunks;
        }
    }
}
