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

        // ── specs/087 FR-02 (AC-01): 이미 공을 쫓는 소유자의 도달 시간 = 반응 시간 없이 현재 위치·속도에서 ──

        [Test]
        public void EstimateNoReaction_Stationary_EqualsEstimateMinusReaction()
        {
            var p = Params;
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(12), p);
            var k = new TimeToIntercept.Context(pr, new Vec2(20f, 34f), Vec2.Zero, 1f, 0f, p);
            foreach (var d in new[] { 0.5f, 2f, 6f })
                Assert.That(TimeToIntercept.EstimateNoReaction(k, 20f + d, 34f), Is.EqualTo(TimeToIntercept.Estimate(k, 20f + d, 34f) - k.Reaction).Within(1e-4f), $"{d} m");
            Assert.AreEqual(0f, TimeToIntercept.EstimateNoReaction(k, 20f, 34f), "제자리 = 0");
        }

        [Test]
        public void EstimateNoReaction_Moving_NoExtrapolation_FasterThanEstimate()
        {
            // 달리는 선수: Estimate는 반응 시간 동안 현재 속도로 외삽한 점에서 출발 + 반응. 쫓는 소유자는 현재 위치에서 반응 0
            var p = Params;
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(12), p);
            var k = new TimeToIntercept.Context(pr, new Vec2(20f, 34f), new Vec2(6f, 0f), 1f, 0f, p);
            var ahead = TimeToIntercept.EstimateNoReaction(k, 23f, 34f);
            Assert.That(ahead, Is.LessThan(TimeToIntercept.Estimate(k, 23f, 34f)));
            Assert.That(ahead, Is.EqualTo(TimeToIntercept.RunTime(3f, 6f, pr.MaxSpeed, pr.Tau)).Within(1e-3f), "앞쪽 공 = 같은 방향 주행 시간만");
            // 외삽 점이 공을 지나치는 경우(공이 외삽 점보다 가까움)도 현재 위치 기준이라 되돌아오지 않는다
            Assert.That(TimeToIntercept.EstimateNoReaction(k, 21f, 34f), Is.LessThan(0.5f));
        }
    }
}
