using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-03·04: 스핀 0 비행 (리서치 01 A-7, 32패널).</summary>
    public class BallFlightTests
    {
        static readonly Vec2 Origin = new Vec2(20f, 34f);

        static (float range, float time) Fly(BallParams p, float v0, float deg)
        {
            var s = BallState.InAir(new Vec3(Origin, p.Radius), BallFlightKit.Launch(v0, deg), Vec3.Zero);
            var (land, t) = BallFlightKit.FlyToFirstBounce(s, p);
            Assert.Greater(t, 0f, "착지하지 않음");
            return (land.X - Origin.X, t);
        }

        [TestCase(18f, 15f, 14.8f, 0.92f)]
        [TestCase(25f, 30f, 39.8f, 2.32f)]
        [TestCase(30f, 30f, 51.6f, 2.70f)]
        public void NoSpin_RangeAndFlightTime_MatchA7(float v0, float deg, float range, float time)
        {
            var (r, t) = Fly(BallTestKit.Params(BallType.Panel32), v0, deg);
            Assert.AreEqual(range, r, range * 0.03f, $"사거리 {r}");
            Assert.AreEqual(time, t, time * 0.03f, $"체공 {t}");
        }

        [TestCase(18f, 15f, 16.5f)]
        [TestCase(25f, 30f, 55.2f)]
        [TestCase(30f, 30f, 79.5f)]
        public void ZeroDrag_MatchesVacuumRange(float v0, float deg, float vacuum)
        {
            var p = BallTestKit.WithValues(@"{""version"":1,""values"":[{""id"":""ball.drag.panel32.cdLow"",""value"":0},{""id"":""ball.drag.panel32.cdHigh"",""value"":0}]}");
            var (r, _) = Fly(p, v0, deg);
            Assert.AreEqual(vacuum, r, vacuum * 0.01f, $"사거리 {r}");
        }
    }
}
