using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-05·06: 마그누스.</summary>
    public class BallMagnusTests
    {
        /// <summary>x 방향으로 날려 x가 start+dist에 닿거나 착지할 때의 위치.</summary>
        static Vec3 FlyUntilX(BallParams p, float v0, float deg, Vec3 spin, float dist)
        {
            var start = new Vec3(30f, 34f, p.Radius);
            var s = BallState.InAir(start, BallFlightKit.Launch(v0, deg), spin);
            var ev = new BallEventBuffer();
            for (int i = 0; i < 1000 && s.Phase == BallPhase.Air && s.Position.X < start.X + dist; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
            }
            return s.Position - start;
        }

        [Test]
        public void Curl_DirectionFollowsRightHandRule()
        {
            var p = BallTestKit.Params();
            // ω = +z, v = +x → ω̂×v̂ = +y
            var left = FlyUntilX(p, 18.5f, 20f, new Vec3(0f, 0f, 64f), 20f);
            var right = FlyUntilX(p, 18.5f, 20f, new Vec3(0f, 0f, -64f), 20f);
            Assert.Greater(left.Y, 0f);
            Assert.Less(right.Y, 0f);
            Assert.AreEqual(left.Y, -right.Y, 1e-3f, "대칭");
            // 백스핀(ω = −y, v = +x → ω̂×v̂ = +z)은 더 멀리 뜬다
            var none = FlyUntilX(p, 18.5f, 20f, Vec3.Zero, 100f);
            var back = FlyUntilX(p, 18.5f, 20f, new Vec3(0f, -64f, 0f), 100f);
            Assert.Greater(back.X, none.X);
        }

        [TestCase(BallType.Panel32)]
        [TestCase(BallType.Modern)]
        public void FreeKick36ms_Lateral2to6m(BallType type)
        {
            var d = FlyUntilX(BallTestKit.Params(type), 36f, 12f, new Vec3(0f, 0f, 63f), 27f);
            Assert.GreaterOrEqual(d.X, 27f, "27 m 전에 착지");
            Assert.That(d.Y, Is.InRange(2f, 6f));
        }

        [Test]
        public void Below8ms_MagnusZero()
        {
            var p = BallTestKit.Params();
            var a = BallAero.AirAcceleration(new Vec3(7.99f, 0f, 0f), new Vec3(0f, 0f, 150f), p);
            Assert.AreEqual(0f, a.Y);
            var noSpin = BallAero.AirAcceleration(new Vec3(7.99f, 0f, 0f), Vec3.Zero, p);
            Assert.AreEqual(noSpin, a);
        }

        [Test]
        public void Fade_HalfAtMidWindow_FullAbove()
        {
            var p = BallTestKit.Params();
            Assert.AreEqual(0.5f, BallAero.MagnusFade(p.MagnusMinSpeed + p.MagnusFadeWidth * 0.5f, p), 1e-5f);
            Assert.AreEqual(1f, BallAero.MagnusFade(p.MagnusMinSpeed + p.MagnusFadeWidth, p));
            Assert.AreEqual(0f, BallAero.MagnusFade(p.MagnusMinSpeed - 0.01f, p));
        }

        [Test]
        public void Cd_DragCrisisShape()
        {
            var p = BallTestKit.Params(BallType.Panel32);
            Assert.AreEqual(0.47f, BallAero.Cd(0f, p), 0.002f);
            Assert.AreEqual((0.47f + 0.20f) / 2f, BallAero.Cd(11f, p), 1e-5f);
            Assert.AreEqual(0.20f, BallAero.Cd(40f, p), 1e-4f);
            Assert.AreEqual(0.20f, BallAero.Cd(1e6f, p));
        }

        [Test]
        public void SpinParameterCurve()
        {
            var p = BallTestKit.Params();
            Assert.AreEqual(0f, BallAero.ClOf(0f, p));
            Assert.AreEqual(0.35f * (1f - System.MathF.Exp(-0.38f / 0.15f)), BallAero.ClOf(0.38f, p), 1e-6f);
        }
    }
}
