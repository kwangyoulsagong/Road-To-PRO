using System;
using NUnit.Framework;
using RoadToPro.Core.Random;

namespace RoadToPro.Tests.EditMode.Core
{
    public class DistributionTests
    {
        [Test]
        public void IntDice_ChiSquare_p001()
        {
            var rng = new SeedRng(11UL);
            var counts = new long[7];
            const int n = 6_000_000;
            for (int i = 0; i < n; i++) counts[rng.Range(1, 6)]++;
            Assert.That(counts[0], Is.EqualTo(0));
            double expected = n / 6.0, chi = 0;
            for (int k = 1; k <= 6; k++) chi += (counts[k] - expected) * (counts[k] - expected) / expected;
            Assert.That(chi, Is.LessThan(20.515), $"χ²={chi}"); // 자유도 5, p=0.001
        }

        [Test]
        public void Normal_MeanStd()
        {
            var rng = new SeedRng(12UL);
            const int n = 1_000_000;
            double sum = 0, sq = 0;
            for (int i = 0; i < n; i++)
            {
                double v = rng.Normal();
                sum += v;
                sq += v * v;
            }
            var mean = sum / n;
            var std = Math.Sqrt(sq / n - mean * mean);
            Assert.That(mean, Is.EqualTo(0).Within(0.005));
            Assert.That(std, Is.EqualTo(1).Within(0.005));
            Assert.That(rng.Normal(10f, 0f), Is.EqualTo(10f));
        }

        [Test]
        public void LogNormal_Positive_MedianNearExpMu()
        {
            var rng = new SeedRng(13UL);
            var below = 0;
            for (int i = 0; i < 100_000; i++)
            {
                var v = rng.LogNormal(1f, 0.5f);
                Assert.That(v, Is.GreaterThan(0f));
                if (v < Math.Exp(1)) below++;
            }
            Assert.That(below / 100_000.0, Is.EqualTo(0.5).Within(0.01));
        }

        [Test]
        public void Bernoulli_Rate()
        {
            var rng = new SeedRng(14UL);
            var hits = 0;
            for (int i = 0; i < 100_000; i++) if (rng.Bernoulli(0.3f)) hits++;
            Assert.That(hits / 100_000.0, Is.EqualTo(0.3).Within(0.01));
            Assert.That(rng.Bernoulli(0f), Is.False);
            Assert.That(rng.Bernoulli(1f), Is.True);
        }

        [Test]
        public void Weighted_Proportional()
        {
            var rng = new SeedRng(15UL);
            var w = new[] { 1f, 0f, 3f };
            var c = new int[3];
            for (int i = 0; i < 100_000; i++) c[rng.PickWeighted(w)]++;
            Assert.That(c[1], Is.EqualTo(0));
            Assert.That(c[2] / 100_000.0, Is.EqualTo(0.75).Within(0.01));
        }

        [Test]
        public void Softmax_LowTemp_PicksMax99()
        {
            var rng = new SeedRng(16UL);
            var u = new[] { 0.5f, 1.0f, 0.9f };
            var hit = 0;
            for (int i = 0; i < 10_000; i++) if (rng.PickSoftmax(u, 0.01f) == 1) hit++;
            Assert.That(hit / 10_000.0, Is.GreaterThanOrEqualTo(0.99));
        }

        [Test]
        public void Softmax_ZeroTemp_ArgmaxLowestIndexOnTie()
        {
            var rng = new SeedRng(17UL);
            Assert.That(rng.PickSoftmax(new[] { 0.2f, 0.9f, 0.9f }, 0f), Is.EqualTo(1));
        }

        [Test]
        public void Softmax_HighTemp_NearUniform()
        {
            var rng = new SeedRng(18UL);
            var c = new int[2];
            for (int i = 0; i < 100_000; i++) c[rng.PickSoftmax(new[] { 0f, 1f }, 1000f)]++;
            Assert.That(c[0] / 100_000.0, Is.EqualTo(0.5).Within(0.01));
        }

        [Test]
        public void Shuffle_Permutation_Deterministic()
        {
            var a = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var b = (int[])a.Clone();
            new SeedRng(19UL).Shuffle(a);
            new SeedRng(19UL).Shuffle(b);
            Assert.That(a, Is.EqualTo(b));
            var sorted = (int[])a.Clone();
            Array.Sort(sorted);
            Assert.That(sorted, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        }

        [Test]
        public void Int_Edges()
        {
            var rng = new SeedRng(20UL);
            Assert.That(rng.Range(5, 5), Is.EqualTo(5));
            bool sawNeg = false, sawPos = false;
            for (int i = 0; i < 1000; i++)
            {
                var v = rng.Range(int.MinValue, int.MaxValue);
                sawNeg |= v < 0;
                sawPos |= v > 0;
            }
            Assert.That(sawNeg && sawPos);
            for (int i = 0; i < 1000; i++) Assert.That(rng.Range(-3, 3), Is.InRange(-3, 3));
        }

        [Test]
        public void Weighted_AllZero_Throws() =>
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).PickWeighted(new[] { 0f, 0f }));

        [Test]
        public void Weighted_Negative_Throws() =>
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).PickWeighted(new[] { 1f, -0.5f }));

        [Test]
        public void Weighted_NaN_Throws() =>
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).PickWeighted(new[] { 1f, float.NaN }));

        [Test]
        public void Weighted_Empty_Throws() =>
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).PickWeighted(new float[0]));

        [Test]
        public void Int_AGreaterB_Throws() =>
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).Range(3, 2));

        [Test]
        public void Softmax_BadInputs_Throw()
        {
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).PickSoftmax(new[] { 1f, float.NaN }, 1f));
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).PickSoftmax(new[] { 1f }, -1f));
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).PickSoftmax(new float[0], 1f));
        }
    }
}
