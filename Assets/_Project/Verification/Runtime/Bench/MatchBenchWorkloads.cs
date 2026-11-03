using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Match.Stages;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/006 성능 워크로드 (성능 예산 표·FR-80). IL2CPP 벤치 플레이어(<see cref="SimBench"/>)와 EditMode 성능 테스트(Mono 회귀)가
    /// 같은 코드를 부른다 — 측정 대상이 어긋나지 않게. 결과 키는 Data/perf/006-match-loop-simevent.json과 같다.
    /// </summary>
    public sealed class MatchBenchWorkloads
    {
        public const ulong Seed = 20261103UL;

        readonly MatchTuning _tuning;

        public MatchBenchWorkloads(string dataDir)
        {
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            _tuning = new MatchTuning(
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("ball.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("match.json"))));
        }

        public MatchTuning Tuning => _tuning;

        static readonly Vec2[] Formation =
        {
            new Vec2(0.04f, 0.50f),
            new Vec2(0.22f, 0.15f), new Vec2(0.20f, 0.38f), new Vec2(0.20f, 0.62f), new Vec2(0.22f, 0.85f),
            new Vec2(0.45f, 0.15f), new Vec2(0.42f, 0.38f), new Vec2(0.42f, 0.62f), new Vec2(0.45f, 0.85f),
            new Vec2(0.66f, 0.40f), new Vec2(0.66f, 0.60f),
        };

        public static MatchSetup Setup(ulong seed)
        {
            var rng = new SeedRng(seed ^ 0xBE4CUL);
            TeamSetup Team(int baseId)
            {
                var players = new MatchPlayer[18];
                for (int i = 0; i < 18; i++)
                    players[i] = new MatchPlayer(baseId + i, new KinematicAttributes(rng.Range(30f, 90f), rng.Range(30f, 90f), rng.Range(30f, 90f),
                        rng.Range(30f, 90f), rng.Range(30f, 90f), rng.Range(30f, 90f), rng.Range(30f, 90f)));
                var st = new int[11];
                for (int i = 0; i < 11; i++) st[i] = baseId + i;
                var b = new int[7];
                for (int i = 0; i < 7; i++) b[i] = baseId + 11 + i;
                return new TeamSetup { TeamId = baseId, Players = players, Starters = st, Bench = b, Formation = (Vec2[])Formation.Clone() };
            }
            return new MatchSetup { Seed = seed, Home = Team(1), Away = Team(101) };
        }

        /// <summary>물리 단계를 정지 대체로 바꾼 루프 뼈대 (2026-11-03 스펙 수정: 뼈대 예산은 물리 제외).</summary>
        public sealed class FrozenPhysicsStage : StatelessStage
        {
            public override StageId Id => StageId.Physics;
            public override void Execute(MatchContext ctx) { }
        }

        public MatchSim NewMatch(CheckpointMode mode = CheckpointMode.None, bool frozenPhysics = false, ulong seed = Seed)
        {
            var o = new MatchSimOptions { Checkpoints = mode };
            if (frozenPhysics) o.Stages = () => { var s = MatchSim.DefaultStages(); s[(int)StageId.Physics] = new FrozenPhysicsStage(); return s; };
            return new MatchSim(Setup(seed), _tuning, o);
        }

        /// <summary>측정 직전 GC 정리 — 앞선 작업(테스트 묶음의 큰 참조 경기 등)이 남긴 쓰레기가 측정 구간에서 수거되지 않게.</summary>
        static void Settle()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        static double Ms(long t0) => (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;

        /// <summary>뼈대 경기 54,000틱 (ms). 물리가 없어 공이 멈춰 있어도 단계·보드·이벤트·체크포인트 해시·진행 판정은 매 틱 돈다.</summary>
        public double SkeletonMatchMs()
        {
            var sim = NewMatch(frozenPhysics: true);
            Settle();
            var t0 = Stopwatch.GetTimestamp();
            sim.RunUntilTick(54_000);
            return Ms(t0);
        }

        /// <summary>대체 단계 + 물리 전체 경기: 총 ms, 틱당 µs, 단계별 평균 µs.</summary>
        public Dictionary<string, double> FullMatch()
        {
            var sim = NewMatch();
            var timer = new StopwatchStageTimer();
            sim.Timer = timer;
            Settle();
            var t0 = Stopwatch.GetTimestamp();
            sim.RunToEnd();
            var ms = Ms(t0);
            var d = new Dictionary<string, double>
            {
                ["full_match_ms"] = ms,
                ["full_match_ticks"] = sim.NextTick,
                ["tick_avg_us"] = ms * 1000.0 / sim.NextTick,
            };
            double sum = 0;
            for (int s = 0; s < Stages.Count; s++)
            {
                var us = timer.AvgMicros((StageId)s);
                d["stage_us." + ((StageId)s)] = us;
                sum += us;
            }
            d["stages_sum_us"] = sum;
            d["physics_us_per_tick"] = timer.AvgMicros(StageId.Physics);
            return d;
        }

        /// <summary>체크포인트 크기 최대(KB)와 복원 평균(ms). 30분까지 All 모드로 돌린 스냅샷 20개.</summary>
        public Dictionary<string, double> Checkpoints()
        {
            var sim = NewMatch(CheckpointMode.All);
            sim.RunUntilTick(18_000);
            var maxBytes = 0;
            foreach (var s in sim.Snapshots) if (s.SizeBytes > maxBytes) maxBytes = s.SizeBytes;
            var fork = sim.Fork();
            fork.Restore(sim.Snapshots[1], sim.Log, sim.Checkpoints); // 워밍업
            Settle();
            var t0 = Stopwatch.GetTimestamp();
            int n = 0;
            for (int i = 5; i < sim.Snapshots.Count && n < 20; i += 8, n++) fork.Restore(sim.Snapshots[i], sim.Log, sim.Checkpoints);
            return new Dictionary<string, double>
            {
                ["checkpoint_max_kb"] = maxBytes / 1024.0,
                ["restore_avg_ms"] = Ms(t0) / n,
            };
        }

        /// <summary>임의 11초 클립 재구성 평균 (ms). 30분 경기에서 10개.</summary>
        public double Clip11sMs()
        {
            var sim = NewMatch(CheckpointMode.All);
            sim.RunUntilTick(18_000);
            sim.ExtractClip(1000, 1110); // 워밍업
            var rng = new SeedRng(11UL);
            Settle();
            var t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < 10; i++)
            {
                var from = rng.Range(100, 17_800);
                sim.ExtractClip(from, from + 110);
            }
            return Ms(t0) / 10;
        }

        /// <summary>이벤트 로그·링 버퍼 메모리 (MB).</summary>
        public Dictionary<string, double> Memory()
        {
            var sim = NewMatch();
            sim.RunToEnd();
            return new Dictionary<string, double>
            {
                ["event_log_mb"] = sim.Log.ApproxBytes / (1024.0 * 1024.0),
                ["ring_buffer_mb"] = sim.Recorder.RingBytes / (1024.0 * 1024.0),
                ["event_count"] = sim.Log.Count,
            };
        }

        /// <summary>워밍업 뒤 틱당 할당 (B). 보관 모드 None. GC 카운터가 없는 런타임이면 −1.</summary>
        public double AllocPerTickBytes()
        {
            // 워밍업: 같은 경기를 한 번 돌려 처음 호출되는 경로의 JIT 할당을 뺀다
            NewMatch().RunUntilTick(9000);
            var sim = NewMatch();
            sim.RunUntilTick(3000);
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            sim.RunUntilTick(9000);
            var a1 = GC.GetAllocatedBytesForCurrentThread();
            if (a0 == 0 && a1 == 0) return -1;
            return (a1 - a0) / 6000.0;
        }

        /// <summary>전체 측정 (벤치 플레이어용).</summary>
        public Dictionary<string, double> RunAll()
        {
            var d = new Dictionary<string, double>();
            SkeletonMatchMs(); // 워밍업
            var sk = double.MaxValue;
            for (int i = 0; i < 3; i++) sk = Math.Min(sk, SkeletonMatchMs());
            d["skeleton_match_ms"] = sk;
            Dictionary<string, double> best = null;
            for (int i = 0; i < 3; i++)
            {
                var f = FullMatch();
                if (best == null || f["physics_us_per_tick"] < best["physics_us_per_tick"]) best = f;
            }
            foreach (var kv in best) d[kv.Key] = kv.Value;
            foreach (var kv in Checkpoints()) d[kv.Key] = kv.Value;
            d["clip_11s_ms"] = Clip11sMs();
            foreach (var kv in Memory()) d[kv.Key] = kv.Value;
            d["alloc_per_tick_b"] = AllocPerTickBytes();
            return d;
        }
    }
}
