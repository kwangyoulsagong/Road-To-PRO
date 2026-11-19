using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/074 FR-01 경기 중 PK: 예약 킥이 잡히면 GK 전략을 뽑고 킥 t_e 전에 조기 다이빙 (010 실경기 파이프라인).</summary>
    public class PenaltyKeeperLiveTests
    {
        public struct PkCase
        {
            public SimEvent Keeper, Shot;
            public bool HasDive;
            public long DiveAbs;
            public bool Scored;
        }

        static List<PkCase> _cases;

        static long Abs(in SimEvent e) => e.Tick * 10 + (e.Substep < 0 ? 9 : e.Substep);

        /// <summary>시드 6개 × PK 판정(스크립트) 3번 — 같은 경기 진행을 공유한다.</summary>
        public static List<PkCase> Cases()
        {
            if (_cases != null) return _cases;
            var list = new List<PkCase>();
            ShapeTestKit.Setup(seed: 1UL);
            for (int m = 0; m < 6; m++)
            {
                var script = new RefereeScript();
                foreach (var t in new long[] { 3_000, 12_000, 30_000 }) script.Add(new RefereeAction(t + m * 37, RefereeActionKind.AwardPenalty, m % 2));
                var sim = new MatchSim(ShapeTestKit.Setup(seed: 7400UL + (ulong)m), ShapeTestKit.TuningWithShape(), new MatchSimOptions { Referee = script });
                sim.RunUntilTick(31_000);
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    if (log[i].Kind != SimEventKind.PenaltyKeeper) continue;
                    var c = new PkCase { Keeper = log[i] };
                    for (int k = i + 1; k < log.Count && log[k].Tick <= log[i].Tick + 30; k++)
                    {
                        var e = log[k];
                        if (e.Kind == SimEventKind.DiveStart && e.Team == log[i].Team && !c.HasDive && Abs(in e) == Abs(log[i])) { c.HasDive = true; c.DiveAbs = Abs(in e); }
                        if (e.Kind == SimEventKind.Shot && (e.A & 1) != 0) { c.Shot = e; break; }
                    }
                    for (int k = i + 1; k < log.Count && log[k].Tick <= log[i].Tick + 50; k++)
                        if (log[k].Kind == SimEventKind.Goal) { c.Scored = true; break; }
                    list.Add(c);
                }
            }
            return _cases = list;
        }

        [Test]
        public void LivePk_EarlyDive_BeforeKick()
        {
            var cases = Cases();
            int dives = 0, kicked = 0;
            foreach (var c in cases)
            {
                if (c.Shot.Kind != SimEventKind.Shot) continue;
                kicked++;
                var lead = (Abs(in c.Shot) - Abs(in c.Keeper)) * 0.01f;
                Assert.That(lead, Is.InRange(0.04f, 0.36f), $"틱 {c.Keeper.Tick}: 킥 전 조기 시각 {lead:F2}");
                if (c.Keeper.A != 0)
                {
                    dives++;
                    Assert.IsTrue(c.HasDive, $"틱 {c.Keeper.Tick}: 방향 {c.Keeper.A}인데 다이빙 시작 없음");
                    Assert.Less(c.DiveAbs, Abs(in c.Shot), "다이빙이 킥보다 먼저");
                }
            }
            TestContext.WriteLine($"PK {cases.Count} · 킥 {kicked} · 조기 다이빙 {dives} · 득점 {cases.FindAll(x => x.Scored).Count}");
            Assert.GreaterOrEqual(kicked, 12);
            Assert.Greater(dives, 0);
        }

        [Test]
        public void LivePk_LeadRecordedActual_OffLineFlag()
        {
            foreach (var c in Cases())
            {
                if (c.Shot.Kind != SimEventKind.Shot) continue;
                var actual = (Abs(in c.Shot) - Abs(in c.Keeper)) * 0.01f;
                Assert.AreEqual(actual, c.Keeper.F0, 0.0101f, $"틱 {c.Keeper.Tick}: F0 = 킥 기준 실제 t_e");
                Assert.That(c.Keeper.B, Is.EqualTo(0).Or.EqualTo(1));
                Assert.AreEqual(System.Math.Abs(c.Keeper.F1) >= RoadToPro.MatchEngine.Goalkeeper.GoalkeeperParams.Defaults.PkCenterBand, c.Keeper.A != 0);
            }
        }
    }
}
