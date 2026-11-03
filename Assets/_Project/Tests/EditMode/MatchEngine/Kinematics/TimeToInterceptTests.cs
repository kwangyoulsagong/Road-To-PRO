using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>FR-40·41 도달 시간.</summary>
    public class TimeToInterceptTests
    {
        [Test]
        public void Stationary_FarTarget_MatchesAsymptote()
        {
            var p = Params;
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(15), p);
            var t = TimeToIntercept.Estimate(pr, new Vec2(10f, 34f), Vec2.Zero, 1f, 0f, p, new Vec2(60f, 34f));
            Assert.That(t, Is.EqualTo(pr.ReactionDelay + 50f / pr.MaxSpeed + pr.Tau).Within(0.01f));
        }

        [Test]
        public void Stationary_MatchesSimulatedRun()
        {
            // TTI = 반응 지연 + 실제 직선 주행 시간 (같은 F-V 모델)
            var p = Params;
            foreach (var d in new[] { 2f, 5f, 10f, 25f })
            {
                var k = One(KinematicAttributes.Uniform(13), new Vec2(10f, 34f), default, new Vec2(1f, 0f));
                k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 34f)));
                int i = 0;
                while (k.State(0).Position.X < 10f + d) { k.Step(); i++; }
                var tti = TimeToIntercept.Estimate(k.Profile(0), new Vec2(10f, 34f), Vec2.Zero, 1f, 0f, p, new Vec2(10f + d, 34f));
                Assert.That(tti - k.Profile(0).ReactionDelay, Is.EqualTo(i * p.Dt).Within(0.03f), $"{d} m");
            }
        }

        [Test]
        public void MovingToward_FasterThanStationary_MovingAway_Slower()
        {
            var p = Params;
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(12), p);
            var target = new Vec2(40f, 34f);
            var still = TimeToIntercept.Estimate(pr, new Vec2(20f, 34f), Vec2.Zero, 1f, 0f, p, target);
            var toward = TimeToIntercept.Estimate(pr, new Vec2(20f, 34f), new Vec2(5f, 0f), 1f, 0f, p, target);
            var away = TimeToIntercept.Estimate(pr, new Vec2(20f, 34f), new Vec2(-5f, 0f), 1f, 0f, p, target);
            var side = TimeToIntercept.Estimate(pr, new Vec2(20f, 34f), new Vec2(0f, 5f), 1f, 0f, p, target);
            Assert.That(toward, Is.LessThan(still));
            Assert.That(away, Is.GreaterThan(still));
            Assert.That(side, Is.GreaterThan(still));
        }

        [Test]
        public void Fatigue_IncreasesTti()
        {
            var p = Params;
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(12), p);
            var fresh = TimeToIntercept.Estimate(pr, new Vec2(20f, 34f), Vec2.Zero, 1f, 0f, p, new Vec2(50f, 34f));
            var tired = TimeToIntercept.Estimate(pr, new Vec2(20f, 34f), Vec2.Zero, 0.2f, 0.9f, p, new Vec2(50f, 34f));
            Assert.That(tired, Is.GreaterThan(fresh));
        }

        [Test]
        public void ZeroDistance_ReturnsReactionDelay()
        {
            var p = Params;
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(12), p);
            Assert.That(TimeToIntercept.Estimate(pr, new Vec2(20f, 34f), Vec2.Zero, 1f, 0f, p, new Vec2(20f, 34f)), Is.EqualTo(pr.ReactionDelay));
        }
    }
}
