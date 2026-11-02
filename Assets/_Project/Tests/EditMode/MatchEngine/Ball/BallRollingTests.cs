using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-01·02: 구름 정지거리·시간 (리서치 01 A-6 표, 32패널·건조).</summary>
    public class BallRollingTests
    {
        [TestCase(3.2f, 7.2f, 5.2f)]
        [TestCase(5f, 13.2f, 6.7f)]
        [TestCase(8f, 22.6f, 8.1f)]
        [TestCase(10f, 28.5f, 8.8f)]
        [TestCase(12f, 34.5f, 9.4f)]
        [TestCase(15f, 44.4f, 10.1f)]
        [TestCase(20f, 60.3f, 11.0f)]
        public void StopDistanceAndTime_MatchA6Table(float v0, float dist, float time)
        {
            var p = BallTestKit.Params(BallType.Panel32);
            var start = new Vec2(10f, 34f);
            var (rest, t) = BallFlightKit.RollToRest(BallState.RollingAt(start, new Vec2(v0, 0f), p.Radius), p);
            Assert.Greater(t, 0f, "정지하지 않음");
            var d = rest.X - start.X;
            Assert.AreEqual(dist, d, dist * 0.03f, $"거리 {d}");
            Assert.AreEqual(time, t, time * 0.05f, $"시간 {t}");
            Assert.AreEqual(34f, rest.Y, 1e-4f);
        }

        [Test]
        public void Roll32_WithinUefaFifaRange()
        {
            var p = BallTestKit.Params(BallType.Panel32);
            var (rest, _) = BallFlightKit.RollToRest(BallState.RollingAt(new Vec2(10f, 34f), new Vec2(3.2f, 0f), p.Radius), p);
            var d = rest.X - 10f;
            Assert.That(d, Is.InRange(5f, 10.6f));
        }

        [Test]
        public void Rolling_NeverReversesDirection()
        {
            var p = BallTestKit.Params(BallType.Panel32, SurfaceCondition.Flooded);
            var s = BallState.RollingAt(new Vec2(10f, 34f), new Vec2(0.3f, 0f), p.Radius);
            var ev = new BallEventBuffer();
            var lastX = s.Position.X;
            for (int i = 0; i < 100 && s.Phase == BallPhase.Rolling; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                Assert.GreaterOrEqual(s.Position.X, lastX);
                lastX = s.Position.X;
            }
            Assert.AreEqual(BallPhase.Rest, s.Phase);
        }

        [Test]
        public void WetShortGrass_RollsFurther_FloodedShorter()
        {
            float Dist(SurfaceCondition c)
            {
                var p = BallTestKit.Params(BallType.Panel32, c);
                return BallFlightKit.RollToRest(BallState.RollingAt(new Vec2(10f, 34f), new Vec2(10f, 0f), p.Radius), p).rest.X;
            }
            Assert.Greater(Dist(SurfaceCondition.Wet), Dist(SurfaceCondition.Dry));
            Assert.Less(Dist(SurfaceCondition.Flooded), Dist(SurfaceCondition.Dry));
            Assert.Less(Dist(SurfaceCondition.LongGrass), Dist(SurfaceCondition.Dry));
        }
    }
}
