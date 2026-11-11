using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Ball;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/011 경계 · AC-15: GK 퇴장 후 필드 선수가 GK를 맡으면 위치·선방이 동작하고 선방률이 정규 GK보다 낮다.</summary>
    public class KeeperReplacementTests
    {
        [Test]
        public void FieldPlayerInGoal_WorksAndSavesLess()
        {
            var sim = Scripted(ctx =>
            {
                if (ctx.Tick == 0) ctx.Flow.OnRestartTaken();
                if (ctx.Tick == 3) ctx.Flow.RemoveFromPitch(0, PlayerStatus.SentOff);
            });
            var ctx = sim.Context;
            var gkId = ctx.Squad.PlayerAt(0);
            for (int i = 0; i < 1200 && !(ctx.Squad.IsSlotFilled(0) && ctx.Squad.PlayerAt(0) != gkId); i++) sim.StepTicks(1);
            Assert.IsTrue(ctx.Squad.IsSlotFilled(0), "GK 슬롯이 다시 찼다");
            var newId = ctx.Squad.PlayerAt(0);
            Assert.AreNotEqual(gkId, newId);
            Assert.AreEqual(PlayerStatus.SentOff, ctx.Squad.StatusOf(gkId));
            Assert.IsTrue(ctx.IsSlotActive(0));
            Assert.AreEqual(10, ctx.Squad.OnPitchCount(0), "10명");

            // 위치 탐색 동작
            sim.StepTicks(5);
            Assert.IsTrue(ctx.Keepers.HasTarget(0));

            // 매핑: 필드 선수 대체값(표시 2) → 리그 평균보다 느리다
            var field = ctx.Keepers.Profile(ctx, 0);
            var avg = ctx.Keepers.LeagueAverage;
            Assert.Greater(field.ReactTime, avg.ReactTime);
            Assert.Less(field.DiveSpeed, avg.DiveSpeed);

            // 같은 위치·같은 슛 600개: 선방률 field < 정규
            var p = ctx.Keepers.Params;
            var m = new SaveModel(p, new KeeperPositioner(p, new GoalGrid(p)));
            var bp = BallTestKit.Params();
            m.Bind(bp);
            var goal = new Vec2(0f, 34f);
            var sctx = new SaveContext { Goal = goal, DirIn = 1f };
            var rng = new SeedRng(9UL);
            var r1 = new SeedRng(1UL);
            var r2 = new SeedRng(1UL);
            int on = 0, sField = 0, sAvg = 0;
            for (int i = 0; i < 600; i++)
            {
                var origin = new Vec3(rng.Range(12f, 22f), 34f + rng.Range(-8f, 8f), 0.11f);
                var s = m.AimedShot(origin, (int)(rng.NextFloat() * GoalGrid.Cells), 24f, goal, bp);
                if (!m.Predict(in s, bp, goal, 1f)) continue;
                on++;
                var gk = new KeeperState { Position = m.Positioner.Target(new PositionInput { Goal = goal, DirIn = 1f, Ball = new Vec2(origin.X, origin.Y), LineHeight = 30f }, avg) };
                var jf = m.Evaluate(in gk, in field, in sctx);
                if (m.Resolve(in jf, in field, in sctx, r1).Kind != SaveOutcomeKind.Miss) sField++;
                var ja = m.Evaluate(in gk, in avg, in sctx);
                if (m.Resolve(in ja, in avg, in sctx, r2).Kind != SaveOutcomeKind.Miss) sAvg++;
            }
            TestContext.WriteLine($"선방 필드 선수 {sField}/{on}, 정규 GK {sAvg}/{on}");
            Assert.Less(sField, sAvg);
            double pf = (double)sField / on, pa = (double)sAvg / on, pp = (pf + pa) / 2;
            var z = (pa - pf) / System.Math.Sqrt(pp * (1 - pp) * 2.0 / on);
            Assert.Greater(z, 2.33);
        }
    
        [Test]
        public void SentOffMidDive_WalksOff_SlotRefilled()
        {
            var removed = false;
            var sim = Scripted(ctx =>
            {
                if (ctx.Tick == 0)
                {
                    ctx.Flow.OnRestartTaken();
                    for (int s = 1; s < Squad.Slots; s++) if (ctx.IsSlotActive(s)) ctx.Kinematics.Place(s, new Vec2(95f, 4f + 2.8f * s), new Vec2(-1f, 0f));
                    ctx.Kinematics.Place(0, new Vec2(2f, 34f), new Vec2(1f, 0f));
                }
                if (ctx.Tick == 5)
                {
                    ctx.Ball = BallState.InAir(new Vec3(16f, 28f, 0.11f), new Vec3(-21.5f, 11.5f, 2.2f), Vec3.Zero);
                    ctx.LastTouchTeam = 1; ctx.LastTouchSlot = 11; ctx.LastKickSlot = 11;
                    ctx.Keepers.NotifyFlight();
                }
                if (!removed && ctx.Keepers.IsBusy(0)) { ctx.Flow.RemoveFromPitch(0, PlayerStatus.SentOff); removed = true; }
            });
            var c = sim.Context;
            var gkId = c.Squad.PlayerAt(0);
            for (int i = 0; i < 1500 && !(c.Squad.IsSlotFilled(0) && c.Squad.PlayerAt(0) != gkId); i++) sim.StepTicks(1);
            Assert.IsTrue(removed, "다이빙 중 퇴장");
            Assert.IsTrue(c.Squad.IsSlotFilled(0) && c.Squad.PlayerAt(0) != gkId, "걸어 나간 뒤 GK 슬롯이 다시 찬다");
            Assert.IsFalse(c.Kinematics.IsExternal(0));
        }

        [Test]
        public void MissThenNoGoal_PendingFlagsCleared()
        {
            var p = GoalkeeperParams.Defaults;
            var w = new ShotWatcher(p, new SaveModel(p, new KeeperPositioner(p, new GoalGrid(p))));
            w.NotifyFlight();
            w.ConsumeGoal(0, out var f, out var x);
            Assert.AreEqual(0, f);
            Assert.AreEqual(-1, w.PendingGoalTeam);
        }
    }
}
