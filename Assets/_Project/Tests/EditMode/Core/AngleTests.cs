using NUnit.Framework;
using RoadToPro.Core.Math;

namespace RoadToPro.Tests.EditMode.Core
{
    public class AngleTests
    {
        const float Deg = DMath.Pi / 180f;

        [Test]
        public void Normalize_MinusPi_IsPi()
        {
            Assert.That(Angle.Normalize(-DMath.Pi), Is.EqualTo(DMath.Pi));
            Assert.That(Angle.Normalize(DMath.Pi), Is.EqualTo(DMath.Pi));
        }

        [Test]
        public void Normalize_WrapsIntoRange()
        {
            Assert.That(Angle.Normalize(3f * DMath.Pi), Is.EqualTo(DMath.Pi).Within(1e-5f));
            Assert.That(Angle.Normalize(270f * Deg), Is.EqualTo(-90f * Deg).Within(1e-5f));
            Assert.That(Angle.Normalize(0f), Is.EqualTo(0f));
            for (float a = -20f; a < 20f; a += 0.37f)
            {
                var n = Angle.Normalize(a);
                Assert.That(n > -DMath.Pi && n <= DMath.Pi, $"{a} → {n}");
            }
        }

        [Test]
        public void LerpShortest_179_Minus179_Mid_Is180()
        {
            var mid = Angle.LerpShortest(179f * Deg, -179f * Deg, 0.5f);
            Assert.That(DMath.Abs(mid), Is.EqualTo(DMath.Pi).Within(1e-5f));
        }

        [Test]
        public void Delta_ShortestSigned()
        {
            Assert.That(Angle.Delta(170f * Deg, -170f * Deg), Is.EqualTo(20f * Deg).Within(1e-5f));
            Assert.That(Angle.Delta(-170f * Deg, 170f * Deg), Is.EqualTo(-20f * Deg).Within(1e-5f));
        }

        [Test]
        public void LerpShortest_Opposite_GoesPositive()
        {
            // 정확히 180° 반대: 양의 방향으로 돈다 (규칙 고정)
            var mid = Angle.LerpShortest(0f, DMath.Pi, 0.5f);
            Assert.That(mid, Is.EqualTo(DMath.Pi / 2f).Within(1e-6f));
        }
    }
}
