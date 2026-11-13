using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/072 성능 워크로드 (AC-12). 시계 API는 코어 밖에만. EditMode와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// ① 공 소유 선수 1명 온볼 결정 — 실경기(010 전술 경기)의 인플레이 필드 소유 틱마다, 008 월드 갱신·압박·시야·후보(008 평가 포함)·점수·선택을
    /// 별도 결정자·별도 RNG로 잰다(경기 결과에 영향 없음) ② 시야 집합 구성 1회 (1,000회 묶음 / 1,000) ③ 010 결정 틱 워크로드(072 포함, 회귀).
    /// </summary>
    public sealed class OnBallBenchWorkloads
    {
        readonly DecisionBenchWorkloads _match;
        readonly DecisionParams _p;

        public OnBallBenchWorkloads(string dataDir)
        {
            _match = new DecisionBenchWorkloads(dataDir);
            _p = DecisionParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(dataDir, "tuning", "decision.json"))));
        }

        static double P95(long[] s, int n, double div = 1)
        {
            if (n == 0) return 0;
            var c = new long[n];
            Array.Copy(s, c, n);
            Array.Sort(c);
            return c[(int)Math.Min(n - 1, Math.Ceiling(n * 0.95) - 1)] * 1e6 / Stopwatch.Frequency / div;
        }

        static double Avg(long[] s, int n, double div = 1)
        {
            if (n == 0) return 0;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += s[i];
            return sum / n * 1e6 / Stopwatch.Frequency / div;
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            Owner(m);
            Vision(m);
            foreach (var kv in _match.RunAll()) m[kv.Key] = kv.Value;
            return m;
        }

        /// <summary>공 소유 선수 1명 결정 (평균·p95·후보 수 평균·결정당 할당).</summary>
        public void Owner(Dictionary<string, double> m, int maxTicks = 54_000)
        {
            var sim = _match.NewMatch();
            var ctx = sim.Context;
            var stage = (UtilityDecisionStage)sim.Stage(StageId.ScenarioOrUtility);
            var dec = new OnBallDecider(_p, stage.Style, new StandInOnBallEval(_p), stage.Chemistry);
            var rng = new SeedRng(7201);
            var act = ctx.Actions;
            var t = new long[maxTicks];
            var tw = new long[maxTicks];
            var td = new long[maxTicks];
            var n = 0;
            long cands = 0, bytes = 0;
            for (int tick = 0; tick < maxTicks && !sim.IsFinished; tick++)
            {
                sim.StepTick();
                ref readonly var ball = ref ctx.Ball;
                if (!ctx.Flow.InPlay || ball.Phase != BallPhase.Controlled || ball.OwnerId < 0 || Squad.IsKeeperSlot(ball.OwnerId)) continue;
                var s = ball.OwnerId;
                var st = PlayerDecisionState.Initial;
                var held = (ctx.Tick - ctx.PossessionStartTick) * 0.1f;
                act.InvalidateWorld();
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                act.Refresh(ctx);
                var pressure = act.PressureOn(ctx, s);
                var t1 = Stopwatch.GetTimestamp();
                dec.Decide(stage.World, act.World, act.P, ctx.BallParams, s, ref st, rng, pressure, false, held);
                var t2 = Stopwatch.GetTimestamp();
                t[n] = t2 - t0;
                tw[n] = t1 - t0; // 008 월드 갱신(22명 TTI)·압박 — 틱당 1회 공유 비용
                td[n] = t2 - t1; // 시야·후보(008 평가)·점수·선택
                n++;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
                cands += dec.Candidates.Count;
            }
            m["onball_owner_avg_us"] = Avg(t, n);
            m["onball_owner_p95_us"] = P95(t, n);
            m["onball_owner_samples"] = n;
            m["onball_owner_world_avg_us"] = Avg(tw, n);
            m["onball_owner_decide_avg_us"] = Avg(td, n);
            m["onball_owner_decide_p95_us"] = P95(td, n);
            m["onball_candidates_avg"] = n > 0 ? (double)cands / n : 0;
            m["onball_gc_bytes_per_decision"] = n > 0 ? (double)bytes / n : 0;
        }

        /// <summary>시야 집합 구성 1회 (동료 10명).</summary>
        public void Vision(Dictionary<string, double> m, int n = 2000)
        {
            const int batch = 1000;
            var sim = _match.NewMatch();
            sim.RunUntilTick(3000);
            var stage = (UtilityDecisionStage)sim.Stage(StageId.ScenarioOrUtility);
            var w = stage.World;
            var v = new VisionSet();
            var t = new long[n];
            for (int k = 0; k < n; k++)
            {
                var s = 1 + k % 10;
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < batch; i++) v.Build(_p, w, s);
                t[k] = Stopwatch.GetTimestamp() - t0;
            }
            m["onball_vision_avg_us"] = Avg(t, n, batch);
            m["onball_vision_p95_us"] = P95(t, n, batch);
        }
    }
}
