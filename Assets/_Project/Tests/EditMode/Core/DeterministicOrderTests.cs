using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Ordering;

namespace RoadToPro.Tests.EditMode.Core
{
    public class DeterministicOrderTests
    {
        [Test]
        public void HashSet_DifferentInsertOrder_SameResultHash()
        {
            var ids = new List<int>();
            for (int i = 0; i < 500; i++) ids.Add((i * 7919) % 10007);
            var setA = new HashSet<int>();
            foreach (var x in ids) setA.Add(x);
            var setB = new HashSet<int>();
            for (int i = ids.Count - 1; i >= 0; i--) setB.Add(ids[i]);
            // 삽입·삭제 순서를 흔들어 내부 버킷 순서를 다르게
            setB.Remove(ids[0]);
            setB.Add(ids[0]);

            Assert.That(HashOf(DeterministicOrder.SortedItems(setA, new List<int>(), Comparer<int>.Default)),
                Is.EqualTo(HashOf(DeterministicOrder.SortedItems(setB, new List<int>(), Comparer<int>.Default))));

            var dictA = new Dictionary<string, int>();
            var dictB = new Dictionary<string, int>();
            foreach (var x in ids) dictA["p" + x] = x;
            for (int i = ids.Count - 1; i >= 0; i--) dictB["p" + ids[i]] = ids[i];
            var ka = DeterministicOrder.SortedKeys(dictA, new List<string>());
            var kb = DeterministicOrder.SortedKeys(dictB, new List<string>());
            Assert.That(ka, Is.EqualTo(kb));
        }

        static ulong HashOf(List<int> items)
        {
            var h = StableHasher.Create();
            foreach (var x in items) h.Add(x);
            return h.Value;
        }

        readonly struct P
        {
            public readonly int Id;
            public readonly int Score;

            public P(int id, int score)
            {
                Id = id;
                Score = score;
            }
        }

        [Test]
        public void StableSort_TiesById()
        {
            var list = new List<P>();
            for (int i = 0; i < 200; i++) list.Add(new P(200 - i, i % 5));
            DeterministicOrder.StableSort(list, (a, b) => DeterministicOrder.ThenById(b.Score.CompareTo(a.Score), a.Id, b.Id));
            for (int i = 1; i < list.Count; i++)
            {
                var prev = list[i - 1];
                var cur = list[i];
                Assert.That(prev.Score > cur.Score || (prev.Score == cur.Score && prev.Id < cur.Id), $"{i}");
            }
        }

        [Test]
        public void StableSort_PreservesEqualOrder()
        {
            var list = new List<P>();
            for (int i = 0; i < 100; i++) list.Add(new P(i, i % 3));
            DeterministicOrder.StableSort(list, (a, b) => a.Score.CompareTo(b.Score)); // id 비교 없음
            for (int i = 1; i < list.Count; i++)
                if (list[i - 1].Score == list[i].Score)
                    Assert.That(list[i - 1].Id, Is.LessThan(list[i].Id));
        }

        [Test]
        public void StableSort_Span_NoAllocPath_ShortScratchThrows()
        {
            var arr = new[] { 3, 1, 2 };
            Assert.Throws<ArgumentException>(() =>
                DeterministicOrder.StableSort<int, Comparer<int>>(arr, new int[1], Comparer<int>.Default));
        }
    }
}
