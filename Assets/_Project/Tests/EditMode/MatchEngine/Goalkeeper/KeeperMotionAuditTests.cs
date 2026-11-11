using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>
    /// specs/011 AC-21: 검증 경기 100경기(2026-11-11 사용자 결정, 1,000경기는 017)에서 다이빙 밖 GK의 005 FR-52 운동 한계 초과 0건,
    /// 결정 틱 사이 GK 목표점 점프(FR-07 상한 1.5 m 초과) 0건. 틱 단위로 재므로 30 Hz 기록 프레임 사이보다 촘촘하다. 약 12분 — 스펙 끝 1회.
    /// </summary>
    public class KeeperMotionAuditTests
    {
        [Test, Explicit("≈ 15분, 스펙 마지막 1회"), Category("Slow"), Timeout(1_800_000)]
        public void Matches100_NoLimitBreach_NoTargetJump()
        {
            int limit = 0, jumps = 0, goals = 0, saves = 0, matches = 100;
            var report = new System.Text.StringBuilder();
            for (int m = 0; m < matches; m++)
            {
                var sim = Sim(110_000UL + (ulong)m);
                var ctx = sim.Context;
                var mon = new MotionLimitMonitor(1 << 16);
                ctx.Kinematics.Monitor = mon;
                var maxStep = ctx.Keepers.Params.PosMaxStep;
                var prev = new Vec2[2];
                var had = new bool[2];
                while (!sim.IsFinished)
                {
                    sim.StepTicks(1);
                    for (int t = 0; t < 2; t++)
                    {
                        var has = ctx.Keepers.HasTarget(t);
                        var cur = ctx.Keepers.Target(t);
                        if (has && had[t] && (cur - prev[t]).Length() > maxStep + 1e-4f)
                        {
                            jumps++;
                            if (jumps <= 10) report.AppendLine($"경기 {m} 틱 {ctx.Tick} 팀 {t} 목표점 점프 {(cur - prev[t]).Length():F3} m");
                        }
                        prev[t] = cur;
                        had[t] = has;
                    }
                }
                Assert.AreEqual(mon.TotalViolations, (long)mon.Count, "모니터 버퍼가 잘리지 않았다 (GK 위반을 놓치지 않게)");
                for (int i = 0; i < mon.Count; i++)
                {
                    var v = mon[i];
                    if (!Squad.IsKeeperSlot(v.PlayerId)) continue;
                    limit++;
                    if (limit <= 10) report.AppendLine($"경기 {m} 서브스텝 {v.Substep} GK {v.PlayerId} {v.Kind} {v.Value:F3} > {v.Limit:F3}");
                }
                goals += sim.Result.Goals.Length;
                saves += Events(sim, RoadToPro.MatchEngine.Match.Events.SimEventKind.Save).Count;
            }
            TestContext.WriteLine($"{matches}경기: 평균 득점 {goals / (double)matches:F2}, 평균 선방 {saves / (double)matches:F2}, 선방률 {saves / (double)(saves + goals):P1}");
            TestContext.WriteLine(report.ToString());
            Assert.AreEqual(0, limit, "다이빙 밖 GK 운동 한계 초과");
            Assert.AreEqual(0, jumps, "GK 목표점 점프");
        }
    }
}
