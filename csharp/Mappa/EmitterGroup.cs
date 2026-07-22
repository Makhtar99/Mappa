using System;
using System.Collections.Generic;

namespace Mappa
{
    /// <summary>
    /// Noeud "Emitter Group" du pipeline de routage.
    ///
    /// Deuxieme etage de la chaine, apres "Unity to Emitters" (reception eHuB) :
    /// il selectionne une plage contigue d'IDs d'entites [First..Last] dans le
    /// State recu, en ignorant toutes les autres entites. Sa sortie ordonnee
    /// (triee par ID) alimente le noeud suivant "Emitter Group to DMX".
    ///
    /// Exemple du sujet : un groupe 1..10 ne conserve que les entites 1 a 10.
    /// Plusieurs groupes peuvent coexister pour router differentes plages
    /// d'entites vers differents controleurs.
    /// </summary>
    public sealed class EmitterGroup
    {
        public int First { get; }
        public int Last { get; }

        public EmitterGroup(int first, int last)
        {
            if (last < first)
                throw new ArgumentException($"Plage d'entites invalide : {first}..{last}");
            First = first;
            Last = last;
        }

        /// <summary>Une entite selectionnee : son ID et sa couleur a l'instant t.</summary>
        public readonly struct Selected
        {
            public readonly int EntityId;
            public readonly Color Color;

            public Selected(int entityId, Color color)
            {
                EntityId = entityId;
                Color = color;
            }
        }

        /// <summary>
        /// Retourne les entites de la plage presentes dans le state, triees par ID
        /// croissant. L'ordre correspond a l'ordre de cablage sequentiel attendu
        /// par le noeud "Emitter Group to DMX".
        /// </summary>
        public IReadOnlyList<Selected> Select(State state)
        {
            var result = new List<Selected>();
            foreach (int id in state.EntityIds)
            {
                if (id >= First && id <= Last)
                    result.Add(new Selected(id, state.Get(id)));
            }
            result.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
            return result;
        }
    }
}
