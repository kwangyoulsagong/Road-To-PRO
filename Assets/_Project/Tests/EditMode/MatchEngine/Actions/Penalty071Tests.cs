using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 FR-11·17 · AC-03: 페널티 xG 고정·키커 방향 혼합전략.</summary>
    public class Penalty071Tests
    {
        static DuelFixture Spot(out int dir)
        {
            var f = new DuelFixture(7173UL);
            var ctx = f.Ctx;
            dir = ctx.AttackDir(0);
            var goal = ctx.OpponentGoal(0);
            f.Put(9, new Vec2(goal.X - dir * 11.5f, goal.Y), new Vec2(dir, 0f));
            f.Put(11, new Vec2(goal.X - dir * 0.3f, goal.Y), new Vec2(-dir, 0f));
            f.Own(9);
            return f;
        }

        [Test]
        public void PenaltyXg_Fixed078_EqualsShotEventF1()
        {
            var f = Spot(out _);
            var ctx = f.Ctx;
            var a = ctx.Actions.Shots.Plan(ctx, 9, 0, -1f, true, true, BodyPart.RightFoot);
            Assert.AreEqual(0.78f, a.ShotXg, 1e-6f);
            Assert.AreEqual(-1, a.ShotCell, "셀은 실행 때 (방향 혼합전략)");
            Assert.IsTrue(ctx.Actions.Schedule(ctx, in a, ctx.Kinematics.Substep));
            SimEvent? shot = null;
            for (int i = 0; i < 20 && shot == null; i++) { f.Tick(); foreach (var e in f.Events) if (e.Kind == SimEventKind.Shot) shot = e; }
            Assert.IsTrue(shot.HasValue);
            Assert.AreEqual(0.78f, shot.Value.F1, 1e-6f, "015 기록 자리 = Shot F1");
            Assert.AreEqual(1, shot.Value.A & 1, "페널티 비트");
            var col = (shot.Value.B - 1) % GoalGrid.Cols;
            var row = (shot.Value.B - 1) / GoalGrid.Cols;
            Assert.IsTrue(col <= 1 || col >= GoalGrid.Cols - 2, "바깥 두 열");
            Assert.LessOrEqual(row, 1, "하단 두 행");
        }

        [Test]
        public void NaturalSide_0p6_10k()
        {
            var f = Spot(out var dir);
            var ctx = f.Ctx;
            var shots = ctx.Actions.Shots;
            var natural = 0;
            const int n = 10_000;
            // 오른발 키커의 자연 측 = 키커 왼쪽 = 골문 y 오프셋 부호 = 공격 방향
            for (int i = 0; i < n; i++)
            {
                var a = shots.Plan(ctx, 9, 0, -1f, true, true, BodyPart.RightFoot);
                shots.ExecuteVelocity(ctx, ref a, default);
                var y = shots.Aim.OffsetY(a.ShotCell);
                if (y * dir > 0f) natural++;
            }
            Assert.AreEqual(0.6, natural / (double)n, 0.02);
        }
    }
}
