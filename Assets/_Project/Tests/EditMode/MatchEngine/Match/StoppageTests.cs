using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Input;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-08 추가시간 발표·실제·페널티 유예, AC-09 종료 시각 서브스텝 골 판정.</summary>
    public class StoppageTests
    {
        static RefereeScript Stops(params (long tick, long ticks)[] s)
        {
            var r = new RefereeScript();
            foreach (var (t, n) in s) r.Add(new RefereeAction(t, RefereeActionKind.Stoppage, -1, n, EventCodes.StopVar));
            return r;
        }

        [Test]
        public void Announced_GeqAccumulated_Actual_GeqAnnounced_GrowsAfterAnnouncement()
        {
            // 전반: 1000틱에 70초 → 발표 2분. 발표 뒤(45+0:30)에 2분 더 → 실제 70 + 120 = 190초 (발표 120초 이상)
            var sim = Scripted(ctx => { if (ctx.Tick == 0) ctx.Flow.OnRestartTaken(); },
                options: new MatchSimOptions { Referee = Stops((1000, 700), (27_000 + 300, 1200)) });
            sim.RunUntilTick(27_000 + 2000);
            var ann = Events(sim, SimEventKind.AddedTimeAnnounced);
            Assert.That(ann[0].A, Is.EqualTo(2), "⌈70 s / 60⌉ = 2분");
            Assert.That(ann[0].A * 600, Is.GreaterThanOrEqualTo(ann[0].B), "발표 ≥ 그때까지 누적");
            var end = Events(sim, SimEventKind.PeriodEnd)[0];
            Assert.That(end.B, Is.EqualTo(1900), "실제 추가 = 발표 뒤 늘어난 누적 190 s");
            Assert.That(end.B, Is.GreaterThanOrEqualTo(ann[0].A * 600));
            Assert.That(end.Time.Format(), Is.EqualTo("45+3:10"));
        }

        [Test]
        public void ZeroStoppage_EndsAtExactly45()
        {
            var sim = Scripted(ctx => { if (ctx.Tick == 0) ctx.Flow.OnRestartTaken(); });
            sim.RunUntilTick(27_010);
            var ann = Events(sim, SimEventKind.AddedTimeAnnounced)[0];
            Assert.That(ann.A, Is.EqualTo(0));
            var end = Events(sim, SimEventKind.PeriodEnd)[0];
            Assert.That(end.Tick, Is.EqualTo(26_999L));
            Assert.That(end.Time.Format(), Is.EqualTo("45:00"));
            Assert.That(Events(sim, SimEventKind.PeriodStart)[1].Tick, Is.EqualTo(27_000L), "하프타임 0틱 — 후반은 바로 다음 틱");
        }

        [Test]
        public void PenaltyDeclared_DelaysWhistleUntilKickResolved()
        {
            // 44:59.5에 페널티 → 추가시간 0이라 원래 45:00 종료지만 킥이 끝날 때까지 기다린다
            var script = new RefereeScript();
            script.Add(new RefereeAction(26_995, RefereeActionKind.AwardPenalty, 0));
            var o = new MatchSimOptions { Referee = script };
            var sim = new MatchSim(Setup(41UL), TuningWith(Values(("standin.goalCelebration", 0f), ("standin.restartDelay", 3f))), o);
            sim.RunUntilTick(27_500);
            var end = Events(sim, SimEventKind.PeriodEnd)[0];
            Assert.That(end.Tick, Is.GreaterThan(26_999L), "페널티 대기 중엔 휘슬 없음");
            var pk = -1;
            for (int i = 0; i < sim.Log.Count; i++) if (sim.Log[i].Kind == SimEventKind.Shot && sim.Log[i].A == 1) { pk = i; break; }
            Assert.That(pk, Is.GreaterThanOrEqualTo(0), "페널티 킥");
            Assert.That(sim.Log[pk].Tick, Is.LessThan(end.Tick), "킥 뒤에 휘슬");
            Assert.That(sim.Log[pk].Tick, Is.GreaterThanOrEqualTo(26_995L + 30));
        }

        [Test]
        public void VarDisallowedInAddedTime_AnnouncedDoesNotShrink()
        {
            var script = Stops((1000, 1200));
            script.Add(new RefereeAction(27_100, RefereeActionKind.DisallowLastGoal, 0));
            var sim = Scripted(ctx => { if (ctx.Tick == 0) ctx.Flow.OnRestartTaken(); }, options: new MatchSimOptions { Referee = script });
            sim.RunUntilTick(30_000);
            Assert.That(Events(sim, SimEventKind.VarOverturn).Count, Is.EqualTo(1));
            Assert.That(Events(sim, SimEventKind.AddedTimeAnnounced)[0].A, Is.EqualTo(2));
            Assert.That(Events(sim, SimEventKind.PeriodEnd)[0].B, Is.GreaterThanOrEqualTo(1200));
        }

        /// <summary>마지막 틱(26,999)의 서브스텝 offset에 골라인을 넘도록 공을 굴린다.</summary>
        static MatchSim LastTickShot(float crossAt)
        {
            MatchSim sim = null;
            sim = Scripted(ctx =>
            {
                if (ctx.Tick == 0) ctx.Flow.OnRestartTaken();
                if (ctx.Tick == 26_999)
                {
                    var L = ctx.Pitch.LengthM;
                    var r = ctx.BallParams.Radius;
                    const float v = 10f;
                    ctx.Ball = BallState.RollingAt(new Vec2(L + r - v * crossAt, 34f), new Vec2(v, 0f), r);
                    ctx.LastTouchTeam = 0;
                    ctx.LastTouchSlot = 9;
                }
            });
            sim.RunUntilTick(27_005);
            return sim;
        }

        [Test]
        public void GoalOnLastSubstep_Counts_NextIsVoid()
        {
            var inTime = LastTickShot(0.095f); // 서브스텝 9 (0.09~0.10 s)
            var goals = Events(inTime, SimEventKind.Goal);
            Assert.That(goals.Count, Is.EqualTo(1), "종료 시각 이하 서브스텝 → 골");
            Assert.That(goals[0].Tick, Is.EqualTo(26_999L));
            Assert.That(goals[0].Substep, Is.EqualTo((sbyte)9));
            Assert.That(goals[0].Team, Is.EqualTo(0));
            Assert.That(Events(inTime, SimEventKind.PeriodEnd)[0].Tick, Is.EqualTo(26_999L));
            Assert.That(inTime.Context.Flow.Goals(0), Is.EqualTo(1));

            var late = LastTickShot(0.105f); // 다음 서브스텝 (다음 틱 0) — 시뮬되지 않는다
            Assert.That(Events(late, SimEventKind.Goal), Is.Empty, "종료 뒤 서브스텝 → 무효");
            Assert.That(late.Context.Flow.Goals(0), Is.EqualTo(0));
        }
    }
}
