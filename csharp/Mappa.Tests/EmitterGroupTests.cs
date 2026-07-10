using System.Linq;
using Mappa;
using Xunit;

namespace Mappa.Tests
{
    /// <summary>
    /// Tests du noeud "Emitter Group" : la selection d'une plage d'entites,
    /// tel que decrit dans l'exemple du sujet (groupe 1..10, entites jaunes).
    /// </summary>
    public class EmitterGroupTests
    {
        // Fabrique un state de `count` entites (IDs 1..count) toutes en jaune (R+G).
        private static State Yellow(int count)
        {
            var s = new State(Enumerable.Range(1, count));
            for (int id = 1; id <= count; id++)
                s.Set(id, 255, 255, 0);
            return s;
        }

        [Fact]
        public void SelectsOnlyEntitiesInRange()
        {
            var group = new EmitterGroup(1, 10);

            var selected = group.Select(Yellow(20)); // 20 entites en entree

            Assert.Equal(10, selected.Count);          // seules 1..10 gardees
            Assert.Equal(1, selected[0].EntityId);
            Assert.Equal(10, selected[9].EntityId);
        }

        [Fact]
        public void KeepsYellowColorOfSelectedEntities()
        {
            var group = new EmitterGroup(1, 10);

            var selected = group.Select(Yellow(20));

            // Comme dans le moniteur du sujet : chaque entite recue est jaune.
            Assert.All(selected, s =>
            {
                Assert.Equal((byte)255, s.Color.R);
                Assert.Equal((byte)255, s.Color.G);
                Assert.Equal((byte)0, s.Color.B);
            });
        }

        [Fact]
        public void IgnoresEntitiesOutsideRange()
        {
            var group = new EmitterGroup(5, 8);

            var ids = group.Select(Yellow(20)).Select(s => s.EntityId).ToArray();

            Assert.Equal(new[] { 5, 6, 7, 8 }, ids);
        }

        [Fact]
        public void ReturnsEntitiesSortedById()
        {
            // IDs volontairement crees dans le desordre.
            var s = new State(new[] { 3, 1, 2 });
            s.Set(1, 1, 1, 1);
            s.Set(2, 2, 2, 2);
            s.Set(3, 3, 3, 3);
            var group = new EmitterGroup(1, 3);

            var ids = group.Select(s).Select(x => x.EntityId).ToArray();

            Assert.Equal(new[] { 1, 2, 3 }, ids);
        }

        [Fact]
        public void RejectsInvalidRange()
        {
            Assert.Throws<System.ArgumentException>(() => new EmitterGroup(10, 1));
        }
    }
}
