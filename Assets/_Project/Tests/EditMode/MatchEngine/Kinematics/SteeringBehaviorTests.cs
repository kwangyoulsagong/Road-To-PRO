using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>FR-21~27 행동 기하·합성.</summary>
    public class SteeringBehaviorTests
    {
        [Test]
        public void PursuitLead_HeadOn_Shorter_CappedAt15()
        {
            var p = Params;
            var headOn = Steering.PursuitLead(new Vec2(0f, 0f), new Vec2(5f, 0f), new Vec2(10f, 0f), new Vec2(-5f, 0f), p);
            Assert.That(headOn, Is.EqualTo(1f).Within(1e-4f));
            var away = Steering.PursuitLead(new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(30f, 0f), new Vec2(8f, 0f), p);
            Assert.That(away, Is.EqualTo(p.PursueMaxT));
        }

        [Test]
        public void Pursue_CatchesSlowerTarget()
        {
            var k = new KinematicsSystem(Params, new[]
            {
                new PlayerSetup(1, Attr(18), new Vec2(20f, 30f)),
                new PlayerSetup(2, Attr(5), new Vec2(30f, 34f), new Vec2(4f, 0f)),
            });
            k.SetCommandImmediate(1, SteeringCommand.Pursue(2));
            k.SetCommandImmediate(2, SteeringCommand.Seek(new Vec2(100f, 34f), 5f));
            float best = float.MaxValue;
            for (int i = 0; i < 800; i++) { k.Step(); best = DMath.Min(best, (k.State(0).Position - k.State(1).Position).Length()); }
            Assert.That(best, Is.LessThan(1.0f));
        }

        [Test]
        public void Evade_IncreasesDistance()
        {
            var k = new KinematicsSystem(Params, new[]
            {
                new PlayerSetup(1, Attr(12), new Vec2(50f, 34f)),
                new PlayerSetup(2, Attr(12), new Vec2(45f, 34f), new Vec2(3f, 0f)),
            });
            k.SetCommandImmediate(1, SteeringCommand.Evade(2));
            k.SetCommandImmediate(2, SteeringCommand.Pursue(1, 3f));
            Run(k, 300);
            Assert.That((k.State(0).Position - k.State(1).Position).Length(), Is.GreaterThan(5f));
        }

        [Test]
        public void MarkPoint_OnLineToOwnGoal_1to2m()
        {
            var opp = new Vec2(30f, 20f);
            var goal = new Vec2(0f, 34f);
            var m = Steering.MarkPoint(opp, goal, Params);
            var d = (m - opp).Length();
            Assert.That(d, Is.InRange(1f, 2f));
            Assert.That(DMath.Abs(Vec2.Cross((goal - opp).Normalized(), (m - opp).Normalized())), Is.LessThan(1e-4f), "선 위");
            Assert.That(Vec2.Dot(goal - opp, m - opp), Is.GreaterThan(0f), "골 쪽");
        }

        [Test]
        public void Marker_FollowsMovingOpponent()
        {
            var goal = new Vec2(0f, 34f);
            var k = new KinematicsSystem(Params, new[]
            {
                new PlayerSetup(1, Attr(12), new Vec2(40f, 30f), new Vec2(0f, 3f)),
                new PlayerSetup(2, Attr(12), new Vec2(35f, 30f)),
            });
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(40f, 60f), 3f));
            k.SetCommandImmediate(2, SteeringCommand.Mark(1, goal));
            Run(k, 500);
            var point = Steering.MarkPoint(k.State(0).Position, goal, Params);
            Assert.That((k.State(1).Position - point).Length(), Is.LessThan(0.6f));
        }

        [Test]
        public void JockeyPoint_SideOffsetTowardWeakFoot()
        {
            var att = new Vec2(30f, 34f);
            var goal = new Vec2(0f, 34f);
            var left = Steering.JockeyPoint(att, goal, 1f, Params);
            var right = Steering.JockeyPoint(att, goal, -1f, Params);
            Assert.That(left.X, Is.EqualTo(28f).Within(1e-4f));
            Assert.That(left.Y - 34f, Is.EqualTo(-Params.JockeySideOffset).Within(1e-4f), "골 방향(−x)의 왼쪽 = −y");
            Assert.That(right.Y - 34f, Is.EqualTo(Params.JockeySideOffset).Within(1e-4f));
        }

        [Test]
        public void Combiner_SeparationFirst_ThenBehavior_ThenFormation()
        {
            var p = Params;
            var r = SteeringCombiner.Combine(new Vec2(0f, 3f), new Vec2(9f, 0f), true, new Vec2(0f, -5f), 5f, p);
            Assert.That(r.Length(), Is.LessThanOrEqualTo(5f + 1e-4f));
            Assert.That(r.Y, Is.EqualTo(3f).Within(1e-4f), "충돌 회피 먼저");
            Assert.That(r.X, Is.EqualTo(2f).Within(1e-4f), "남은 예산만큼 행동");
            var f = SteeringCombiner.Combine(Vec2.Zero, Vec2.Zero, true, new Vec2(4f, 0f), 9f, p);
            Assert.That(f.X, Is.EqualTo(4f * p.WFormation).Within(1e-4f), "행동이 없으면 대형 추종");
        }
    }
}
