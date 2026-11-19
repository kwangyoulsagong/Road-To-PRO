using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;
using static RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper.KeeperLeaveKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>
    /// specs/073 실경기 불변식 (AC-01 손 접촉·AC-02 실경로·AC-08·AC-09). 공유 경기 8개(병렬 1회): 팀 0 = 스위퍼 키퍼, 팀 1 = 전통 골키퍼.
    /// </summary>
    public class KeeperLeaveGoalTests
    {
        public const int Matches = 8;
        public const ulong FirstSeed = 7300UL;
        static MatchSim[] _sims;
        static readonly object Gate = new object();

        public static MatchSim[] Played()
        {
            lock (Gate)
            {
                if (_sims != null) return _sims;
                ShapeTestKit.Setup(seed: 1UL);
                var sims = new MatchSim[Matches];
                System.Threading.Tasks.Parallel.For(0, Matches, m =>
                {
                    var sim = new MatchSim(ShapeTestKit.Setup(seed: FirstSeed + (ulong)m), ShapeTestKit.TuningWithShape());
                    sim.Context.Keepers.Roles[0] = KeeperRole.Sweeper;
                    sim.Context.Keepers.Roles[1] = KeeperRole.Traditional;
                    sim.RunToEnd();
                    sims[m] = sim;
                });
                _sims = sims;
                return sims;
            }
        }

        static int KeeperId(MatchSim sim, int team) => sim.Context.Setup.Team(team).Starters[0];

        /// <summary>이벤트 위치(정규화)가 팀의 자기 박스 안인지 (기간별 공격 방향, 여유 tol m).</summary>
        static bool InOwnBox(MatchSim sim, in SimEvent e, float tol)
        {
            var L = sim.Context.Pitch.LengthM;
            var W = sim.Context.Pitch.WidthM;
            var dir = ((e.Team == 0) ^ ((e.Period & 1) == 1)) ? 1 : -1;
            var goalX = dir > 0 ? 0f : L;
            var depth = (e.X * L - goalX) * dir;
            return depth >= -tol && depth <= RoadToPro.MatchEngine.Match.Stages.PhysicsStage.BoxDepth + tol
                && DMath.Abs(e.Y * W - W * 0.5f) <= RoadToPro.MatchEngine.Match.Stages.PhysicsStage.BoxHalfWidth + tol;
        }

        static bool IsKick(SimEventKind k) => k == SimEventKind.Pass || k == SimEventKind.Shot || k == SimEventKind.Cross || k == SimEventKind.Clearance
            || k == SimEventKind.Punch || k == SimEventKind.Parry || k == SimEventKind.Header || k == SimEventKind.Tackle;

        [Test]
        public void RushOutsideBox_NoHandContact()
        {
            int rush = 0, rushOut = 0, outsideHand = 0, gkOutsideClear = 0;
            foreach (var sim in Played())
            {
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    ref readonly var e = ref log[i];
                    if (e.Kind == SimEventKind.KeeperRush) { rush++; if (e.B == 1) rushOut++; }
                    if (e.Team < 0 || e.PlayerId != KeeperId(sim, e.Team)) continue;
                    if (e.Part == BodyPart.Hand && !InOwnBox(sim, in e, 0.5f) && e.Kind != SimEventKind.HandballOffence)
                    {
                        outsideHand++;
                        TestContext.WriteLine($"박스 밖 손 접촉: {e}");
                    }
                    if (e.Kind == SimEventKind.Clearance && !InOwnBox(sim, in e, 0f)) gkOutsideClear++;
                }
            }
            TestContext.WriteLine($"{Matches}경기: 돌진 {rush} (박스 밖 첫 접촉점 {rushOut}), GK 박스 밖 클리어 {gkOutsideClear}, 박스 밖 손 접촉 {outsideHand}");
            Assert.Greater(rush, 0, "스위퍼 키퍼 경기에 돌진이 있어야 한다");
            Assert.AreEqual(0, outsideHand, "박스 밖 돌진에 손 접촉 없음 (FR-04)");
        }

        /// <summary>AC-02 실경로·AC-08: 크로스 잡기·펀칭은 071 공중볼 경합을 거친다. 잡기 실패 뒤 같은 비행에서 GK 손 잡기 없음, 같은 틱 결과 1개.</summary>
        [Test]
        public void Cross_GoesThroughAerialSystem_SingleOutcome()
        {
            int punch = 0, fail = 0, claimCatch = 0, reCatch = 0, doubled = 0;
            foreach (var sim in Played())
            {
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    ref readonly var e = ref log[i];
                    if (e.Kind == SimEventKind.Header && e.A == (int)RoadToPro.MatchEngine.Actions.HeaderOutcome.Keeper) claimCatch++;
                    var aerialPunch = e.Kind == SimEventKind.Punch && e.B > 0 && e.Part == BodyPart.Hand && e.A == (int)KeeperMotionKind.PunchTwoHands && e.F0 > 1.5f; // 071 경합 펀칭: F0 = 공 속도 (011 슛 펀칭은 F0 = 도달 확률 ≤ 1)
                    if (aerialPunch) punch++;
                    if (e.Kind == SimEventKind.ClaimFail)
                    {
                        fail++;
                        var gk = e.PlayerId;
                        for (int j = i + 1; j < log.Count && log[j].Tick <= e.Tick + 40; j++)
                        {
                            ref readonly var f = ref log[j];
                            if (IsKick(f.Kind)) break;
                            if (f.PlayerId == gk && f.Part == BodyPart.Hand && (f.Kind == SimEventKind.Catch || f.Kind == SimEventKind.PassReceived || f.Kind == SimEventKind.BallRecovered || f.Kind == SimEventKind.Interception)) reCatch++;
                        }
                    }
                    if (aerialPunch || e.Kind == SimEventKind.ClaimFail)
                        for (int j = System.Math.Max(0, i - 30); j < System.Math.Min(log.Count, i + 30); j++)
                            if (log[j].Tick == e.Tick && log[j].Substep == e.Substep && log[j].Kind == SimEventKind.Header && log[j].Team != e.Team) doubled++;
                }
            }
            TestContext.WriteLine($"{Matches}경기: GK 공중볼 잡기 {claimCatch}, 펀칭 {punch}, 잡기 실패 {fail}, 실패 뒤 손 잡기 {reCatch}, 같은 순간 상대 헤더 {doubled}");
            Assert.Greater(claimCatch + punch + fail, 0, "크로스 처리가 071 경합을 거쳐야 한다");
            Assert.AreEqual(0, reCatch, "잡기 실패한 비행에서 GK 손 잡기 없음");
            Assert.AreEqual(0, doubled, "같은 경합 결과는 1개");
        }

        /// <summary>
        /// AC-04 (2026-11-19 플랜 Q1: 역할별 100경기, 1,000경기는 017 보류): 같은 시드 묶음에서 팀 0 = 스위퍼 키퍼, 팀 1 = 전통 골키퍼.
        /// 스위퍼는 분류 기준(박스 밖 수비 행동 ≥ 1.1회/90 또는 평균 수비 행동 거리 ≥ 15야드, 리서치 04 § A4)을 만족하고 전통은 만족하지 않으며,
        /// 스위퍼의 박스 밖 수비 행동이 더 많다. 결과는 `.farm/keeper-roles.json`.
        /// </summary>
        [Test, Explicit("073 AC-04 역할 비교 (100경기)"), Category("Slow"), Timeout(3_600_000)]
        public void SweeperRole_MeetsClassification_Traditional_DoesNot()
        {
            var farm = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".farm");
            var nPath = System.IO.Path.Combine(farm, "feel-n.txt");
            var n = System.IO.File.Exists(nPath) ? int.Parse(System.IO.File.ReadAllText(nPath).Trim()) : 100;
            MatchFeelReportTests.RolesOverride = new[] { KeeperRole.Sweeper, KeeperRole.Traditional };
            MatchFeelMetrics.Totals t;
            try { t = MatchFeelReportTests.Run(n, MatchFeelReportTests.FirstSeed); }
            finally { MatchFeelReportTests.RolesOverride = null; }
            var json = MatchFeelMetrics.ToJson(t, "keeper-roles (team0 Sweeper, team1 Traditional)", MatchFeelReportTests.FirstSeed, "-");
            System.IO.File.WriteAllText(System.IO.Path.Combine(farm, "keeper-roles.json"), json);
            var k = MatchFeelMetrics.Keeper(t);
            double Get(string key) { foreach (var kv in k) if (kv.Key == key) return kv.Value; return double.NaN; }
            var swOut = Get("outsideActs90Team0"); var swDist = Get("avgActDistYdTeam0");
            var trOut = Get("outsideActs90Team1"); var trDist = Get("avgActDistYdTeam1");
            TestContext.WriteLine($"{n}경기: 스위퍼 박스 밖 {swOut:F2}/90·평균 거리 {swDist:F1} yd, 전통 {trOut:F2}/90·{trDist:F1} yd\n{json}");
            Assert.Greater(swOut, trOut, "스위퍼 키퍼가 박스 밖 수비 행동이 더 많다");
            Assert.IsTrue(swOut >= 1.1 || swDist >= 15.0, "스위퍼 분류 기준 만족");
            Assert.IsFalse(trOut >= 1.1 || trDist >= 15.0, "전통 골키퍼는 분류 기준 불만족");
        }

        /// <summary>진단 (Explicit): 결정 틱마다 GK 계획을 읽어 크로스 모드·돌진·1v1 모드 빈도를 센다.</summary>
        [Test, Explicit("073 진단"), Category("Slow")]
        public void Diagnose_KeeperPlans()
        {
            var counts = new int[16];
            for (int m = 0; m < 4; m++)
            {
                var sim = new MatchSim(ShapeTestKit.Setup(seed: FirstSeed + (ulong)m), ShapeTestKit.TuningWithShape());
                sim.Context.Keepers.Roles[0] = KeeperRole.Sweeper;
                sim.Context.Keepers.Roles[1] = KeeperRole.Traditional;
                var lastFlight = new[] { -1, -1 };
                var lastOne = new OneOnOneMode[2];
                while (!sim.IsFinished)
                {
                    sim.StepTicks(1);
                    for (int t = 0; t < 2; t++)
                    {
                        var p = sim.Context.Keepers.Plan(t);
                        if (p.Flight >= 0 && p.Flight != lastFlight[t])
                        {
                            lastFlight[t] = p.Flight;
                            counts[(int)p.Cross]++;
                            if (p.Rush) counts[5]++;
                            counts[6]++;
                        }
                        if (p.OneOnOne != lastOne[t]) { lastOne[t] = p.OneOnOne; counts[8 + (int)p.OneOnOne]++; }
                    }
                }
                foreach (var k in new[] { SimEventKind.KeeperRush, SimEventKind.Smother, SimEventKind.ClaimFail, SimEventKind.Punch, SimEventKind.Catch })
                {
                    var n = 0;
                    for (int i = 0; i < sim.Log.Count; i++) if (sim.Log[i].Kind == k) n++;
                    TestContext.WriteLine($"경기 {m} {k} {n}");
                }
            }
            TestContext.WriteLine($"확정 비행 {counts[6]}: 크로스 없음 {counts[0]} 잡기 {counts[1]} 펀칭 {counts[2]} 포기 {counts[3]} 돌진 {counts[5]} / 1v1 전환 스마더 {counts[9]} 세트 {counts[10]} 해제 {counts[8]}");
        }

        /// <summary>AC-09 (FR-31): G-SW 의도(돌진 종류·지점 일치)를 받아도 EU가 음수인 돌진은 1,000회 동안 고르지 않는다 (010 FR-94 θ_opp 규칙).</summary>
        [Test]
        public void NegativeEuRush_WithIntent_NeverChosen_1000()
        {
            var m = new SweeperModel(P);
            var ch = new KeeperChooser(P);
            var rng = new SeedRng(7309UL);
            var fx = new SeedRng(7310UL);
            var cands = new KeeperCandidate[3];
            var chosen = 0;
            for (int i = 0; i < 1000; i++)
            {
                var x = new Vec2(fx.Range(17f, 35f), fx.Range(10f, 58f));
                var u = m.Utility(fx.Range(0f, 0.1f), fx.Range(0f, 0.1f), m.EmptyXg(x, Goal));
                Assume.That(u, Is.LessThan(0f));
                var intent = new KeeperIntent { Has = true, Kind = KeeperActionKind.Rush, Point = x, InWindow = true };
                cands[0] = new KeeperCandidate { Kind = KeeperActionKind.Position, Point = new Vec2(6f, 34f), Utility = P.ChoicePositionUtility };
                cands[1] = new KeeperCandidate { Kind = KeeperActionKind.Rush, Point = x, Utility = u };
                cands[2] = new KeeperCandidate { Kind = KeeperActionKind.Contest, Point = new Vec2(10f, 34f), Utility = fx.Range(-0.5f, 0.5f) };
                if (ch.Choose(cands, 3, in intent, fx.NextFloat(), rng) == 1) chosen++;
            }
            Assert.AreEqual(0, chosen, "의도는 음수 EU 돌진을 강제하지 않는다");
        }
    }
}
