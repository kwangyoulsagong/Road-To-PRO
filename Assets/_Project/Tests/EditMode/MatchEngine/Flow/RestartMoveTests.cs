using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Flow
{
    /// <summary>specs/058 FR-07~09, AC-04: 재시작 6종 — 접근 구간 순간이동 0, 공 재배치 ≤ 1회, 키커가 공에, 준비 조건 뒤 재시작.</summary>
    public class RestartMoveTests
    {
        static MatchParams P => MatchParams.Create(Tuning().Match);

        sealed class Run
        {
            public MatchSim Sim;
            public TeleportAudit Audit;
        }

        static readonly Dictionary<string, Run> Cache = new Dictionary<string, Run>();

        static Run Get(string key, RefereeScript script, long until)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var r)) return r;
                var sim = Sim(ReferenceSeed, script == null ? null : new MatchSimOptions { Referee = script });
                var audit = new TeleportAudit(sim);
                if (until < 0) sim.RunToEnd(); else sim.RunUntilTick(until);
                return Cache[key] = new Run { Sim = sim, Audit = audit };
            }
        }

        static long FirstGoalTick() => Reference().Result.Goals[0].Tick;

        /// <summary>재시작 이벤트 하나를 골라 접근 → 준비 → 킥을 검사한다.</summary>
        static void Check(Run run, SimEventKind kind, long fromTick = 0)
        {
            var sim = run.Sim;
            int idx = -1;
            for (int i = 0; i < sim.Log.Count; i++)
                if (sim.Log[i].Kind == kind && sim.Log[i].Tick >= fromTick) { idx = i; break; }
            Assert.That(idx, Is.GreaterThanOrEqualTo(0), $"{kind} 재시작 없음");
            var restart = sim.Log[idx];
            long readyTick = -1, kickTick = -1;
            int relocations = 0, approach = 0;
            for (int i = idx; i < sim.Log.Count; i++)
            {
                var e = sim.Log[i];
                if (e.Kind == SimEventKind.DeadBallPhase && e.A == (int)DeadBallPhase.Approach && e.Tick == restart.Tick) approach++;
                if (e.Kind == SimEventKind.BallRelocated && e.A == EventCodes.RelocateRestart) relocations++;
                if (e.Kind == SimEventKind.DeadBallPhase && e.A == (int)DeadBallPhase.RestartReady) readyTick = e.Tick;
                if (readyTick >= 0 && (e.Kind == SimEventKind.Pass || e.Kind == SimEventKind.Shot || e.Kind == SimEventKind.Clearance || e.Kind == SimEventKind.Cross))
                { kickTick = e.Tick; break; }
                if (e.Kind == SimEventKind.Goal || e.Kind == SimEventKind.PeriodEnd || (e.Tick > restart.Tick && e.Kind == SimEventKind.DeadBallPhase && e.A == (int)DeadBallPhase.Approach)) break;
            }
            Assert.That(approach, Is.EqualTo(1), $"{kind} {restart.Tick}: 접근 단계 시작");
            Assert.That(relocations, Is.LessThanOrEqualTo(1), $"{kind} {restart.Tick}: 공 재배치 {relocations}회");
            Assert.That(readyTick, Is.GreaterThanOrEqualTo(restart.Tick), $"{kind} {restart.Tick}: 준비 단계 없음");
            Assert.That(kickTick, Is.GreaterThanOrEqualTo(readyTick + P.RestartDelayTicks), $"{kind} {restart.Tick}: 준비 지연 전 재시작");

            // 순간이동: 재시작 ~ 킥 구간 선수 0건, 공은 재배치 틱만
            foreach (var j in run.Audit.PlayerJumpsExceptPeriodStart())
                Assert.That(j.Substep / 10 < restart.Tick || j.Substep / 10 > kickTick, Is.True, $"{kind}: 선수 순간이동 {j}");
            foreach (var j in run.Audit.BallJumpsWithoutRelocation())
                Assert.That(j.Substep / 10 < restart.Tick || j.Substep / 10 > kickTick, Is.True, $"{kind}: 공 순간이동 {j}");

            // 준비 틱 상태: 키커가 공을 잡았고, 형태 목표 3 m 안 80% 이상 (또는 상한)
            var re = Sim(ReferenceSeed, sim.Context.RefereeScript.Count > 0 ? new MatchSimOptions { Referee = sim.Context.RefereeScript.Clone() } : null);
            re.RunUntilTick(readyTick + 1);
            var ctx = re.Context;
            var taker = ctx.Flow.RestartTaker;
            Assert.That(ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId == taker, Is.True, $"{kind}: 준비 때 키커가 공을 잡지 않음");
            Assert.That((ctx.Ball.Position.XY - ctx.Flow.RestartSpot).Length(), Is.LessThanOrEqualTo(P.ClaimRadius + 0.05f), $"{kind}: 공이 재시작 지점에 없음");
            var capped = readyTick - restart.Tick >= P.RestartWaitMaxTicks;
            if (!capped)
            {
                int n = 0, near = 0;
                for (int s = 0; s < Squad.Slots; s++)
                {
                    if (s == taker || !ctx.IsSlotActive(s) || !ctx.Board.HasShapeTarget(s)) continue;
                    n++;
                    if ((ctx.SlotState(s).Position - ctx.Board.ShapeTarget(s)).Length() <= P.RestartArriveRadius) near++;
                }
                Assert.That(n == 0 || near >= P.RestartArriveShare * n, Is.True, $"{kind}: 배치 도착 {near}/{n}");
            }
        }

        /// <summary>
        /// specs/086 FR-01·AC-01 (Q1 ①): 기준 시드 90분의 모든 재시작·킥오프에서 키커는 멈춘 뒤(직전 틱 끝 속도 ≤ restartTakeMaxSpeed) 공을 잡고,
        /// 잡은 뒤 킥까지 공은 재시작 지점(≤ claimRadius + 0.05)에서 움직이지 않는다(≤ 1 cm — 순간이동 0).
        /// 084 T15b ①: 기준 시드 스로인에서 지나가던 키커가 잡아 공이 발 앞으로 1.52 m 옮겨졌다.
        /// 플랜의 "고정 상황(0.8 m 옆 6 m/s)"은 같은 경우가 기준 시드 실경기에 있어 그 경기 전체 재시작으로 대신했다(086 T02 결과 줄).
        /// </summary>
        [Test]
        public void Restart_KickerPassingBy_TakesOnlyAfterStop_BallStaysAtSpot()
        {
            var sim = Sim(ReferenceSeed);
            var ctx = sim.Context;
            var failures = new List<string>();
            var prevSpeed = new float[Squad.Slots];
            bool holding = false, reported = false;
            int takes = 0;
            while (sim.StepTick())
            {
                var flow = ctx.Flow;
                ref readonly var ball = ref ctx.Ball;
                var taker = flow.RestartTaker;
                var owned = flow.State == FlowState.DeadBall && taker >= 0 && ball.Phase == BallPhase.Controlled && ball.OwnerId == taker;
                if (owned && !holding)
                {
                    takes++;
                    reported = false;
                    var periodStart = ctx.Tick - flow.PeriodStartTick <= 1; // 기간 시작 킥오프는 배치로 공을 준다(058 기간 시작 예외) — 잡기 아님
                    if (!periodStart && prevSpeed[taker] > P.RestartTakeMaxSpeed + 1e-4f) failures.Add($"틱 {ctx.Tick} {flow.Restart}: 키커 {taker} 속도 {prevSpeed[taker]:0.00} m/s에서 잡음");
                }
                if (owned && !reported)
                {
                    // 잡은 순간부터 킥 전까지 공은 재시작 지점 그대로 (FR-01: 허용치 claimRadius + 0.05, 순간이동 0 → 1 cm)
                    var toSpot = (ball.Position.XY - flow.RestartSpot).Length();
                    if (toSpot > 0.01f) { failures.Add($"틱 {ctx.Tick} {flow.Restart}: 잡은 뒤 공–지점 {toSpot:0.00} m (허용 {P.ClaimRadius + 0.05f:0.00}, 순간이동 0 기준 0.01)"); reported = true; }
                }
                holding = owned;
                for (int s = 0; s < Squad.Slots; s++) prevSpeed[s] = ctx.SlotState(s).Velocity.Length();
            }
            Assert.That(failures, Is.Empty, $"잡기 {takes}회 중 위반 {failures.Count}건\n" + string.Join("\n", failures));
            Assert.That(takes, Is.GreaterThan(10), "재시작 잡기가 너무 적음");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test] public void ThrowIn_ApproachReadyKick() => Check(Get("ref", null, 20_000), SimEventKind.ThrowIn);
        [Test] public void GoalKick_ApproachReadyKick() => Check(Get("ref-full", null, -1), SimEventKind.GoalKick);
        [Test] public void Corner_ApproachReadyKick() => Check(Get("ref-full", null, -1), SimEventKind.Corner);

        [Test]
        public void FreeKick_ApproachReadyKick()
        {
            var g = FirstGoalTick();
            var script = new RefereeScript();
            script.Add(new RefereeAction(g + 30, RefereeActionKind.DisallowLastGoal, Reference().Result.Goals[0].Team));
            Check(Get("fk", script, g + 900), SimEventKind.VarOverturn, g + 30); // 골 취소 → 프리킥 (대체 심판은 FreeKick 이벤트 없이 재시작)
        }

        [Test]
        public void Penalty_ApproachReadyKick()
        {
            var script = new RefereeScript();
            script.Add(new RefereeAction(3_000, RefereeActionKind.AwardPenalty, 0));
            Check(Get("pk", script, 4_000), SimEventKind.Penalty, 3_000);
        }

        [Test]
        public void DropBall_ApproachReadyKick()
        {
            var script = new RefereeScript();
            script.Add(new RefereeAction(5_000, RefereeActionKind.Injury, 104, 100));
            Check(Get("drop", script, 6_500), SimEventKind.Injury, 5_000);
        }
    }
}
