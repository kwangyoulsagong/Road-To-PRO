using System;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.Core.Tuning;

namespace RoadToPro.Tests.EditMode.Core
{
    /// <summary>T23 determinism-reviewer 지적 사항 회귀 테스트.</summary>
    public class ReviewRegressionTests
    {
        /// <summary>NextFloat() = 1 − 2^-24을 내는 상태를 직접 만든다 (xoshiro256** 출력의 상위 24비트가 전부 1).</summary>
        [Test]
        public void RangeFloat_NeverReturnsMax()
        {
            // 출력 상위 24비트가 전부 1인 상태를 찾는다
            var rng = new SeedRng(1UL);
            RngState hit = default;
            bool found = false;
            for (int i = 0; i < 200_000_000 && !found; i++)
            {
                var s = rng.SaveState();
                if ((rng.NextU64() >> 40) == 0xFFFFFF)
                {
                    hit = s;
                    found = true;
                }
            }
            Assume.That(found, "검색 범위에서 최대값 출력을 찾지 못함");
            foreach (var (lo, hi) in new[] { (1f, 2f), (0.5f, 1.5f), (2f, 3f) })
            {
                var r = new SeedRng(hit);
                var v = r.Range(lo, hi);
                Assert.That(v, Is.LessThan(hi), $"Range({lo},{hi}) = {v}");
                Assert.That(v, Is.GreaterThanOrEqualTo(lo));
            }
        }

        [Test]
        public void Weighted_SumOverflow_Throws() =>
            Assert.Throws<ArgumentException>(() => new SeedRng(1UL).PickWeighted(new[] { 3e38f, 3e38f }));

        [Test]
        public void Length_NaN_IsNotHiddenAsZero()
        {
            Assert.That(float.IsNaN(new Vec2(float.NaN, 0f).Length()));
            Assert.That(float.IsNaN(new Vec2(0f, float.NaN).Length()));
            Assert.That(float.IsNaN(new Vec3(float.NaN, 0f, 0f).Length()));
        }

        [Test]
        public void AngleNormalize_HugeInput_InRange()
        {
            foreach (var a in new[] { -210830240f, 3e9f, -1e10f })
            {
                var n = Angle.Normalize(a);
                Assert.That(n > -DMath.Pi && n <= DMath.Pi, $"{a} → {n}");
            }
        }

        [Test]
        public void RngState_WrongLength_Throws() =>
            Assert.Throws<ArgumentException>(() => RngState.ReadFrom(new byte[RngState.ByteSize + 1]));

        [Test]
        public void Json_DeepNesting_Throws()
        {
            var deep = new string('[', 100) + new string(']', 100);
            Assert.Throws<JsonException>(() => JsonReader.Parse(deep));
            Assert.That(JsonReader.Parse(new string('[', 60) + new string(']', 60)), Is.Not.Null);
        }

        [Test]
        public void Tuning_DuplicateKeyInObject_Throws()
        {
            var reg = TuningLoader.ParseDefinitions(@"{""version"":1,""definitions"":[{""id"":""a"",""default"":0,""min"":0,""max"":1,""tag"":""[제안]""}]}");
            var e = Assert.Throws<TuningLoadException>(() =>
                TuningLoader.ParseBundle(reg, @"{""version"":1,""values"":[{""id"":""a"",""value"":0.1,""value"":0.2}]}"));
            StringAssert.Contains("value", e.Message);
        }

        [Test]
        public void CheckpointLog_ZeroInterval_Throws() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new CheckpointLog(0));

        [Test]
        public void Hasher_AddWithLength_SeparatesFields()
        {
            var a = StableHasher.Create();
            a.AddWithLength(new byte[] { 1, 2 });
            a.AddWithLength(new byte[] { 3 });
            var b = StableHasher.Create();
            b.AddWithLength(new byte[] { 1 });
            b.AddWithLength(new byte[] { 2, 3 });
            Assert.That(a.Value, Is.Not.EqualTo(b.Value));
        }

        [Test]
        public void FloatGuard_Message_InvariantCulture()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var log = new RoadToPro.Core.Logging.MemoryLogger(RoadToPro.Core.Logging.LogLevel.Trace);
                FloatGuard.Check(float.NaN, "x", GuardMode.Sanitize, log, 0.5f);
                StringAssert.Contains("0.5", log.Entries[0].Message);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prev;
            }
        }
    }
}
