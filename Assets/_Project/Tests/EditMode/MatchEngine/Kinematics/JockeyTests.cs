using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-07: 저킹 (FR-26, 리서치 02 §3.4).</summary>
    public class JockeyTests
    {
        static readonly Vec2 Goal = new Vec2(0f, 34f);

        [TestCase(2f, 1f)]
        [TestCase(3f, -1f)]
        [TestCase(3.8f, 1f)]
        public void Defender_KeepsDistance15to25_BackpedalBelow4(float dribbleSpeed, float side)
        {
            var a = KinematicAttributes.Uniform(12);
            var k = new KinematicsSystem(Params, new[]
            {
                new PlayerSetup(1, a, new Vec2(70f, 34f), new Vec2(-dribbleSpeed, 0f)),
                new PlayerSetup(2, a, new Vec2(68f, 34f), new Vec2(-dribbleSpeed, 0f)),
            });
            k.Monitor = new MotionLimitMonitor();
            k.SetCommandImmediate(1, SteeringCommand.Seek(Goal, dribbleSpeed));
            k.SetCommandImmediate(2, SteeringCommand.Jockey(1, Goal, side));
            float dMin = float.MaxValue, dMax = 0f, backpedal = 0f;
            for (int i = 0; i < 1200; i++)
            {
                if (i % 10 == 0)
                {
                    k.SetCommand(1, SteeringCommand.Seek(Goal, dribbleSpeed));
                    k.SetCommand(2, SteeringCommand.Jockey(1, Goal, side));
                }
                k.Step();
                var d = (k.State(0).Position - k.State(1).Position).Length();
                dMin = DMath.Min(dMin, d);
                dMax = DMath.Max(dMax, d);
                backpedal = DMath.Max(backpedal, Vec2.Dot(k.State(1).Velocity, (Goal - k.State(0).Position).Normalized()));
            }
            Assert.That(dMin, Is.GreaterThanOrEqualTo(1.5f));
            Assert.That(dMax, Is.LessThanOrEqualTo(2.5f));
            Assert.That(backpedal, Is.LessThanOrEqualTo(4f + 1e-3f), "백페달 최대 4 m/s");
            Assert.That(k.Monitor.TotalViolations, Is.Zero);
        }

        [Test]
        public void FastAttacker_BackpedalCappedAt4()
        {
            var a = KinematicAttributes.Uniform(12);
            var k = new KinematicsSystem(Params, new[]
            {
                new PlayerSetup(1, a, new Vec2(70f, 34f), new Vec2(-6f, 0f)),
                new PlayerSetup(2, a, new Vec2(68f, 34f), new Vec2(-4f, 0f)),
            });
            k.SetCommandImmediate(1, SteeringCommand.Seek(Goal, 6f));
            k.SetCommandImmediate(2, SteeringCommand.Jockey(1, Goal, 0f));
            float backpedal = 0f;
            for (int i = 0; i < 200; i++)
            {
                k.Step();
                backpedal = DMath.Max(backpedal, -k.State(1).Velocity.X);
            }
            Assert.That(backpedal, Is.LessThanOrEqualTo(4f + 1e-3f));
        }

        [Test]
        public void Approach_LimitedTo09OfAttackerSpeed()
        {
            // 공격수가 2 m/s로 오고 수비수는 6 m 떨어져 있으면 지점까지 '접근'해야 한다: 공격수 쪽 속도 성분 ≤ max(0.9·2, 하한)
            var a = KinematicAttributes.Uniform(12);
            var k = new KinematicsSystem(Params, new[]
            {
                new PlayerSetup(1, a, new Vec2(70f, 34f), new Vec2(-2f, 0f)),
                new PlayerSetup(2, a, new Vec2(60f, 34f)),
            });
            k.SetCommandImmediate(1, SteeringCommand.Seek(Goal, 2f));
            k.SetCommandImmediate(2, SteeringCommand.Jockey(1, Goal, 0f));
            var cap = DMath.Max(2f * Params.JockeySpeedRatio, Params.JockeyMinApproach);
            for (int i = 0; i < 300; i++)
            {
                k.Step();
                Assert.That(k.State(1).Velocity.X, Is.LessThanOrEqualTo(cap + 1e-3f), "접근 속도 상한");
            }
        }
    }
}
