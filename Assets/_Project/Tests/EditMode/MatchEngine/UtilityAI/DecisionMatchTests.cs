using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Tactics;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Scenarios;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>모든 결정(선수·틱·선택·점수·출처)을 해시하고 활동을 구간별로 센다 (읽기 전용).</summary>
    public sealed class DecisionRecorder : IDecisionDiagnostics
    {
        StableHasher _h = StableHasher.Create();
        public ulong Hash => _h.Value;
        public long Decisions;
        public int BadSteps;
        readonly Intensity[] _prev = new Intensity[22];
        readonly bool[] _has = new bool[22];
        public int Team = 0;
        public long FromTick = long.MinValue, ToTick = long.MaxValue;
        public long OffBall, Press, Sprint;
        public long GegenWindowDecisions, GegenWindowPress;

        public void OnDecision(long tick, int slot, in PlayerDecisionState st, OffBallDecider decider, DecisionWorld world)
        {
            Decisions++;
            _h.Add(tick); _h.Add(slot); _h.Add((byte)st.Kind); _h.Add((byte)st.Source); _h.Add(st.Point.X); _h.Add(st.Point.Y); _h.Add((byte)st.Intensity);
            if (decider == null) { _has[slot] = false; return; }
            _h.Add(decider.Chooser.Score(decider.LastPick));
            if (_has[slot] && !decider.LastTrigger && Math.Abs((int)st.Intensity - (int)_prev[slot]) > 1) BadSteps++;
            _prev[slot] = st.Intensity;
            _has[slot] = true;
            if (DecisionWorld.TeamOf(slot) != Team || tick < FromTick || tick >= ToTick) return;
            OffBall++;
            if (st.Kind == Consideration.Press) Press++;
            if (st.Intensity == Intensity.Sprint) Sprint++;
            if (world.GegenActive[Team]) { GegenWindowDecisions++; if (st.Kind == Consideration.Press) GegenWindowPress++; }
        }

        public void OnTick(long tick, DecisionWorld world, int spaceQueries) { }

        /// <summary>072 온볼 결정 (AC-11 해시: 선수·틱·후보 집합·점수·선택).</summary>
        public long OnBallDecisions;
        public void OnOnBallDecision(long tick, int slot, in OnBallResult r, OnBallDecider decider, DecisionWorld world)
        {
            OnBallDecisions++;
            _h.Add(tick); _h.Add(slot); _h.Add((byte)r.Item); _h.Add(r.Target); _h.Add(r.Point.X); _h.Add(r.Point.Y); _h.Add(r.IgnoredInstruction);
            var c = decider.Candidates;
            _h.Add(c.Count);
            for (int i = 0; i < c.Count; i++) { _h.Add((byte)c[i].Item); _h.Add(c[i].Target); _h.Add(decider.Chooser.Score(i)); }
        }
    }

    /// <summary>specs/010 T14: 결정론(AC-15·16·19)·90분 강도(AC-21)·전술 → 운동량(AC-34·35·36)·시나리오 0개 기준선 측정(AC-33 보류).</summary>
    public class DecisionMatchTests
    {
        const int Matches = 100;

        static UtilityDecisionStage StageOf(MatchSim sim) => (UtilityDecisionStage)sim.Stage(StageId.ScenarioOrUtility);

        static MatchSim ScenarioSim(MatchSimOptions o = null, ulong seed = 20261109UL, MatchSetup setup = null) =>
            new MatchSim(setup ?? ShapeTestKit.Setup(seed: seed), ScenarioTestKit.MatchTuningOf(ScenarioTestKit.DataOf(ScenarioTestKit.Samples())), o);

        [Test, Category("Determinism")]
        public void Determinism_90min_DecisionHashSame_AC15()
        {
            DecisionRecorder ra = null, rb = null;
            var a = ScenarioSim(new MatchSimOptions { DecisionDiagnostics = () => ra = new DecisionRecorder() }).RunToEnd();
            var b = ScenarioSim(new MatchSimOptions { DecisionDiagnostics = () => rb = new DecisionRecorder() }).RunToEnd();
            Assert.Greater(ra.Decisions, 100_000);
            Assert.AreEqual(ra.Hash, rb.Hash, "결정 해시");
            Assert.AreEqual(a.EventLogHash, b.EventLogHash);
            Assert.AreEqual(a.FinalStateHash, b.FinalStateHash);
            Assert.AreEqual(0, ra.BadSteps, "트리거 없는 결정에서 강도 두 단계 이상 변화 0 (AC-21)");
        }

        [Test, Category("Determinism")]
        public void Determinism_ShuffledRosterOrder_SameHash_AC15()
        {
            var s1 = ShapeTestKit.Setup();
            var s2 = s1.Clone();
            Array.Reverse(s2.Home.Players);
            Array.Reverse(s2.Away.Players);
            DecisionRecorder ra = null, rb = null;
            var a = ScenarioSim(new MatchSimOptions { DecisionDiagnostics = () => ra = new DecisionRecorder() }, setup: s1);
            var b = ScenarioSim(new MatchSimOptions { DecisionDiagnostics = () => rb = new DecisionRecorder() }, setup: s2);
            a.StepTicks(9000);
            b.StepTicks(9000);
            Assert.AreEqual(ra.Hash, rb.Hash);
            Assert.AreEqual(a.StateHash(), b.StateHash());
        }

        [Test, Category("Determinism")]
        public void Determinism_TelemetryOnOff_SameHash_AC16_AC19()
        {
            var on = ScenarioSim(new MatchSimOptions { Checkpoints = CheckpointMode.All, DecisionDiagnostics = () => new DecisionTelemetry(256) }).RunToEnd();
            var off = ScenarioSim(new MatchSimOptions { Checkpoints = CheckpointMode.All }).RunToEnd();
            Assert.AreEqual(off.EventLogHash, on.EventLogHash, "이벤트 로그 해시");
            Assert.AreEqual(off.FinalStateHash, on.FinalStateHash);
            Assert.That(CheckpointLog.FindFirstDivergence(off.Checkpoints, on.Checkpoints), Is.Null, "체크포인트 해시");
        }

        // ── 전술 → 운동량 (100경기씩, Slow) ──

        struct TeamStats
        {
            public long OffBall, Press, Sprint, GegenDec, GegenPress, EarlyOff, EarlySprint, LateOff, LateSprint;
            public List<double> HiDist, FinalC, EarlyShare, LateShare;
            public double Goals, Shots;
        }

        static MatchTuning NoFatigue()
        {
            var t = ShapeTestKit.TuningWithShape();
            var kin = TuningLoader.ParseBundle(t.Kinematics.Registry, MatchTestKit.Values(
                ("kin.fatigue.chronicDistRate", 0f), ("kin.fatigue.chronicHirRate", 0f), ("kin.fatigue.acuteDrain", 0f)));
            return new MatchTuning(t.Ball, kin, t.Match, t.Shape, t.Scenario, t.Goalkeeper, t.Decision);
        }

        delegate void Tweak(ref TeamInstructions i);

        sealed class One
        {
            public long OffBall, Press, Sprint, GegenDec, GegenPress, EarlyOff, EarlySprint, LateOff, LateSprint;
            public double Hi, C, Goals;
        }

        static One RunOne(Tweak home, int i, MatchTuning tuning)
        {
            var setup = ShapeTestKit.Setup(seed: 9100UL + (ulong)i);
            var ins = setup.Home.Tactics.Instructions;
            home(ref ins);
            setup.Home.Tactics.Instructions = ins;
            DecisionRecorder all = null, early = null, late = null;
            DecisionTelemetry tel = null;
            var fan = new FanOut();
            var o = new MatchSimOptions
            {
                DecisionDiagnostics = () =>
                {
                    all = new DecisionRecorder();
                    early = new DecisionRecorder { ToTick = 15 * 600 };
                    late = new DecisionRecorder { FromTick = 80 * 600 };
                    tel = new DecisionTelemetry(1);
                    fan.Set(all, early, late, tel);
                    return fan;
                },
            };
            var sim = new MatchSim(setup, tuning, o);
            var r = sim.RunToEnd();
            var one = new One
            {
                OffBall = all.OffBall, Press = all.Press, Sprint = all.Sprint, GegenDec = all.GegenWindowDecisions, GegenPress = all.GegenWindowPress,
                EarlyOff = early.OffBall, EarlySprint = early.Sprint, LateOff = late.OffBall, LateSprint = late.Sprint,
                Goals = r.RegularGoals[0] + r.RegularGoals[1],
            };
            int cn = 0;
            var w = StageOf(sim).World;
            for (int s = 1; s < 11; s++)
            {
                one.Hi += tel.HighIntensitySeconds[s];
                if (w.Active[s]) { one.C += w.C[s]; cn++; }
            }
            one.C = cn > 0 ? one.C / cn : 0;
            return one;
        }

        /// <summary>경기 n개를 병렬로 돌리고 경기 번호 순으로 합친다 (경기 하나는 단일 스레드, 006 FR-71).</summary>
        static TeamStats Run(Tweak home, int n, MatchTuning tuning = null)
        {
            var tu = tuning ?? ShapeTestKit.TuningWithShape();
            ShapeTestKit.Setup(seed: 1UL); // 정적 캐시(포메이션·튜닝)를 스레드 시작 전에 채운다
            var res = new One[n];
            System.Threading.Tasks.Parallel.For(0, n, i => res[i] = RunOne(home, i, tu));
            var st = new TeamStats { HiDist = new List<double>(), FinalC = new List<double>(), EarlyShare = new List<double>(), LateShare = new List<double>() };
            foreach (var x in res)
            {
                st.OffBall += x.OffBall; st.Press += x.Press; st.Sprint += x.Sprint;
                st.GegenDec += x.GegenDec; st.GegenPress += x.GegenPress;
                st.EarlyOff += x.EarlyOff; st.EarlySprint += x.EarlySprint;
                st.LateOff += x.LateOff; st.LateSprint += x.LateSprint;
                st.HiDist.Add(x.Hi);
                st.FinalC.Add(x.C);
                st.EarlyShare.Add(x.EarlyOff > 0 ? (double)x.EarlySprint / x.EarlyOff : 0);
                st.LateShare.Add(x.LateOff > 0 ? (double)x.LateSprint / x.LateOff : 0);
                st.Goals += x.Goals;
            }
            st.Goals /= n;
            return st;
        }

        sealed class FanOut : IDecisionDiagnostics
        {
            IDecisionDiagnostics[] _d = Array.Empty<IDecisionDiagnostics>();
            public void Set(params IDecisionDiagnostics[] d) => _d = d;
            public void OnDecision(long tick, int slot, in PlayerDecisionState state, OffBallDecider decider, DecisionWorld world)
            {
                for (int i = 0; i < _d.Length; i++) _d[i].OnDecision(tick, slot, in state, decider, world);
            }
            public void OnTick(long tick, DecisionWorld world, int q)
            {
                for (int i = 0; i < _d.Length; i++) _d[i].OnTick(tick, world, q);
            }
            public void OnOnBallDecision(long tick, int slot, in OnBallResult r, OnBallDecider decider, DecisionWorld world)
            {
                for (int i = 0; i < _d.Length; i++) _d[i].OnOnBallDecision(tick, slot, in r, decider, world);
            }
        }

        /// <summary>대응 t (a_i − b_i의 평균 / 표준오차). 경기가 분석 단위 — 경기 안 결정은 서로 상관된다.</summary>
        static double PairedT(List<double> a, List<double> b)
        {
            int n = a.Count;
            double m = 0, v = 0;
            for (int i = 0; i < n; i++) m += a[i] - b[i];
            m /= n;
            for (int i = 0; i < n; i++) { var d = a[i] - b[i] - m; v += d * d; }
            v /= Math.Max(1, n - 1);
            var se = Math.Sqrt(v / n);
            return se <= 0 ? 0 : m / se;
        }

        static double WelchT(List<double> a, List<double> b)
        {
            double ma = 0, mb = 0, va = 0, vb = 0;
            foreach (var x in a) ma += x; ma /= a.Count;
            foreach (var x in b) mb += x; mb /= b.Count;
            foreach (var x in a) va += (x - ma) * (x - ma); va /= Math.Max(1, a.Count - 1);
            foreach (var x in b) vb += (x - mb) * (x - mb); vb /= Math.Max(1, b.Count - 1);
            var se = Math.Sqrt(va / a.Count + vb / b.Count);
            return se <= 0 ? 0 : (ma - mb) / se;
        }

        static TeamStats _high, _low, _regroup, _noFatigue;
        static TeamStats High => _high.HiDist != null ? _high : (_high = Run((ref TeamInstructions i) => { i.PressIntensity = 0.95f; i.OnLoss = OnLossMode.CounterPress; }, Matches));
        static TeamStats Low => _low.HiDist != null ? _low : (_low = Run((ref TeamInstructions i) => { i.PressIntensity = 0.1f; i.OnLoss = OnLossMode.CounterPress; }, Matches));
        static TeamStats Regroup => _regroup.HiDist != null ? _regroup : (_regroup = Run((ref TeamInstructions i) => { i.PressIntensity = 0.95f; i.OnLoss = OnLossMode.Regroup; }, Matches));
        static TeamStats HighNoFatigue => _noFatigue.HiDist != null ? _noFatigue : (_noFatigue = Run((ref TeamInstructions i) => { i.PressIntensity = 0.95f; i.OnLoss = OnLossMode.CounterPress; }, Matches, NoFatigue()));

        [Test, Category("Slow"), Timeout(3_600_000)]
        public void Tactics_PressIntensityHighLow_100Matches_AC34()
        {
            var h = High;
            var l = Low;
            var zp = Stat.Z((int)h.Press, (int)h.OffBall, (int)l.Press, (int)l.OffBall);
            var zs = Stat.Z((int)h.Sprint, (int)h.OffBall, (int)l.Sprint, (int)l.OffBall);
            var th = WelchT(h.HiDist, l.HiDist);
            var tc = WelchT(h.FinalC, l.FinalC);
            TestContext.WriteLine($"Press 비율 높음 {(double)h.Press / h.OffBall:P2} vs 낮음 {(double)l.Press / l.OffBall:P2} (z {zp:F1})");
            TestContext.WriteLine($"질주 비율 {(double)h.Sprint / h.OffBall:P2} vs {(double)l.Sprint / l.OffBall:P2} (z {zs:F1}), 고강도 시간 t {th:F1}, 종료 C t {tc:F1}");
            Assert.Greater(zp, Stat.Z01, "Press 결정 비율");
            Assert.Greater(zs, Stat.Z01, "질주 결정 비율");
            Assert.Greater(th, 2.6, "고강도 시간");
            Assert.Greater(tc, 2.6, "경기 종료 만성 피로 C (T14a 강도 보정 뒤 포화가 풀려 판정 — 2026-11-12 보류 해제)");
        }

        [Test, Category("Slow"), Timeout(3_600_000)]
        public void Gegenpress_OnOff_WindowPressers_AC35()
        {
            var on = High;
            var off = Regroup;
            Assert.Greater(on.GegenDec, 100, "창 안 결정 표본");
            Assert.AreEqual(0, off.GegenDec, "Regroup이면 창 없음");
            var zon = Stat.Z((int)on.Press, (int)on.OffBall, (int)off.Press, (int)off.OffBall);
            TestContext.WriteLine($"창 안 Press 비율 {(double)on.GegenPress / on.GegenDec:P2}, 전체 Press 켬 {(double)on.Press / on.OffBall:P2} vs 끔 {(double)off.Press / off.OffBall:P2} (z {zon:F1})");
            Assert.Greater(zon, Stat.Z01, "게겐프레싱 켬 쪽 압박 비율");
        }

        [Test]
        public void Gegenpress_RegainEndsWindow_AC35()
        {
            var setup = ShapeTestKit.Setup();
            var ins = setup.Home.Tactics.Instructions; ins.OnLoss = OnLossMode.CounterPress; setup.Home.Tactics.Instructions = ins;
            var sim = ShapeTestKit.Sim(setup: setup);
            var stage = StageOf(sim);
            int windows = 0;
            for (int k = 0; k < 9000; k++)
            {
                sim.StepTick();
                var w = stage.World;
                for (int t = 0; t < 2; t++)
                {
                    if (w.GegenActive[t]) windows++;
                    if (w.PossTeam == t) Assert.IsFalse(w.GegenActive[t], $"tick {k}: 소유 중 창 없음");
                }
            }
            Assert.Greater(windows, 0);
        }

        [Test, Category("Slow"), Timeout(3_600_000)]
        public void Fatigue_LastVsFirst15_SprintDrops_OffNotSignificant_AC36()
        {
            var h = High;
            var nf = HighNoFatigue;
            // 분석 단위 = 경기 (100경기 대응 t, df 99 단측 0.01 ≈ 2.36). 결정 단위 z는 경기 안 상관 때문에 작은 차이도 유의하게 나온다.
            var t = PairedT(h.EarlyShare, h.LateShare);
            var tOff = PairedT(nf.EarlyShare, nf.LateShare);
            var early = (double)h.EarlySprint / h.EarlyOff;
            var late = (double)h.LateSprint / h.LateOff;
            var e0 = (double)nf.EarlySprint / nf.EarlyOff;
            var l0 = (double)nf.LateSprint / nf.LateOff;
            TestContext.WriteLine($"질주 비율 첫 15분 {early:P2} → 마지막 15분 {late:P2} (감소 {(early - late) / early:P1}, 대응 t {t:F1}); 피로 끔 {e0:P2} → {l0:P2} (대응 t {tOff:F1})");
            Assert.Greater(t, 2.36, "마지막 15분 질주 감소 (경기 단위 p < 0.01)");
            Assert.Less(tOff, 2.36, "피로를 끈 대조에서는 유의한 감소 없음 (경기 단위)");
        }

        [Test, Category("Slow"), Timeout(3_600_000)]
        public void Baseline_NoScenario_100Matches_Recorded_AC33()
        {
            var b = Run((ref TeamInstructions i) => { }, Matches);
            TestContext.WriteLine($"[AC-33 보류] 시나리오 0개 기준선 {Matches}경기 평균 득점 {b.Goals:F2} (072·008·070·071·012 연결 전 대체 온볼 수치)");
            Assert.Greater(b.OffBall, 0);
        }

        [Test, Explicit("리포트용 운동량 측정")]
        public void Report_Workload()
        {
            foreach (var press in new[] { 0.1f, 0.95f })
            {
                var setup = ShapeTestKit.Setup(seed: 9100UL);
                var ins = setup.Home.Tactics.Instructions; ins.PressIntensity = press; setup.Home.Tactics.Instructions = ins;
                DecisionTelemetry tel = null;
                var sim = new MatchSim(setup, ShapeTestKit.TuningWithShape(), new MatchSimOptions { DecisionDiagnostics = () => tel = new DecisionTelemetry(1) });
                var stage = StageOf(sim);
                var line = "";
                for (int m = 15; m <= 90; m += 15)
                {
                    sim.RunUntilTick(m * 600);
                    double c = 0; double d = 0;
                    for (int s = 1; s < 11; s++) { c += stage.World.C[s]; d += tel.Distance[s]; }
                    line += $" {m}′ C={c / 10:F2} 거리={d / 10 / 1000:F2}km";
                }
                double hi = 0, sp = 0; long sd = 0, ob = 0;
                for (int s = 1; s < 11; s++) { hi += tel.HighIntensitySeconds[s]; sp += tel.SprintDistance[s]; sd += tel.SprintDecisions[s]; ob += tel.OffBallDecisions[s]; }
                TestContext.WriteLine($"압박 {press}: {line} | 고강도 {hi / 10 / 60:F1}분/명, 질주 거리 {sp / 10:F0} m/명, 질주 결정 {100.0 * sd / ob:F1} %");
            }
        }

        [Test, Explicit("리포트용 속도 구간 분석")]
        public void Report_SpeedBreakdown()
        {
            var sim = ShapeTestKit.Sim(setup: ShapeTestKit.Setup(seed: 9100UL));
            var stage = StageOf(sim);
            var bins = new double[4];
            var bySrc = new double[8];
            var byKind = new double[Considerations.Count];
            var byDec = new double[8];
            double dead = 0, all = 0, anchorMove = 0, anchorSamples = 0, holdDec = 0, settledDec = 0, errSum = 0;
            var prevAnchor = new RoadToPro.Core.Math.Vec2[22];
            var jumpHist = new long[6];
            var bigLog = "";
            while (sim.StepTick() && sim.View.Tick < 54000)
            {
                var w = stage.World;
                for (int s = 1; s < 22; s++)
                {
                    if (s == 11 || !w.Active[s]) continue;
                    if (sim.View.State == RoadToPro.MatchEngine.Match.Flow.FlowState.InPlay && w.HasShape[s] && prevAnchor[s] != default)
                    {
                        var jd = (w.Anchor[s] - prevAnchor[s]).Length();
                        anchorMove += jd; anchorSamples++;
                        jumpHist[jd < 0.2f ? 0 : jd < 0.5f ? 1 : jd < 1f ? 2 : jd < 3f ? 3 : jd < 10f ? 4 : 5]++;
                        if (jd >= 10f && bigLog.Length < 600) bigLog += $" t{w.Tick}s{s}:{jd:F0}m";
                    }
                    prevAnchor[s] = w.Anchor[s];
                    ref readonly var st = ref stage.State(s);
                    if (st.LastDecisionTick == w.Tick && st.Source == DecisionSource.General && st.Kind == Consideration.HoldShape) { holdDec++; if (st.Settled) settledDec++; errSum += (w.Anchor[s] - w.Pos[s]).Length(); }
                }
                var inPlay = sim.View.State == RoadToPro.MatchEngine.Match.Flow.FlowState.InPlay;
                for (int s = 1; s < 22; s++)
                {
                    if (s == 11 || !w.Active[s]) continue;
                    var v = w.Vel[s].Length();
                    var dd = v * 0.1;
                    all += dd;
                    if (!inPlay) { dead += dd; continue; }
                    bins[v < 1.5f ? 0 : v < 3.5f ? 1 : v < 5.5f ? 2 : 3] += dd;
                    bySrc[(int)stage.State(s).Source] += dd;
                    byDec[(int)stage.DecisionOf(s).Kind] += dd;
                    if (stage.State(s).Source <= DecisionSource.Opportunistic) byKind[(int)stage.State(s).Kind] += dd;
                }
            }
            var n = 20.0;
            TestContext.WriteLine($"선수당 거리 {all / n / 1000:F2} km, 데드볼 중 {dead / n / 1000:F2} km");
            TestContext.WriteLine($"앵커 이동 평균 {anchorMove / anchorSamples * 10:F2} m/s, 형태 유지 결정 중 서 있음 {100 * settledDec / holdDec:F1} %, 앵커 오차 평균 {errSum / holdDec:F2} m");
            TestContext.WriteLine($"앵커 틱당 이동 분포 <0.2 {jumpHist[0]} / <0.5 {jumpHist[1]} / <1 {jumpHist[2]} / <3 {jumpHist[3]} / <10 {jumpHist[4]} / ≥10 {jumpHist[5]} |{bigLog}");
            TestContext.WriteLine($"인플레이 속도 구간(거리) <1.5 {bins[0] / n / 1000:F2} / 1.5~3.5 {bins[1] / n / 1000:F2} / 3.5~5.5 {bins[2] / n / 1000:F2} / >5.5 {bins[3] / n / 1000:F2} km");
            var src = ""; for (int i = 0; i < 8; i++) if (bySrc[i] > 0) src += $" {(DecisionSource)i}={bySrc[i] / n / 1000:F2}";
            TestContext.WriteLine("출처별 km:" + src);
            var dk = ""; for (int i = 0; i < 8; i++) if (byDec[i] > 0) dk += $" {(RoadToPro.MatchEngine.Match.Pipeline.DecisionKind)i}={byDec[i] / n / 1000:F2}";
            TestContext.WriteLine("결정 종류별 km:" + dk);
            var kk = ""; for (int i = 0; i < byKind.Length; i++) if (byKind[i] > 0) kk += $" {(Consideration)i}={byKind[i] / n / 1000:F2}";
            TestContext.WriteLine("오프볼 행동별 km:" + kk);
        }

        [Test, Explicit("리포트용 국면 깜빡임")]
        public void Report_PhaseFlicker()
        {
            var sim = ShapeTestKit.Sim(setup: ShapeTestKit.Setup(seed: 9100UL));
            var stage = StageOf(sim);
            var line = "";
            RoadToPro.MatchEngine.Tactics.TeamPhase prev = (RoadToPro.MatchEngine.Tactics.TeamPhase)255;
            int prevOwner = -9, changes = 0, ownerChanges = 0;
            while (sim.StepTick() && sim.View.Tick < 3000)
            {
                var w = stage.World;
                if (w.Phase[0] != prev) { changes++; if (line.Length < 1500) line += $" t{w.Tick}:{w.Phase[0]}/o{w.Owner}"; prev = w.Phase[0]; }
                if (w.Owner != prevOwner) { ownerChanges++; prevOwner = w.Owner; }
            }
            TestContext.WriteLine($"3000틱 국면 변화 {changes}, 소유자 변화 {ownerChanges} |{line}");
        }

        static MatchTuning WithDecision(params (string id, float v)[] kv)
        {
            var t = ShapeTestKit.TuningWithShape();
            var dec = TuningLoader.ParseBundle(MatchTestKit.DecisionTuning().Registry, MatchTestKit.Values(kv));
            return new MatchTuning(t.Ball, t.Kinematics, t.Match, t.Shape, t.Scenario, t.Goalkeeper, dec);
        }

        [Test, Explicit("리포트용 득점 원인 분리"), Timeout(3_600_000)]
        public void Report_GoalsBisect()
        {
            var cases = new (string name, MatchTuning tu)[]
            {
                ("데드볼 상한 끔", WithDecision(("dec.deadball.cap", 0f))),
                ("강도 값 모두 이전", WithDecision(("dec.intensity.walk", 0.3f), ("dec.intensity.jog", 0.5f), ("dec.intensity.run", 0.75f), ("dec.intensity.walkDist", 1.5f), ("dec.intensity.jogDist", 6f), ("dec.settle.in", 0f), ("dec.settle.out", 0f))),
                ("강도 이전 + 데드볼 상한 끔", WithDecision(("dec.intensity.walk", 0.3f), ("dec.intensity.jog", 0.5f), ("dec.intensity.run", 0.75f), ("dec.intensity.walkDist", 1.5f), ("dec.intensity.jogDist", 6f), ("dec.settle.in", 0f), ("dec.settle.out", 0f), ("dec.deadball.cap", 0f))),
            };
            foreach (var c in cases)
            {
                var st = Run((ref TeamInstructions i) => { }, 40, c.tu);
                TestContext.WriteLine($"{c.name}: 평균 득점 {st.Goals:F2}");
            }
        }

        [Test, Explicit("리포트용 홈/원정 득점"), Timeout(3_600_000)]
        public void Report_HomeAwayGoals()
        {
            var tu = ShapeTestKit.TuningWithShape();
            ShapeTestKit.Setup(seed: 1UL);
            var g = new int[40, 2];
            System.Threading.Tasks.Parallel.For(0, 40, i =>
            {
                var r = new MatchSim(ShapeTestKit.Setup(seed: 9100UL + (ulong)i), tu).RunToEnd();
                g[i, 0] = r.RegularGoals[0]; g[i, 1] = r.RegularGoals[1];
            });
            int h = 0, a = 0;
            for (int i = 0; i < 40; i++) { h += g[i, 0]; a += g[i, 1]; }
            TestContext.WriteLine($"홈 {h} / 원정 {a} (40경기)");
        }

        [Test, Explicit("리포트용 속도 점프 출처")]
        public void Report_SpeedJumpBySource()
        {
            const long T = 18000;
            var sim = ShapeTestKit.Sim(new MatchSimOptions { Checkpoints = CheckpointMode.All }, ShapeTestKit.Setup(seed: 9100UL));
            var stage = StageOf(sim);
            var src = new byte[T + 1, 22];
            var kind = new byte[T + 1, 22];
            var inten = new byte[T + 1, 22];
            while (sim.View.Tick < T && sim.StepTick())
            {
                var t = stage.World.Tick;
                if (t < 0 || t > T) continue;
                for (int s = 0; s < 22; s++) { src[t, s] = (byte)stage.State(s).Source; kind[t, s] = (byte)stage.State(s).Kind; inten[t, s] = (byte)stage.State(s).Intensity; }
            }
            var clip = sim.ExtractClip(0, T);
            var F = clip.Frames.Length;
            var dt = 1f / 30f;
            var bySrc = new int[8];
            var byKind = new int[Considerations.Count];
            int total = 0, offTotal = 0;
            var samples = "";
            for (int e = 0; e < 22; e++)
            {
                float prev = 0f;
                for (int f = 1; f < F - 1; f++)
                {
                    var a = clip.Frames[f - 1]; var b = clip.Frames[f + 1];
                    var vx = (b.X(e) - a.X(e)) / (2 * dt); var vy = (b.Y(e) - a.Y(e)) / (2 * dt);
                    var v = (float)System.Math.Sqrt(vx * vx + vy * vy);
                    if (f > 1 && System.Math.Abs(v - prev) > 25f * dt && v < 15f && prev < 15f)
                    {
                        total++;
                        var tick = clip.Frames[f].Index / 3;
                        if (tick > T) tick = T;
                        var so = src[tick, e];
                        bySrc[so]++;
                        if (so <= 3) { offTotal++; byKind[kind[tick, e]]++; if (samples.Length < 400) samples += $" t{tick}s{e}:{prev:F1}->{v:F1}({(Consideration)kind[tick, e]},{(Intensity)inten[tick, e]})"; }
                    }
                    prev = v;
                }
            }
            var o = ""; for (int k = 0; k < 8; k++) if (bySrc[k] > 0) o += $" {(DecisionSource)k}={bySrc[k]}";
            var kk = ""; for (int k = 0; k < byKind.Length; k++) if (byKind[k] > 0) kk += $" {(Consideration)k}={byKind[k]}";
            TestContext.WriteLine($"30분 점프 {total}, 출처별{o}\n오프볼 행동별{kk}\n예:{samples}");
        }
    }
}
