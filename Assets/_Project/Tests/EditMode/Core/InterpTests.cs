using NUnit.Framework;
using RoadToPro.Core.Math;

namespace RoadToPro.Tests.EditMode.Core
{
    public class InterpTests
    {
        [Test]
        public void Lerp_InverseLerp_Clamp()
        {
            Assert.That(Interp.Lerp(2f, 4f, 0.5f), Is.EqualTo(3f));
            Assert.That(Interp.Lerp(2f, 4f, 0f), Is.EqualTo(2f));
            Assert.That(Interp.Lerp(2f, 4f, 1f), Is.EqualTo(4f));
            Assert.That(Interp.InverseLerp(2f, 4f, 3f), Is.EqualTo(0.5f));
            Assert.That(Interp.InverseLerp(2f, 2f, 3f), Is.EqualTo(0f));
            Assert.That(Interp.Clamp(5f, 0f, 1f), Is.EqualTo(1f));
            Assert.That(Interp.Clamp(-5f, 0f, 1f), Is.EqualTo(0f));
            Assert.That(Interp.Clamp01(0.3f), Is.EqualTo(0.3f));
            Assert.That(Interp.Clamp(5, 0, 3), Is.EqualTo(3));
        }

        [Test]
        public void Smoothstep_Sigmoid()
        {
            Assert.That(Interp.Smoothstep(0f, 1f, -1f), Is.EqualTo(0f));
            Assert.That(Interp.Smoothstep(0f, 1f, 2f), Is.EqualTo(1f));
            Assert.That(Interp.Smoothstep(0f, 1f, 0.5f), Is.EqualTo(0.5f));
            Assert.That(Interp.Sigmoid(0f), Is.EqualTo(0.5f));
            Assert.That(Interp.Sigmoid(100f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(Interp.Sigmoid(-100f), Is.EqualTo(0f).Within(1e-6f));
        }
    }
}
