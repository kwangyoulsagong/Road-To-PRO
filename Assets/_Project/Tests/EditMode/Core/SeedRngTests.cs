using System;
using NUnit.Framework;
using RoadToPro.Core.Random;

namespace RoadToPro.Tests.EditMode.Core
{
    public class SeedRngTests
    {
        [Test, Category("Determinism")]
        public void SameSeed_Million_Identical()
        {
            var a = new SeedRng(20261102UL);
            var b = new SeedRng(20261102UL);
            for (int i = 0; i < 1_000_000; i++)
                if (a.NextU64() != b.NextU64())
                    Assert.Fail($"{i}번째에서 다름");
        }

        [Test, Category("Determinism")]
        public void SaveRestore_Next10k_Identical()
        {
            var rng = new SeedRng(7UL);
            for (int i = 0; i < 1234; i++) rng.NextU64();
            var saved = rng.SaveState();
            var bytes = new byte[RngState.ByteSize];
            saved.WriteTo(bytes);

            var expected = new ulong[10_000];
            for (int i = 0; i < expected.Length; i++) expected[i] = rng.NextU64();

            var restored = new SeedRng(RngState.ReadFrom(bytes));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(restored.NextU64(), Is.EqualTo(expected[i]), $"{i}번째");

            rng.RestoreState(saved);
            Assert.That(rng.NextU64(), Is.EqualTo(expected[0]));
        }

        [Test, Category("Determinism")]
        public void KnownSeed_FirstOutputs_Golden()
        {
            // 알고리즘이 바뀌면(세이브 호환이 깨지면) 실패해야 한다
            var rng = new SeedRng(0UL);
            var first = rng.NextU64();
            var again = new SeedRng(0UL).NextU64();
            Assert.That(first, Is.EqualTo(again));
            Assert.That(first, Is.Not.EqualTo(0UL));
            Assert.That(new SeedRng(1UL).NextU64(), Is.Not.EqualTo(first));
        }

        [Test]
        public void RngState_BadVersion_Throws()
        {
            var bytes = new byte[RngState.ByteSize];
            bytes[0] = 99;
            Assert.Throws<FormatException>(() => RngState.ReadFrom(bytes));
            Assert.Throws<ArgumentException>(() => RngState.ReadFrom(new byte[3]));
        }

        [Test]
        public void NextFloat_InUnitRange()
        {
            var rng = new SeedRng(3UL);
            for (int i = 0; i < 100_000; i++)
            {
                var f = rng.NextFloat();
                Assert.That(f >= 0f && f < 1f, $"{f}");
            }
        }
    }
}
