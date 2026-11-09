using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Verification.Bench
{
    /// <summary>specs/009 성능 워크로드 (AC-17). 시계 API는 코어 밖에만 둔다. EditMode와 IL2CPP 벤치가 같은 코드를 쓴다.</summary>
    public sealed class ShapeBenchWorkloads
    {
        const int MaxTicks = 70000;

        readonly MatchTuning _tuning;

        public ShapeBenchWorkloads(string dataDir)
        {
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            var shape = new ShapeData(
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("shape.json"))),
                FormationCatalog.Parse(File.ReadAllText(Path.Combine(dataDir, "tactics", "formations.json"))));
            _tuning = new MatchTuning(
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("ball.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("match.json"))),
                shape);
        }

        public MatchSim NewMatch(ulong seed = MatchBenchWorkloads.Seed)
        {
            var s = MatchBenchWorkloads.Setup(seed);
            s.Home.Tactics = new TeamTactics { FormationId = "4-3-3" };
            s.Away.Tactics = new TeamTactics { FormationId = "4-4-2" };
            return new MatchSim(s, _tuning, new MatchSimOptions { Checkpoints = RoadToPro.MatchEngine.Match.Replay.CheckpointMode.None });
        }

        /// <summary>단계 0~2 합의 틱별 시간을 모은다.</summary>
        sealed class ShapeTimer : IStageTimer
        {
            public readonly long[] Samples = new long[MaxTicks];
            public int Count;
            long _start, _tickSum;

            public void Begin(StageId stage)
            {
                if (stage <= StageId.Shape) _start = Stopwatch.GetTimestamp();
            }

            public void End(StageId stage)
            {
                if (stage > StageId.Shape) return;
                _tickSum += Stopwatch.GetTimestamp() - _start;
                if (stage == StageId.Shape && Count < Samples.Length)
                {
                    Samples[Count++] = _tickSum;
                    _tickSum = 0;
                }
            }
        }

        /// <summary>전체 경기에서 결정 틱당 형태 계산 평균·p95 (µs), 경기 누적 (s).</summary>
        public Dictionary<string, double> Match()
        {
            NewMatch().RunUntilTick(6000); // JIT 워밍업
            var sim = NewMatch();
            var t = new ShapeTimer();
            sim.Timer = t;
            GC.Collect();
            sim.RunToEnd();

            var sorted = new long[t.Count];
            Array.Copy(t.Samples, sorted, t.Count);
            Array.Sort(sorted);
            double sum = 0;
            for (int i = 0; i < t.Count; i++) sum += sorted[i];
            var toUs = 1e6 / Stopwatch.Frequency;
            return new Dictionary<string, double>
            {
                ["shape_tick_avg_us"] = sum / t.Count * toUs,
                ["shape_tick_p95_us"] = sorted[(int)(t.Count * 0.95)] * toUs,
                ["shape_match_s"] = sum * toUs * 1e-6,
                ["shape_ticks"] = t.Count,
            };
        }

        /// <summary>슬롯 재매칭 1회(10×10 비용 행렬 풀이) p95 (µs).</summary>
        public double RematchP95Us()
        {
            const int N = 10, Samples = 5000;
            var rng = new SeedRng(909UL);
            var matcher = new SlotMatcher();
            var cost = new float[SlotMatcher.MaxSize * SlotMatcher.MaxSize];
            var result = new int[N];
            var times = new long[Samples];
            for (int w = 0; w < 500; w++) { Fill(cost, rng); matcher.Solve(N, N, cost, result); }
            for (int i = 0; i < Samples; i++)
            {
                Fill(cost, rng);
                var t0 = Stopwatch.GetTimestamp();
                matcher.Solve(N, N, cost, result);
                times[i] = Stopwatch.GetTimestamp() - t0;
            }
            Array.Sort(times);
            return times[(int)(Samples * 0.95)] * 1e6 / Stopwatch.Frequency;
        }

        static void Fill(float[] cost, SeedRng rng)
        {
            for (int r = 0; r < 10; r++)
                for (int c = 0; c < 10; c++)
                    cost[r * SlotMatcher.MaxSize + c] = rng.Range(0f, 2500f);
        }

        /// <summary>워밍업 뒤 틱당 할당 (B). GC 카운터가 없는 런타임이면 −1.</summary>
        public double AllocPerTickBytes()
        {
            NewMatch().RunUntilTick(9000);
            var sim = NewMatch();
            sim.RunUntilTick(3000);
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            sim.RunUntilTick(9000);
            var a1 = GC.GetAllocatedBytesForCurrentThread();
            if (a0 == 0 && a1 == 0) return -1;
            return (a1 - a0) / 6000.0;
        }

        /// <summary>전체 측정 (벤치 플레이어용). 표본 3회 중 평균이 가장 낮은 회차.</summary>
        public Dictionary<string, double> RunAll()
        {
            Dictionary<string, double> best = null;
            for (int i = 0; i < 3; i++)
            {
                var m = Match();
                if (best == null || m["shape_tick_avg_us"] < best["shape_tick_avg_us"]) best = m;
            }
            best["rematch_p95_us"] = RematchP95Us();
            best["alloc_per_tick_b"] = AllocPerTickBytes();
            return best;
        }
    }
}
