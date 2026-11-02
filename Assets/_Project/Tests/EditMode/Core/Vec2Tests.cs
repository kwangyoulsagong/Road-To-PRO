using NUnit.Framework;
using RoadToPro.Core.Math;

namespace RoadToPro.Tests.EditMode.Core
{
    public class Vec2Tests
    {
        [Test]
        public void Normalize_Zero_ReturnsZeroNotNaN()
        {
            var n = Vec2.Zero.Normalized();
            Assert.That(n, Is.EqualTo(Vec2.Zero));
            Assert.That(float.IsNaN(n.X) || float.IsNaN(n.Y), Is.False);
        }

        [Test]
        public void Normalize_Tiny_ReturnsZero()
        {
            Assert.That(new Vec2(1e-7f, 0f).Normalized(), Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void Normalize_Regular_UnitLength()
        {
            var n = new Vec2(3f, 4f).Normalized();
            Assert.That(n.X, Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(n.Y, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(n.Length(), Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void Operators_DotCross_Distance()
        {
            var a = new Vec2(1f, 2f);
            var b = new Vec2(3f, -1f);
            Assert.That(a + b, Is.EqualTo(new Vec2(4f, 1f)));
            Assert.That(a - b, Is.EqualTo(new Vec2(-2f, 3f)));
            Assert.That(a * 2f, Is.EqualTo(new Vec2(2f, 4f)));
            Assert.That(2f * a, Is.EqualTo(new Vec2(2f, 4f)));
            Assert.That(a / 2f, Is.EqualTo(new Vec2(0.5f, 1f)));
            Assert.That(-a, Is.EqualTo(new Vec2(-1f, -2f)));
            Assert.That(Vec2.Dot(a, b), Is.EqualTo(1f));
            Assert.That(Vec2.Cross(a, b), Is.EqualTo(-7f));
            Assert.That(Vec2.Distance(new Vec2(0, 0), new Vec2(3, 4)), Is.EqualTo(5f));
            Assert.That(Vec2.DistanceSq(new Vec2(0, 0), new Vec2(3, 4)), Is.EqualTo(25f));
            Assert.That(new Vec2(3, 4).LengthSq(), Is.EqualTo(25f));
        }

        [Test]
        public void Project_And_Rotate()
        {
            var p = Vec2.Project(new Vec2(2f, 3f), new Vec2(1f, 0f));
            Assert.That(p, Is.EqualTo(new Vec2(2f, 0f)));
            Assert.That(Vec2.Project(new Vec2(2f, 3f), Vec2.Zero), Is.EqualTo(Vec2.Zero));
            var r = new Vec2(1f, 0f).Rotate(DMath.Pi / 2f);
            Assert.That(r.X, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(r.Y, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void Length_LargeValues_NoOverflow()
        {
            var v = new Vec2(3e19f, 4e19f);
            Assert.That(v.Length(), Is.EqualTo(5e19f).Within(1e14f));
            Assert.That(float.IsInfinity(v.Length()), Is.False);
        }
    }
}
