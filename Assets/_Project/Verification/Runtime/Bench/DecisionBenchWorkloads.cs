using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Tactics;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/010 성능 워크로드 (AC-17). 시계 API는 코어 밖에만 둔다. EditMode와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// ① 결정 틱 1회(단계 5 전체: 22명 LOD + 오프볼 + 대체 온볼 + GK 위임 + 질의) 평균·p95 ② 오프볼 선수 1명 결정 평균·p95
    /// ③ 결정 틱당 피치 컨트롤 질의 평균·최대 ④ 단계 5 틱당 할당(B).
    /// </summary>
    public sealed class DecisionBenchWorkloads
    {
        const int MaxTicks = 70000;
        readonly MatchTuning _tuning;

        public DecisionBenchWorkloads(string dataDir)
        {
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            var shape = new ShapeData(
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("shape.json"))),
                FormationCatalog.Parse(File.ReadAllText(Path.Combine(dataDir, "tactics", "formations.json"))));
            _tuning = new MatchTuning(
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("ball.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("match.json"))),
                shape,
                goalkeeper: TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("goalkeeper.json"))),
                decision: TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("decision.json"))),
                action: TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("action.json"))));
        }

        /// <summary>전술(009)·GK(011)·판단(010) 튜닝 묶음 — 실경기 E2E 장면도 쓴다.</summary>
        public MatchTuning Tuning => _tuning;

        public MatchSim NewMatch(MatchSimOptions o = null, ulong seed = MatchBenchWorkloads.Seed)
        {
            var s = MatchBenchWorkloads.Setup(seed);
            s.Home.Tactics = new TeamTactics { FormationId = "4-3-3" };
            s.Away.Tactics = new TeamTactics { FormationId = "4-4-2" };
            o = o ?? new MatchSimOptions();
            o.Checkpoints = RoadToPro.MatchEngine.Match.Replay.CheckpointMode.None;
            return new MatchSim(s, _tuning, o);
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            Tick(m);
            OffBall(m);
            return m;
        }

        /// <summary>단계 5 시간·할당을 틱마다 모은다.</summary>
        sealed class DecisionTimer : IStageTimer
        {
            public readonly long[] Samples = new long[MaxTicks];
            public int Count;
            public long Bytes;
            long _start, _alloc;

            public void Begin(StageId stage)
            {
                if (stage != StageId.ScenarioOrUtility) return;
                _alloc = GC.GetAllocatedBytesForCurrentThread();
                _start = Stopwatch.GetTimestamp();
            }

            public void End(StageId stage)
            {
                if (stage != StageId.ScenarioOrUtility) return;
                var t = Stopwatch.GetTimestamp() - _start;
                Bytes += GC.GetAllocatedBytesForCurrentThread() - _alloc;
                if (Count < Samples.Length) Samples[Count++] = t;
            }
        }

        public void Tick(Dictionary<string, double> m)
        {
            NewMatch().RunUntilTick(6000); // JIT 워밍업
            DecisionTelemetry tel = null;
            var sim = NewMatch(new MatchSimOptions { DecisionDiagnostics = () => tel = new DecisionTelemetry(1) });
            var timed = NewMatch();
            var t = new DecisionTimer();
            timed.Timer = t;
            GC.Collect();
            timed.RunToEnd();
            sim.RunToEnd(); // 질의 수 (진단 켠 별도 실행 — 시간 측정과 분리)

            var sorted = new long[t.Count];
            Array.Copy(t.Samples, sorted, t.Count);
            Array.Sort(sorted);
            double sum = 0;
            for (int i = 0; i < t.Count; i++) sum += sorted[i];
            var toUs = 1e6 / Stopwatch.Frequency;
            m["dec_tick_avg_us"] = sum / t.Count * toUs;
            m["dec_tick_p95_us"] = sorted[(int)(t.Count * 0.95)] * toUs;
            m["dec_match_s"] = sum * toUs * 1e-6;
            m["dec_ticks"] = t.Count;
            m["dec_gc_bytes_per_tick"] = (double)t.Bytes / Math.Max(1, t.Count);
            m["dec_space_queries_avg"] = tel.Ticks > 0 ? (double)tel.SpaceQueries / tel.Ticks : 0;
            m["dec_space_queries_max"] = tel.MaxSpaceQueries;
        }

        /// <summary>실경기 스냅샷 월드에서 오프볼 선수 1명 결정 시간 (후보 ≤ 8).</summary>
        public void OffBall(Dictionary<string, double> m, int n = 20000)
        {
            var sim = NewMatch();
            sim.RunUntilTick(9000);
            var stage = (UtilityDecisionStage)sim.Stage(StageId.ScenarioOrUtility);
            var w = stage.World;
            var p = stage.Params;
            var decider = new OffBallDecider(p, new StandInSpace(p), stage.Style);
            var rng = new SeedRng(1010UL);
            var pick = new SeedRng(1011UL);
            var slots = new List<int>();
            for (int s = 0; s < DecisionWorld.N; s++) if (w.Active[s] && !DecisionWorld.IsKeeper(s) && s != w.Owner) slots.Add(s);
            var states = new PlayerDecisionState[DecisionWorld.N];
            for (int s = 0; s < states.Length; s++) states[s] = PlayerDecisionState.Initial;
            for (int i = 0; i < 2000; i++) { var s = slots[i % slots.Count]; decider.Decide(w, s, ref states[s], rng, (i & 3) == 0); } // 워밍업
            var t = new long[n];
            for (int i = 0; i < n; i++)
            {
                var s = slots[(int)(pick.NextFloat() * slots.Count) % slots.Count];
                var trigger = (i & 3) == 0;
                var a = Stopwatch.GetTimestamp();
                decider.Decide(w, s, ref states[s], rng, trigger);
                t[i] = Stopwatch.GetTimestamp() - a;
            }
            Array.Sort(t);
            double sum = 0;
            foreach (var x in t) sum += x;
            var toUs = 1e6 / Stopwatch.Frequency;
            m["dec_offball_avg_us"] = sum / n * toUs;
            m["dec_offball_p95_us"] = t[(int)(n * 0.95)] * toUs;
        }
    }
}
