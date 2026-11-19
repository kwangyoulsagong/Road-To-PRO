using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/074 AC-07 (012 연결, 플랜 Q2): 012 8초 판정이 074 시계를 읽고, 012 재차기가 074 라인 이탈 기록을 읽는다.</summary>
    public class KeeperHoldRetakeLinkTests
    {
        static List<MatchSim> _offLine;

        /// <summary>라인 이탈 확률 1 (모든 PK에서 GK 조기 이탈) — 시드 4 × PK 판정 3.</summary>
        static List<MatchSim> OffLineSims()
        {
            if (_offLine != null) return _offLine;
            var t = MatchTestKit.Tuning();
            var json = "{\"version\": 1, \"values\": [{\"id\": \"gk.pk.offLineP\", \"value\": 1}]}";
            var tuning = ShapeTestKit.TuningWithShape(TuningLoader.ParseBundle(t.Goalkeeper.Registry, json));
            var list = new List<MatchSim>();
            for (int m = 0; m < 4; m++)
            {
                var script = new RefereeScript();
                foreach (var tick in new long[] { 3_000, 12_000, 30_000 }) script.Add(new RefereeAction(tick + m * 41, RefereeActionKind.AwardPenalty, m % 2));
                var sim = new MatchSim(ShapeTestKit.Setup(seed: 7420UL + (ulong)m), tuning, new MatchSimOptions { Referee = script });
                sim.RunUntilTick(31_000);
                list.Add(sim);
            }
            return _offLine = list;
        }

        [Test]
        public void Hold801_Violation_800_Not_LiveUsesKeeperClock()
        {
            foreach (var sim in DistributionLiveTests.Played())
                for (int i = 0; i < sim.Log.Count; i++)
                {
                    var e = sim.Log[i];
                    if (e.Kind == SimEventKind.KeeperHoldViolation) Assert.Greater(e.F0, 8.0f, $"틱 {e.Tick}: 위반은 8 s 초과만");
                    if (e.Kind == SimEventKind.Distribution && e.B >= 0) Assert.LessOrEqual(e.B, 800 + 10, $"틱 {e.Tick}: 손 배급 {e.B} 서브스텝 — 8초 판정을 지나서 배급");
                }
        }

        [Test]
        public void Hold_Violation_ClearsClock()
        {
            foreach (var sim in DistributionLiveTests.Played())
            {
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    if (log[i].Kind != SimEventKind.KeeperHoldViolation) continue;
                    for (int k = i + 1; k < log.Count && log[k].Tick <= log[i].Tick + 100; k++)
                        Assert.IsFalse(log[k].Kind == SimEventKind.KeeperHoldViolation && log[k].Team == log[i].Team, "같은 보유에 두 번 판정");
                }
            }
        }

        /// <summary>FR-03 → 012 FR-66: 라인 이탈 GK가 막거나 PK가 빗나가면 재차기(A = 1), 들어가면 재차기 없음. 재차기 PK는 새 GK 전략.</summary>
        [Test]
        public void OffLine_SavedPk_Retake_ScoredPk_NoRetake()
        {
            int retakes = 0, scoredNoRetake = 0, redraws = 0;
            foreach (var sim in OffLineSims())
            {
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    if (log[i].Kind != SimEventKind.PenaltyKeeper) continue;
                    Assert.AreEqual(1, log[i].B, "라인 이탈 확률 1");
                    bool goal = false, retake = false, interrupted = false;
                    for (int k = i + 1; k < log.Count && log[k].Tick <= log[i].Tick + 60; k++)
                    {
                        if (log[k].Kind == SimEventKind.Penalty) { interrupted = true; break; } // 스크립트 PK 판정이 비행 중에 끼어듦 — 결과 없음
                        if (log[k].Kind == SimEventKind.Goal) { goal = true; break; }
                        if (log[k].Kind == SimEventKind.PenaltyRetake) { retake = true; Assert.AreEqual(1, log[k].A); break; }
                        if (log[k].Kind == SimEventKind.PenaltyKeeper) break;
                    }
                    if (interrupted) continue;
                    if (goal) { scoredNoRetake++; continue; }
                    if (!retake)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int k = i; k < log.Count && log[k].Tick <= log[i].Tick + 60; k++) sb.Append(log[k].Tick).Append(':').Append(log[k].Kind).Append('/').Append(log[k].Team).Append(' ');
                        Assert.Fail($"틱 {log[i].Tick}: 라인 이탈 + 비득점인데 재차기 없음 — {sb}");
                    }
                    retakes++;
                    if (sim.Context.Tick - log[i].Tick < 400) { redraws++; continue; } // 경기 구간 끝 직전 재차기 — 다음 킥이 구간 밖
                    for (int k = i + 1; k < log.Count && log[k].Tick <= log[i].Tick + 400; k++)
                        if (log[k].Kind == SimEventKind.PenaltyKeeper) { redraws++; break; }
                }
            }
            TestContext.WriteLine($"재차기 {retakes} · 득점(재차기 없음) {scoredNoRetake} · 재차기 PK 새 전략 {redraws}");
            Assert.Greater(retakes, 0);
            Assert.Greater(scoredNoRetake, 0);
            Assert.AreEqual(retakes, redraws, "재차기마다 새 GK 전략");
        }
    }
}
