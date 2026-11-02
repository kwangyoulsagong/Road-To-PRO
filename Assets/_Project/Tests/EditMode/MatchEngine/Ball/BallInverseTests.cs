using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-10: 역산 (FR-33·34). 검증은 실제 적분기(BallStepper)로 굴려서 한다.</summary>
    public class BallInverseTests
    {
        [TestCase(BallType.Modern, SurfaceCondition.Dry)]
        [TestCase(BallType.Panel32, SurfaceCondition.Dry)]
        [TestCase(BallType.Modern, SurfaceCondition.Wet)]
        [TestCase(BallType.Modern, SurfaceCondition.Flooded)]
        public void Ground_5to45m_ErrorWithin02(BallType type, SurfaceCondition surface)
        {
            var p = BallTestKit.Params(type, surface);
            for (float d = 5f; d <= 45f; d += 0.5f)
            {
                var r = BallInverse.GroundSpeedFor(p, d);
                Assert.IsTrue(r.Ok, $"{d} m 도달 불가");
                var (rest, _) = BallFlightKit.RollToRest(BallState.RollingAt(new Vec2(10f, 34f), new Vec2(r.Speed, 0f), p.Radius), p);
                Assert.AreEqual(d, rest.X - 10f, 0.2f, $"목표 {d} m, 초속 {r.Speed}");
            }
        }

        [Test]
        public void Ground_ResidualSpeed()
        {
            var p = BallTestKit.Params();
            foreach (var d in new[] { 8f, 20f, 35f })
            {
                var r = BallInverse.GroundSpeedFor(p, d, 3f);
                Assert.IsTrue(r.Ok);
                var s = BallState.RollingAt(new Vec2(10f, 34f), new Vec2(r.Speed, 0f), p.Radius);
                var ev = new BallEventBuffer();
                var prevX = s.Position.X;
                var prevV = r.Speed;
                for (int i = 0; i < 5000; i++)
                {
                    ev.Clear();
                    BallStepper.Step(ref s, p, ev, out _);
                    var v = s.Velocity.Length();
                    if (v <= 3f)
                    {
                        var f = (prevV - 3f) / (prevV - v);
                        var x = prevX + (s.Position.X - prevX) * f;
                        Assert.AreEqual(d, x - 10f, 0.2f);
                        break;
                    }
                    prevX = s.Position.X;
                    prevV = v;
                }
            }
        }

        [Test]
        public void Ground_Unreachable()
        {
            var p = BallTestKit.Params();
            Assert.AreEqual(BallInverseStatus.Unreachable, BallInverse.GroundSpeedFor(p, 500f).Status);
            Assert.AreEqual(BallInverseStatus.Unreachable, BallInverse.GroundSpeedFor(p, 0f).Status);
            Assert.AreEqual(BallInverseStatus.Unreachable, BallInverse.GroundSpeedFor(p, -3f).Status);
            Assert.AreEqual(BallInverseStatus.Unreachable, BallInverse.GroundSpeedFor(p, 10f, 41f).Status);
        }

        [TestCase(20f, 30f)]
        [TestCase(35f, 25f)]
        [TestCase(50f, 35f)]
        public void Lofted_LandsOnTarget(float d, float deg)
        {
            var p = BallTestKit.Params();
            var el = deg * DMath.Pi / 180f;
            var r = BallInverse.LoftedSpeedFor(p, d, el, Vec3.Zero, 100f, new BallEventBuffer());
            Assert.IsTrue(r.Ok, r.Status.ToString());
            var s = BallState.InAir(new Vec3(20f, 34f, p.Radius), BallFlightKit.Launch(r.Speed, deg), Vec3.Zero);
            var (land, _) = BallFlightKit.FlyToFirstBounce(s, p);
            Assert.AreEqual(d, land.X - 20f, 0.2f);
        }

        [Test]
        public void Lofted_UnreachableOrApexExceeded()
        {
            var p = BallTestKit.Params();
            Assert.AreEqual(BallInverseStatus.Unreachable, BallInverse.LoftedSpeedFor(p, 200f, 0.5f, Vec3.Zero, 100f, new BallEventBuffer()).Status);
            Assert.AreEqual(BallInverseStatus.ApexExceeded, BallInverse.LoftedSpeedFor(p, 40f, 1.0f, Vec3.Zero, 3f, new BallEventBuffer()).Status);
        }
    }
}
