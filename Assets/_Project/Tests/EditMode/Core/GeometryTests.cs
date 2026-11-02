using NUnit.Framework;
using RoadToPro.Core.Math;

namespace RoadToPro.Tests.EditMode.Core
{
    public class GeometryTests
    {
        static readonly Vec2 C = Vec2.Zero;

        [Test]
        public void Swept_ThroughCenter_ExactT()
        {
            // 0.3 m 선분이 반지름 0.5 원 중심 방향으로: x=-0.7 → -0.4, 접촉은 x=-0.5 → t = 0.2/0.3
            Assert.That(Geometry.SweptSegmentCircle(new Vec2(-0.7f, 0f), new Vec2(-0.4f, 0f), C, 0.5f, out var t), Is.True);
            Assert.That(t, Is.EqualTo(2f / 3f).Within(1e-5f));
        }

        [Test]
        public void Swept_Miss06_NoContact()
        {
            Assert.That(Geometry.SweptSegmentCircle(new Vec2(-0.15f, 1.1f), new Vec2(0.15f, 1.1f), C, 0.5f, out _), Is.False);
        }

        [Test]
        public void Swept_StartInside_T0()
        {
            Assert.That(Geometry.SweptSegmentCircle(new Vec2(0.1f, 0f), new Vec2(0.4f, 0f), C, 0.5f, out var t), Is.True);
            Assert.That(t, Is.EqualTo(0f));
        }

        [Test]
        public void Swept_ZeroLength_Outside_NoContact()
        {
            Assert.That(Geometry.SweptSegmentCircle(new Vec2(1f, 0f), new Vec2(1f, 0f), C, 0.5f, out _), Is.False);
        }

        [Test]
        public void Swept_Tangent_Contacts()
        {
            Assert.That(Geometry.SweptSegmentCircle(new Vec2(-1f, 0.5f), new Vec2(1f, 0.5f), C, 0.5f, out var t), Is.True);
            Assert.That(t, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Swept_RadiusZero_OnlyExactHit()
        {
            Assert.That(Geometry.SweptSegmentCircle(new Vec2(-1f, 0f), new Vec2(1f, 0f), C, 0f, out var t), Is.True);
            Assert.That(t, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(Geometry.SweptSegmentCircle(new Vec2(-1f, 0.01f), new Vec2(1f, 0.01f), C, 0f, out _), Is.False);
        }

        [Test]
        public void Swept_EndsBeforeCircle_NoContact()
        {
            Assert.That(Geometry.SweptSegmentCircle(new Vec2(-1f, 0f), new Vec2(-0.6f, 0f), C, 0.5f, out _), Is.False);
        }
    }
}
