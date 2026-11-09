using NUnit.Framework;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public class HungarianTests
    {
        const int M = SlotMatcher.MaxSize;

        [Test]
        public void Crossing_Resolved()
        {
            var c = new float[M * M];
            c[0] = 100; c[1] = 1; c[M] = 1; c[M + 1] = 100;
            var r = new int[2];
            new SlotMatcher().Solve(2, 2, c, r);
            Assert.That(r, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void AllTies_SlotIdOrder()
        {
            var c = new float[M * M];
            var r = new int[5];
            new SlotMatcher().Solve(5, 5, c, r);
            Assert.That(r, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
        }

        [Test]
        public void Rectangular_LeavesCostliestColumn()
        {
            var c = new float[M * M];
            for (int i = 0; i < 2; i++) { c[i * M] = 5; c[i * M + 1] = 5; c[i * M + 2] = 50; }
            var r = new int[2];
            new SlotMatcher().Solve(2, 3, c, r);
            Assert.That(r[0], Is.Not.EqualTo(r[1]));
            Assert.That(r, Has.None.EqualTo(2));
        }

        [Test]
        public void MatchesBruteForce()
        {
            var rng = new SeedRng(44UL);
            var m = new SlotMatcher();
            for (int k = 0; k < 200; k++)
            {
                var c = new float[M * M];
                for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++) c[i * M + j] = rng.Range(0f, 100f);
                var r = new int[6];
                m.Solve(6, 6, c, r);
                float got = 0;
                for (int i = 0; i < 6; i++) got += c[i * M + r[i]];
                Assert.That(got, Is.EqualTo(Best(c, 0, 0)).Within(1e-2f));
            }
        }

        static float Best(float[] c, int row, int used)
        {
            if (row == 6) return 0;
            var b = float.MaxValue;
            for (int j = 0; j < 6; j++)
            {
                if ((used & (1 << j)) != 0) continue;
                var v = c[row * M + j] + Best(c, row + 1, used | (1 << j));
                if (v < b) b = v;
            }
            return b;
        }
    }

    public class ZoneResponderTests
    {
        [Test]
        public void WeightWidensZone()
        {
            var slots = new[] { default, new RoadToPro.Core.Math.Vec2(.4f, .5f), new RoadToPro.Core.Math.Vec2(.6f, .5f) };
            var active = new[] { false, true, true };
            var skip = new bool[3];
            var point = new RoadToPro.Core.Math.Vec2(.52f, .5f);
            Assert.That(ZoneResponder.Find(point, slots, new[] { 0f, 1f, 1f }, active, skip, 105, 68), Is.EqualTo(2));
            Assert.That(ZoneResponder.Find(point, slots, new[] { 0f, 3f, 1f }, active, skip, 105, 68), Is.EqualTo(1), "반경이 크면 영역이 넓다");
            skip[2] = true;
            Assert.That(ZoneResponder.Find(point, slots, new[] { 0f, 1f, 1f }, active, skip, 105, 68), Is.EqualTo(1));
        }
    }
}
