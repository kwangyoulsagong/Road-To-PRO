using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Flow
{
    /// <summary>specs/058 FR-01~06·60·64, AC-02·03: 골 후 데드볼 (세리머니 → 복귀 → 준비 → 킥오프).</summary>
    public class DeadBallFlowTests
    {
        static MatchSim _full;
        static readonly object Gate = new object();

        /// <summary>참조 시드 한 경기 (끝까지). 골이 여러 개 난다.</summary>
        static MatchSim Full()
        {
            lock (Gate)
            {
                if (_full != null) return _full;
                var s = Sim(ReferenceSeed);
                s.RunToEnd();
                return _full = s;
            }
        }

        static MatchParams P => MatchParams.Create(Tuning().Match);

        /// <summary>골마다 (골 틱, 이어지는 킥오프 틱 또는 −1).</summary>
        static List<(SimEvent goal, long kickoff)> Goals(MatchSim sim)
        {
            var r = new List<(SimEvent, long)>();
            for (int i = 0; i < sim.Log.Count; i++)
            {
                if (sim.Log[i].Kind != SimEventKind.Goal) continue;
                long ko = -1;
                for (int j = i + 1; j < sim.Log.Count; j++)
                {
                    var k = sim.Log[j].Kind;
                    if (k == SimEventKind.Kickoff) { ko = sim.Log[j].Tick; break; }
                    if (k == SimEventKind.PeriodEnd || k == SimEventKind.Goal) break;
                }
                r.Add((sim.Log[i], ko));
            }
            return r;
        }

        static List<SimEvent> Between(MatchSim sim, SimEventKind kind, long from, long to)
        {
            var l = new List<SimEvent>();
            foreach (var e in Events(sim, kind)) if (e.Tick >= from && e.Tick <= to) l.Add(e);
            return l;
        }

        [Test]
        public void Goal_CelebrationLength_EqualsTuning()
        {
            var sim = Full();
            var goals = Goals(sim);
            Assert.That(goals.Count, Is.GreaterThan(0), "참조 경기에 골이 없다");
            foreach (var (g, _) in goals)
            {
                var phases = Between(sim, SimEventKind.DeadBallPhase, g.Tick, g.Tick + P.CelebrationTicks + 1);
                var cel = phases.Find(e => e.A == (int)DeadBallPhase.Celebration);
                var ret = phases.Find(e => e.A == (int)DeadBallPhase.Return);
                Assert.That(cel.Kind, Is.EqualTo(SimEventKind.DeadBallPhase), $"골 {g.Tick}: 세리머니 단계 없음");
                Assert.That(cel.Tick, Is.EqualTo(g.Tick));
                Assert.That(ret.Kind, Is.EqualTo(SimEventKind.DeadBallPhase), $"골 {g.Tick}: 복귀 단계 없음");
                Assert.That(ret.Tick - g.Tick, Is.InRange(P.CelebrationTicks - 1, P.CelebrationTicks + 1));
            }
        }

        [Test]
        public void Goal_BallRelocatedOnce_AfterCelebration()
        {
            var sim = Full();
            foreach (var (g, ko) in Goals(sim))
            {
                if (ko < 0) continue;
                var rel = Between(sim, SimEventKind.BallRelocated, g.Tick, ko);
                Assert.That(rel.Count, Is.LessThanOrEqualTo(1), $"골 {g.Tick}: 재배치 {rel.Count}회");
                if (rel.Count == 1)
                {
                    Assert.That(rel[0].A, Is.EqualTo(EventCodes.RelocateGoal));
                    Assert.That(rel[0].Tick, Is.GreaterThanOrEqualTo(g.Tick + P.CelebrationTicks), "세리머니 구간(라이브 골 장면 안)에 재배치");
                }
            }
        }

        [Test]
        public void Goal_KickoffAfterArrival_AndReadyDelay()
        {
            var sim = Full();
            var goals = Goals(sim);
            var checkedOne = false;
            foreach (var (g, ko) in goals)
            {
                if (ko < 0) continue;
                var ready = Between(sim, SimEventKind.DeadBallPhase, g.Tick, ko).Find(e => e.A == (int)DeadBallPhase.Ready);
                Assert.That(ready.Kind, Is.EqualTo(SimEventKind.DeadBallPhase), $"골 {g.Tick}: 준비 단계 없음");
                Assert.That(ko, Is.GreaterThanOrEqualTo(ready.Tick + P.KickoffDelayTicks), "준비 지연 전에 킥오프");
                Assert.That(ready.Tick - g.Tick, Is.LessThanOrEqualTo(P.GoalCelebrationTicks + 600), "상한을 한참 넘겨 대기");

                // 준비 틱 상태를 다시 만들어 도착(1.0 m) 또는 상한 + 규칙 위반 0명을 확인
                var re = Sim(ReferenceSeed);
                re.RunUntilTick(ready.Tick + 1);
                var ctx = re.Context;
                var kickTeam = ctx.Flow.RestartTeam;
                var kicker = ctx.Flow.RestartTaker;
                bool capped = ready.Tick - g.Tick >= P.GoalCelebrationTicks;
                for (int s = 0; s < Squad.Slots; s++)
                {
                    if (!ctx.IsSlotActive(s)) continue;
                    var pos = ctx.SlotState(s).Position;
                    Assert.That(KickoffLayout.Violates(ctx, kickTeam, kicker, s, pos), Is.False, $"골 {g.Tick}: 슬롯 {s} 킥오프 규칙 위반 {pos}");
                    if (!capped)
                        Assert.That((pos - KickoffLayout.Target(ctx, kickTeam, kicker, s)).Length(), Is.LessThanOrEqualTo(P.ArriveRadius + 0.05f),
                            $"골 {g.Tick}: 슬롯 {s} 미도착인데 준비");
                }
                checkedOne = true;
                break; // 재시뮬은 한 골만 (시간)
            }
            Assert.That(checkedOne, Is.True, "킥오프까지 이어진 골이 없다");
        }

        [Test]
        public void Goal_StoppageEqualsActualLength()
        {
            var sim = Full();
            foreach (var (g, ko) in Goals(sim))
            {
                if (ko < 0) continue;
                var re = Sim(ReferenceSeed);
                re.RunUntilTick(g.Tick);
                var before = re.Context.Flow.Stoppage.KindTicks(EventCodes.StopGoal);
                re.RunUntilTick(ko + 1);
                var after = re.Context.Flow.Stoppage.KindTicks(EventCodes.StopGoal);
                Assert.That(after - before, Is.EqualTo(ko - g.Tick), "골 정지 추가시간 ≠ 골 ~ 킥오프 실제 길이");
                return;
            }
            Assert.Inconclusive("같은 기간에 킥오프까지 이어진 골 없음");
        }

        [Test]
        public void Goal_NoPeriodEndDuringCelebration()
        {
            var sim = Full();
            foreach (var (g, _) in Goals(sim))
                Assert.That(Between(sim, SimEventKind.PeriodEnd, g.Tick, g.Tick + P.CelebrationTicks - 1), Is.Empty, $"골 {g.Tick}: 세리머니 중 기간 종료");
        }

        [Test]
        public void Goal_CapReached_KickoffWithoutViolators()
        {
            // 복귀 속도를 아주 낮추고 상한을 짧게: 도착 못 하고 상한에서 시작하거나, 위반자가 있으면 진단 뒤 기다린다
            var tuning = TuningWith(Values(("deadball.returnSpeedMin", 0.1f), ("deadball.returnSpeedMax", 0.1f), ("standin.goalCelebration", 10f)));
            var sim = Sim(ReferenceSeed, tuning: tuning);
            sim.RunToEnd();
            var goals = Goals(sim);
            Assert.That(goals.Count, Is.GreaterThan(0));
            var (g, ko) = goals[0];
            Assert.That(ko, Is.GreaterThan(0));
            var ready = Between(sim, SimEventKind.DeadBallPhase, g.Tick, ko).Find(e => e.A == (int)DeadBallPhase.Ready);
            Assert.That(ready.Tick - g.Tick, Is.GreaterThanOrEqualTo(100L), "상한 전 준비 (도착 불가 조건)");
            var held = Between(sim, SimEventKind.RestartHeld, g.Tick, ready.Tick);
            var re = Sim(ReferenceSeed, tuning: tuning);
            re.RunUntilTick(ready.Tick + 1);
            var ctx = re.Context;
            for (int s = 0; s < Squad.Slots; s++)
                if (ctx.IsSlotActive(s))
                    Assert.That(KickoffLayout.Violates(ctx, ctx.Flow.RestartTeam, ctx.Flow.RestartTaker, s, ctx.SlotState(s).Position), Is.False, $"슬롯 {s} 위반 상태로 준비");
            if (ready.Tick - g.Tick > 100L) Assert.That(held.Count, Is.EqualTo(1), "상한 뒤 대기했는데 진단 없음");
        }

        [Test]
        public void Goal_Disallowed_SwitchesToRestart()
        {
            var first = Goals(Full())[0].goal;
            var script = new RefereeScript();
            script.Add(new RefereeAction(first.Tick + 30, RefereeActionKind.DisallowLastGoal, first.Team));
            var sim = Sim(ReferenceSeed, new MatchSimOptions { Referee = script });
            sim.RunUntilTick(first.Tick + 400);
            var fk = Between(sim, SimEventKind.DeadBallPhase, first.Tick + 30, first.Tick + 400).Find(e => e.A == (int)DeadBallPhase.Approach);
            Assert.That(fk.Kind, Is.EqualTo(SimEventKind.DeadBallPhase), "골 취소 뒤 재시작 접근 단계 없음");
            Assert.That(Between(sim, SimEventKind.Kickoff, first.Tick, first.Tick + 400), Is.Empty, "취소된 골로 킥오프");
            Assert.That(sim.Context.Flow.Goals(first.Team), Is.EqualTo(0));
        }
    }
}
