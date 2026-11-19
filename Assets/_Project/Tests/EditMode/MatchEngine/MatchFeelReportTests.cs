using System.IO;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine
{
    /// <summary>
    /// specs/081 FR-01·02: 같은 시드 묶음(070·071 측정과 같은 7400 + i)·같은 경기 설정(010 전술 경기)으로 N경기 측정 → `.farm/feel-metrics.json`.
    /// N 기본 100, `.farm/feel-n.txt`, 시드 시작 `.farm/feel-seed.txt`(084, 기본 7400), 라벨 `.farm/feel-label.txt`(없으면 "measure"). 판정 없음 — 범위 판정은 /verify 리포트(AC-02).
    /// </summary>
    public class MatchFeelReportTests
    {
        public const ulong FirstSeed = 7400UL;

        /// <summary>specs/012 T27: 지정하면 모든 경기에 이 심판 (`.farm/feel-referee.txt` = 프로필 id).</summary>
        public static RoadToPro.MatchEngine.Referee.RefereeProfile RefereeOverride;

        /// <summary>specs/073 T17: 지정하면 팀별 GK 역할 (AC-04 역할 비교).</summary>
        public static RoadToPro.MatchEngine.Goalkeeper.KeeperRole[] RolesOverride;

        public static MatchFeelMetrics.Totals RunOne(ulong seed)
        {
            var setup = ShapeTestKit.Setup(seed: seed);
            if (RefereeOverride != null) setup.Referee = RefereeOverride; // specs/012 T27: 엄격 상·하위 심판 측정
            var sim = new MatchSim(setup, ShapeTestKit.TuningWithShape());
            if (RolesOverride != null) { sim.Context.Keepers.Roles[0] = RolesOverride[0]; sim.Context.Keepers.Roles[1] = RolesOverride[1]; }
            sim.RunToEnd();
            var t = MatchFeelMetrics.Count(sim.Log);
            MatchFeelMetrics.AddCounters(t, sim.Context);
            MatchFeelMetrics.AddReferee(t, sim);
            MatchFeelMetrics.AddKeepers(t, sim);
            return t;
        }

        /// <summary>n경기 병렬 실행 → 시드 순서대로 합산 (합산 순서 고정 = 같은 결과).</summary>
        public static MatchFeelMetrics.Totals Run(int n, ulong firstSeed)
        {
            ShapeTestKit.Setup(seed: 1UL); // 공유 데이터 캐시를 먼저 채운다 (070·071 측정과 같음)
            var res = new MatchFeelMetrics.Totals[n];
            System.Threading.Tasks.Parallel.For(0, n, i => res[i] = RunOne(firstSeed + (ulong)i));
            var sum = new MatchFeelMetrics.Totals();
            foreach (var r in res) sum.Add(r);
            return sum;
        }

        static string ReadOr(string path, string fallback) => File.Exists(path) ? File.ReadAllText(path).Trim() : fallback;

        [Test, Explicit("리포트용 측정 (081 FR-01·02)"), Category("Slow"), Timeout(7_200_000)]
        public void Report_100Matches()
        {
            var farm = Path.Combine(Directory.GetCurrentDirectory(), ".farm");
            Directory.CreateDirectory(farm);
            var n = int.Parse(ReadOr(Path.Combine(farm, "feel-n.txt"), "100"));
            var label = ReadOr(Path.Combine(farm, "feel-label.txt"), "measure");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var seed = ulong.Parse(ReadOr(Path.Combine(farm, "feel-seed.txt"), FirstSeed.ToString())); // 084: 경계값 재측정용 다른 시드 묶음
            var refId = ReadOr(Path.Combine(farm, "feel-referee.txt"), "");
            RefereeOverride = null;
            if (refId.Length > 0)
                foreach (var pr in RoadToPro.MatchEngine.Referee.RefereeProfile.ParseList(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Data", "referees", "profiles.json"))))
                    if (pr.Id == refId) RefereeOverride = pr;
            var t = Run(n, seed);
            RefereeOverride = null;
            var json = MatchFeelMetrics.ToJson(t, label, seed, ReadOr(Path.Combine(farm, "feel-commit.txt"), "-"));
            File.WriteAllText(Path.Combine(farm, "feel-metrics.json"), json);
            TestContext.WriteLine($"{n}경기 {sw.Elapsed.TotalSeconds:F0}s\n{json}");
            Assert.Greater(t.Passes, 0);
        }

        /// <summary>T04·T11 진단: 라인 밖으로 나간 패스의 xPass·속도·기술·거리·재시작 종류 분포 (판정 없음).</summary>
        [Test, Explicit("진단 (081 T11)"), Category("Slow"), Timeout(1_800_000)]
        public void Diagnose_PassOut_20Matches()
        {
            ShapeTestKit.Setup(seed: 1UL);
            var xp = new double[5]; var xpAll = new double[5]; var spd = new double[5]; var tech = new double[4]; var dist = new double[5]; var kind = new double[3];
            double outN = 0, all = 0, sumXp = 0, sumXpOut = 0, ownRestart = 0;
            for (int m = 0; m < 20; m++)
            {
                var sim = new MatchSim(ShapeTestKit.Setup(seed: FirstSeed + (ulong)m), ShapeTestKit.TuningWithShape());
                sim.RunToEnd();
                var log = sim.Log;
                for (int k = 0; k < log.Count; k++)
                {
                    var e = log[k];
                    if (e.Kind != RoadToPro.MatchEngine.Match.Events.SimEventKind.Pass) continue;
                    all++; sumXp += e.F1;
                    var xb = System.Math.Min(4, (int)(e.F1 * 5)); xpAll[xb]++;
                    if (MatchFeelMetrics.FateOf(log, k) != MatchFeelMetrics.PassFate.Out) continue;
                    outN++; sumXpOut += e.F1; xp[xb]++;
                    if (m == 0 && outN <= 6)
                    {
                        var sb = new System.Text.StringBuilder($"[밖 {outN}] ");
                        for (int j = k; j < log.Count && j < k + 12; j++)
                        {
                            var r = log[j];
                            sb.Append($"{r.Kind}(t{r.Tick} T{r.Team} p{r.PlayerId}→{r.OtherId} {r.X * 105:F0},{r.Y * 68:F0}) ");
                            if (r.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.ThrowIn) break;
                        }
                        TestContext.WriteLine(sb.ToString());
                    }
                    spd[System.Math.Min(4, (int)(e.F0 / 6f))]++;
                    tech[e.A & 3]++;
                    for (int j = k + 1; j < log.Count; j++)
                    {
                        var r = log[j];
                        if (r.Kind != RoadToPro.MatchEngine.Match.Events.SimEventKind.ThrowIn && r.Kind != RoadToPro.MatchEngine.Match.Events.SimEventKind.GoalKick && r.Kind != RoadToPro.MatchEngine.Match.Events.SimEventKind.Corner) continue;
                        kind[r.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.ThrowIn ? 0 : r.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.GoalKick ? 1 : 2]++;
                        if (r.Team == e.Team) ownRestart++;
                        var d = System.Math.Sqrt(System.Math.Pow((r.X - e.X) * 105.0, 2) + System.Math.Pow((r.Y - e.Y) * 68.0, 2));
                        dist[System.Math.Min(4, (int)(d / 10.0))]++;
                        break;
                    }
                }
            }
            string P(double[] a, double n) { var sb = new System.Text.StringBuilder(); foreach (var v in a) sb.Append($"{(n > 0 ? v / n : 0):P0} "); return sb.ToString(); }
            TestContext.WriteLine($"패스 {all / 20:F0}/경기, 라인 밖 {outN / all:P0}, 평균 xPass 전체 {sumXp / all:F2} · 라인 밖 {sumXpOut / outN:F2}");
            TestContext.WriteLine($"xPass 구간(0~.2….8~1) 전체 {P(xpAll, all)}| 라인 밖 {P(xp, outN)}");
            TestContext.WriteLine($"출발 속도(0~6,6~12,12~18,18~24,24+ m/s) {P(spd, outN)}");
            TestContext.WriteLine($"기술(Inside·Instep·Outside·Chip) {P(tech, outN)}");
            TestContext.WriteLine($"출발→재시작 거리(0~10…40+ m) {P(dist, outN)}");
            TestContext.WriteLine($"재시작(스로인·골킥·코너) {P(kind, outN)}, 우리 팀 재시작 {ownRestart / outN:P0}");
            Assert.Greater(all, 0);
        }

        /// <summary>T11 진단: 득점 직전 공 사건 종류·자책, 슈팅별 첫 결과 (판정 없음).</summary>
        [Test, Explicit("진단 (081 T11)"), Category("Slow"), Timeout(1_800_000)]
        public void Diagnose_GoalsAndShots_20Matches()
        {
            ShapeTestKit.Setup(seed: 1UL);
            var before = new System.Collections.Generic.SortedDictionary<string, int>();
            var shotOut = new System.Collections.Generic.SortedDictionary<string, int>();
            int goals = 0, own = 0, shots = 0;
            void Inc(System.Collections.Generic.SortedDictionary<string, int> d, string k) { d.TryGetValue(k, out var v); d[k] = v + 1; }
            for (int m = 0; m < 20; m++)
            {
                var sim = new MatchSim(ShapeTestKit.Setup(seed: FirstSeed + (ulong)m), ShapeTestKit.TuningWithShape());
                sim.RunToEnd();
                var log = sim.Log;
                for (int k = 0; k < log.Count; k++)
                {
                    var e = log[k];
                    if (e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Goal)
                    {
                        goals++;
                        if (e.A == 1) own++;
                        if (m < 2 && goals <= 40 && (e.A == 1 || true))
                        {
                            var sq = new System.Text.StringBuilder($"[골 {goals} T{e.Team} own{e.A} t{e.Tick} {e.X * 105:F1},{e.Y * 68:F1}] ");
                            for (int j = System.Math.Max(0, k - 7); j < k; j++) { var b0 = log[j]; sq.Append($"{b0.Kind}(T{b0.Team} p{b0.PlayerId} t{b0.Tick} {b0.X * 105:F0},{b0.Y * 68:F0}) "); }
                            TestContext.WriteLine(sq.ToString());
                        }
                        for (int j = k - 1; j >= 0; j--)
                        {
                            var b = log[j];
                            var kk = (int)b.Kind;
                            if (kk >= 200 && kk < 400 && b.Kind != RoadToPro.MatchEngine.Match.Events.SimEventKind.Contact && b.Kind != RoadToPro.MatchEngine.Match.Events.SimEventKind.Carry
                                && b.Kind != RoadToPro.MatchEngine.Match.Events.SimEventKind.DiveStart && b.Kind != RoadToPro.MatchEngine.Match.Events.SimEventKind.Header)
                            { Inc(before, $"{b.Kind}{(b.Part == RoadToPro.MatchEngine.Match.Events.BodyPart.Head ? "(머리)" : "")}{(b.Team == e.Team ? "" : "·상대")}"); break; }
                        }
                    }
                    else if (e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Shot)
                    {
                        shots++;
                        var res = "끝";
                        for (int j = k + 1; j < log.Count; j++)
                        {
                            var r = log[j];
                            var kk = (int)r.Kind;
                            if (r.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Contact || r.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.DiveStart || r.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.KeeperRush || r.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.PhaseChange) continue;
                            if (kk >= 100) { res = r.Kind.ToString(); break; }
                        }
                        Inc(shotOut, res);
                    }
                }
            }
            var sb = new System.Text.StringBuilder($"득점 {goals / 20.0:F1}/경기 (자책 {own}), 슈팅 {shots / 20.0:F1}/경기\n득점 직전: ");
            foreach (var kv in before) sb.Append($"{kv.Key} {kv.Value * 100.0 / goals:F0}% ");
            sb.Append("\n슈팅 첫 결과: ");
            foreach (var kv in shotOut) sb.Append($"{kv.Key} {kv.Value * 100.0 / shots:F0}% ");
            TestContext.WriteLine(sb.ToString());
            Assert.Greater(shots, 0);
        }

        /// <summary>T12a 진단: 슈팅 없는 득점 직전 4 s를 틱 단위로 다시 재생 (소유자·공·결정·GK). 판정 없음.</summary>
        [Test, Explicit("진단 (081 T12a)"), Category("Slow"), Timeout(1_800_000)]
        public void Diagnose_NoShotGoal_Trace()
        {
            ShapeTestKit.Setup(seed: 1UL);
            var shown = 0;
            for (int m = 0; m < 6 && shown < 4; m++)
            {
                var seed = FirstSeed + (ulong)m;
                var first = new MatchSim(ShapeTestKit.Setup(seed: seed), ShapeTestKit.TuningWithShape());
                first.RunToEnd();
                var targets = new System.Collections.Generic.List<long>();
                var log = first.Log;
                for (int k = 0; k < log.Count; k++)
                    if (log[k].Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Goal)
                    {
                        var shot = false;
                        for (int j = k - 1; j >= 0 && log[j].Tick >= log[k].Tick - 30; j--) if (log[j].Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Shot) { shot = true; break; }
                        if (!shot) targets.Add(log[k].Tick);
                    }
                if (targets.Count == 0) continue;
                var goalTick = targets[0];
                var sim = new MatchSim(ShapeTestKit.Setup(seed: seed), ShapeTestKit.TuningWithShape());
                var ctx = sim.Context;
                var st = sim.Stage(RoadToPro.MatchEngine.Match.Pipeline.StageId.ScenarioOrUtility);
                var us = st is RoadToPro.MatchEngine.Match.Stages.ScenarioStage sc ? (RoadToPro.MatchEngine.Match.Stages.UtilityDecisionStage)sc.DecisionStage : (RoadToPro.MatchEngine.Match.Stages.UtilityDecisionStage)st;
                var sb = new System.Text.StringBuilder($"\n== 시드 {seed} 슈팅 없는 득점 틱 {goalTick} (이 경기 {targets.Count}건) ==\n");
                while (ctx.Tick < goalTick + 1 && sim.StepTick())
                {
                    if (ctx.Tick < goalTick - 40) continue;
                    ref readonly var b = ref ctx.Ball;
                    var o = b.Phase == RoadToPro.MatchEngine.Ball.BallPhase.Controlled ? b.OwnerId : ctx.LastTouchSlot;
                    var team = o >= 0 ? Squad.TeamOfSlot(o) : -1;
                    var gk0 = ctx.SlotState(0).Position; var gk1 = ctx.SlotState(11).Position;
                    sb.Append($"t{ctx.Tick} {b.Phase} ball {b.Position.X:F1},{b.Position.Y:F1},{b.Position.Z:F1} v {b.Velocity.X:F1},{b.Velocity.Y:F1} ");
                    if (o >= 0)
                    {
                        var p = ctx.SlotState(o).Position; var d = us.DecisionOf(o);
                        sb.Append($"| s{o}(T{team} dir{ctx.AttackDir(team)}) {p.X:F1},{p.Y:F1} {d.Kind}→{d.Point.X:F1},{d.Point.Y:F1} gap{(ctx.Actions.Duels.GapOpenFor(o) ? 1 : 0)} ");
                    }
                    sb.Append($"| GK0 {gk0.X:F1},{gk0.Y:F1} GK1 {gk1.X:F1},{gk1.Y:F1} flow {ctx.Flow.State}\n");
                }
                for (int k = 0; k < sim.Log.Count; k++) { var e = sim.Log[k]; if (e.Tick >= goalTick - 40 && e.Tick <= goalTick && (int)e.Kind >= 200) sb.Append($"  ev t{e.Tick} {e.Kind} T{e.Team} p{e.PlayerId}→{e.OtherId} A{e.A} B{e.B}\n"); }
                TestContext.WriteLine(sb.ToString());
                shown++;
            }
            Assert.Pass();
        }
    }
}
