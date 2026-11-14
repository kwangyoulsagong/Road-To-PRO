using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 T05: 슛 의도(셀·파워·xG) → 예약 킥 → 접촉 서브스텝 실행. 결정 RNG 소비 없음.</summary>
    public class ShotStageTests
    {
        static (DuelFixture f, int shooter, Vec2 goal) Setup(Vec2 at)
        {
            var f = new DuelFixture(7171UL);
            var ctx = f.Ctx;
            var dir = ctx.AttackDir(0);
            var goal = ctx.OpponentGoal(0);
            var pos = new Vec2(dir > 0 ? at.X : ctx.Pitch.LengthM - at.X, at.Y);
            f.Put(9, pos, new Vec2(dir, 0f));
            f.Put(11, new Vec2(goal.X - dir * 0.5f, goal.Y), new Vec2(-dir, 0f));
            f.Own(9);
            return (f, 9, goal);
        }

        static SimEvent? FirstShot(DuelFixture f, int maxTicks = 20)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                f.Tick();
                foreach (var e in f.Events) if (e.Kind == SimEventKind.Shot) return e;
            }
            return null;
        }

        [Test]
        public void Plan_NoDecisionRng_ExecutesWithCellAndXg()
        {
            var (f, s, goal) = Setup(new Vec2(90f, 30f));
            var ctx = f.Ctx;
            var before = ctx.DecisionRng.SaveState();
            var a = ctx.Actions.Shots.Plan(ctx, s, 0, -1f, false, false, BodyPart.RightFoot);
            Assert.AreEqual(before, ctx.DecisionRng.SaveState(), "결정 RNG 소비 없음 (대체 ShootWithXg 3회 제거)");
            Assert.IsTrue(a.HasShot);
            Assert.That(a.ShotCell, Is.InRange(0, ShotAim.Cells - 1));
            Assert.That(a.ShotXg, Is.InRange(0.001f, 0.999f));
            Assert.That(a.ShotPower, Is.InRange(0f, 1f));
            Assert.IsTrue(ctx.Actions.Schedule(ctx, in a, ctx.Kinematics.Substep));
            var e = FirstShot(f);
            Assert.IsTrue(e.HasValue, "슛 실행");
            Assert.AreEqual(a.ShotXg, e.Value.F1, 1e-6f, "Shot F1 = 슈팅 순간 xG");
            Assert.AreEqual(a.ShotCell + 1, e.Value.B);
            Assert.AreNotEqual(0, e.Value.A & ActionSystem.ShotFlagPlanned);
            Assert.That(e.Value.F0, Is.InRange(17.9f, 32.1f), "v_shot 18~32 m/s");
            Assert.IsTrue(ctx.Actions.Shots.InFlight || ctx.Ball.Phase == BallPhase.Controlled, "비행 시작 (이미 잡혔으면 컨트롤)");
        }

        [Test]
        public void Xg_SameAsModel_DecisionAndActionWorld()
        {
            var (f, s, goal) = Setup(new Vec2(92f, 36f));
            var ctx = f.Ctx;
            var xg = ctx.Actions.Shots.Xg(ctx, s, ctx.Ball.Position.XY, 0f);
            var w = ctx.Actions.World;
            var c = ShotContextBuilder.Build(ctx.Actions.P, w.Pos, w.Active, goal, ctx.AttackDir(0), 1, ctx.Ball.Position.XY, 0f);
            Assert.AreEqual(ShotModel.Xg(ctx.Actions.P, in c), xg, 0f);
        }

        [Test]
        [Category("Slow")]
        public void Match20min_ShotsArePlanned_XgOnEvent()
        {
            var sim = ShapeTestKit.Sim();
            sim.StepTicks(12_000);
            var ctx = sim.Context;
            int shots = 0, planned = 0;
            for (int i = 0; i < ctx.Log.Count; i++)
            {
                var e = ctx.Log[i];
                if (e.Kind != SimEventKind.Shot) continue;
                shots++;
                if ((e.A & ActionSystem.ShotFlagPlanned) == 0) continue;
                planned++;
                Assert.That(e.F1, Is.InRange(0f, 1f));
                Assert.That(e.B, Is.InRange(1, ShotAim.Cells));
            }
            TestContext.WriteLine($"슛 {shots}, 071 조준 {planned}, 블록 {ctx.Actions.Shots.Blocks}");
            Assert.Greater(shots, 0);
            Assert.AreEqual(shots, planned, "010 파이프라인 슛은 전부 071 경로");
        }
    }
}
