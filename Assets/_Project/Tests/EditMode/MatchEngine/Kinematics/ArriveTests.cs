using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-05: Arrive 물리 기반 감속 (FR-20).</summary>
    public class ArriveTests
    {
        [Test]
        public void From8ms_DecelStartDistance_StopsWithinTolerance_OvershootBelow03()
        {
            var target = new Vec2(50f, 34f);
            var k = One(KinematicAttributes.Uniform(15), new Vec2(20f, 34f), new Vec2(8f, 0f), new Vec2(1f, 0f));
            k.Monitor = new MotionLimitMonitor();
            k.SetCommandImmediate(1, SteeringCommand.Arrive(target, 8f));
            float decelStart = -1f, maxX = 0f;
            int stoppedAt = -1;
            for (int i = 0; i < 1500; i++)
            {
                var v0 = k.State(0).Velocity.Length();
                k.Step();
                var s = k.State(0);
                if (decelStart < 0f && s.Velocity.Length() < v0 - 1e-4f) decelStart = target.X - s.Position.X;
                maxX = DMath.Max(maxX, s.Position.X);
                if (stoppedAt < 0 && s.Velocity.Length() <= 0.3f && (s.Position - target).Length() <= 0.3f) stoppedAt = i;
            }
            var aDec = Params.ArriveDecelFactor * k.Profile(0).MaxDecel;
            var expected = 64f / (2f * aDec);
            Assert.That(decelStart, Is.EqualTo(expected).Within(0.1f * expected), "감속 시작 거리 v²/(2·a_dec)");
            Assert.That(decelStart, Is.InRange(6f, 8f));
            Assert.That(stoppedAt, Is.GreaterThan(0), "목표 0.3 m 안에서 0.3 m/s 이하로 정지");
            Assert.That(maxX - target.X, Is.LessThanOrEqualTo(0.3f), "오버슈트");
            Assert.That(k.State(0).Velocity.Length(), Is.LessThanOrEqualTo(0.3f));
            Assert.That(k.Monitor.TotalViolations, Is.Zero);
        }

        [Test]
        public void ArriveFromSide_StopsNearTarget()
        {
            var target = new Vec2(60f, 40f);
            var k = One(KinematicAttributes.Uniform(12), new Vec2(40f, 20f), new Vec2(6f, 0f), new Vec2(1f, 0f));
            k.Monitor = new MotionLimitMonitor();
            k.SetCommandImmediate(1, SteeringCommand.Arrive(target));
            Run(k, 1500);
            Assert.That((k.State(0).Position - target).Length(), Is.LessThanOrEqualTo(0.3f));
            Assert.That(k.State(0).Velocity.Length(), Is.LessThanOrEqualTo(0.3f));
            Assert.That(k.Monitor.TotalViolations, Is.Zero);
        }
    }
}
