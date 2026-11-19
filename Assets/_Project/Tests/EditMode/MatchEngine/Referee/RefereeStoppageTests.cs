using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Flow;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-15 (FR-73·90): 012 정지 이벤트 시간 합 = 006 하프별 집계, 같은 시드 = 같은 VAR 시간. AC-25 (FR-68·69): 실경기 재시작 이동.</summary>
    public class RefereeStoppageTests
    {
        static bool Is012Stop(in SimEvent e, out long ticks, out int kind)
        {
            ticks = 0; kind = -1;
            if (e.Kind == SimEventKind.VarCheck) { ticks = e.B; kind = EventCodes.StopVar; return true; }
            if (e.Kind == SimEventKind.Stoppage && (e.A == EventCodes.StopCard || e.A == EventCodes.StopDissent)) { ticks = e.B; kind = e.A; return true; }
            return false;
        }

        [Test, Category("Slow")]
        public void VarAndTwoCards_SumMatchesRng_FlowsInto006()
        {
            for (ulong m = 0; m < LiveReferee.Matches; m++)
            {
                var seed = LiveReferee.FirstSeed + m;
                var sim = new MatchSim(ShapeTestKit.Setup(seed: seed), ShapeTestKit.TuningWithShape());
                long expected = 0;
                int vars = 0, cards = 0;
                var seen = 0;
                var runningBefore = false;
                long lastGot = 0;
                while (sim.StepTick())
                {
                    var flow = sim.Context.Flow;
                    var log = sim.Log;
                    var goalTick = false;
                    for (int i = seen; i < log.Count; i++) if (log[i].Kind == SimEventKind.Goal) goalTick = true;
                    var periodEnd = false;
                    for (int i = seen; i < log.Count; i++)
                    {
                        ref readonly var e = ref log[i];
                        if (e.Period != 0) continue;
                        if (e.Kind == SimEventKind.PeriodEnd) periodEnd = true;
                        if (!Is012Stop(in e, out var t, out var kind)) continue;
                        if (kind == EventCodes.StopVar) vars++; else cards++;
                        if (!goalTick && !runningBefore) expected += t; // 골 데드볼 중이면 골 정지(실제 길이)가 담는다 (006·058 FR-05)
                    }
                    seen = log.Count;
                    if (periodEnd)
                    {
                        if (vars >= 1 && cards >= 2)
                        {
                            var got = lastGot; // 전반 끝 틱에 006이 다음 기간으로 집계를 비운다 → 직전 틱 값
                            TestContext.WriteLine($"시드 {seed} 전반: VAR {vars} · 카드·항의 정지 {cards} · 이벤트 합 {expected} 틱 · 006 집계 {got} 틱");
                            Assert.AreEqual(expected, got, "012 정지 이벤트 합 = 006 하프 집계");
                            var again = new MatchSim(ShapeTestKit.Setup(seed: seed), ShapeTestKit.TuningWithShape());
                            again.RunToEnd();
                            for (int i = 0, j = 0; i < sim.Log.Count; i++)
                            {
                                if (sim.Log[i].Kind != SimEventKind.VarCheck) continue;
                                while (again.Log[j].Kind != SimEventKind.VarCheck) j++;
                                Assert.AreEqual(sim.Log[i].B, again.Log[j].B, "같은 시드 = 같은 VAR 체크 시간");
                                j++;
                                if (sim.Log[i].Period != 0) break;
                            }
                            return;
                        }
                        break;
                    }
                    runningBefore = flow.Stoppage.Running;
                    if (flow.Period == 0) lastGot = flow.Stoppage.KindTicks(EventCodes.StopVar) + flow.Stoppage.KindTicks(EventCodes.StopCard) + flow.Stoppage.KindTicks(EventCodes.StopDissent);
                }
            }
            Assert.Inconclusive("12경기 전반에 VAR 1 + 카드 2 조합이 없다");
        }

        [Test, Category("Slow")]
        public void RealReferee_Restarts_NoTeleport_BallContinuous()
        {
            var sim = new MatchSim(ShapeTestKit.Setup(seed: LiveReferee.FirstSeed + 5), ShapeTestKit.TuningWithShape());
            var audit = new TeleportAudit(sim);
            sim.RunToEnd();
            var fk = LiveReferee.Count(sim.Log, SimEventKind.FreeKick);
            var outs = LiveReferee.Count(sim.Log, SimEventKind.GoalKick) + LiveReferee.Count(sim.Log, SimEventKind.Corner);
            TestContext.WriteLine($"프리킥 {fk} · 골킥·코너 {outs}");
            Assert.Greater(fk, 0);
            Assert.Greater(outs, 0);
            Assert.IsEmpty(audit.PlayerJumpsExceptPeriodStart(), "선수 순간이동 0 (058 FR-07~09 이동 규칙)");
            Assert.IsEmpty(audit.BallJumpsWithoutRelocation(), "공 순간이동 0 (재배치 이벤트 틱 제외 — FR-68 아웃 뒤 공 연속)");
            // 재배치는 재시작당 1회 이하
            long lastRestart = -1; int reloc = 0;
            for (int i = 0; i < sim.Log.Count; i++)
            {
                var k = sim.Log[i].Kind;
                if (k == SimEventKind.DeadBallPhase && sim.Log[i].A == 4) { lastRestart = sim.Log[i].Tick; reloc = 0; }
                if (k == SimEventKind.BallRelocated && sim.Log[i].A == EventCodes.RelocateRestart) { reloc++; Assert.LessOrEqual(reloc, 1, $"틱 {sim.Log[i].Tick} 재배치 2회"); }
            }
        }
    }
}
