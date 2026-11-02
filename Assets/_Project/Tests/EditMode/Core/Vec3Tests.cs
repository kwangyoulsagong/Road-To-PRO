using NUnit.Framework;
using RoadToPro.Core.Math;

namespace RoadToPro.Tests.EditMode.Core
{
    public class Vec3Tests
    {
        [Test]
        public void Normalize_Zero_ReturnsZero()
        {
            Assert.That(Vec3.Zero.Normalized(), Is.EqualTo(Vec3.Zero));
        }

        [Test]
        public void Cross_XY_IsZ()
        {
            Assert.That(Vec3.Cross(new Vec3(1, 0, 0), new Vec3(0, 1, 0)), Is.EqualTo(new Vec3(0, 0, 1)));
        }

        [Test]
        public void Basic_Ops()
        {
            var a = new Vec3(1, 2, 2);
            Assert.That(a.Length(), Is.EqualTo(3f));
            Assert.That(a.LengthSq(), Is.EqualTo(9f));
            Assert.That(Vec3.Dot(a, new Vec3(1, 1, 1)), Is.EqualTo(5f));
            Assert.That(a + a, Is.EqualTo(new Vec3(2, 4, 4)));
            Assert.That(a - a, Is.EqualTo(Vec3.Zero));
            Assert.That(a * 2f, Is.EqualTo(new Vec3(2, 4, 4)));
            Assert.That(a.XY, Is.EqualTo(new Vec2(1, 2)));
            Assert.That(Vec3.Distance(a, Vec3.Zero), Is.EqualTo(3f));
            Assert.That(Vec3.Project(new Vec3(2, 3, 4), new Vec3(0, 0, 2)), Is.EqualTo(new Vec3(0, 0, 4)));
            var n = a.Normalized();
            Assert.That(n.Length(), Is.EqualTo(1f).Within(1e-6f));
        }
    }
}
