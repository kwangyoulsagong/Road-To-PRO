using NUnit.Framework;
using RoadToPro.Core.Math;

namespace RoadToPro.Tests.EditMode.Core
{
    public class DMathTests
    {
        [Test]
        public void Basic_Functions_MatchExpected()
        {
            Assert.That(DMath.Sqrt(9f), Is.EqualTo(3f));
            Assert.That(DMath.Sin(0f), Is.EqualTo(0f));
            Assert.That(DMath.Cos(0f), Is.EqualTo(1f));
            Assert.That(DMath.Atan2(1f, 1f), Is.EqualTo(DMath.Pi / 4f).Within(1e-6f));
            Assert.That(DMath.Exp(0f), Is.EqualTo(1f));
            Assert.That(DMath.Log(1f), Is.EqualTo(0f));
            Assert.That(DMath.Pow(2f, 10f), Is.EqualTo(1024f));
            Assert.That(DMath.Abs(-2f), Is.EqualTo(2f));
            Assert.That(DMath.Floor(-1.5f), Is.EqualTo(-2f));
        }

        [Test]
        public void Atan2_ZeroZero_IsZero()
        {
            Assert.That(DMath.Atan2(0f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void Sqrt_Negative_StrictThrows()
        {
            // AC-19: 디버그(에디터) 빌드에서는 Strict가 기본
            Assume.That(FloatGuard.DefaultMode, Is.EqualTo(GuardMode.Strict));
            Assert.Throws<NumericException>(() => DMath.Sqrt(-1f));
            Assert.Throws<NumericException>(() => DMath.Log(-1f));
        }
    }
}
