using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/011 성능 워크로드 (AC-17). 시계 API는 코어 밖에만 둔다. EditMode와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// ① 위치 탐색 1회(후보 15 × 목표 21, 슈팅 가능 거리 안 공) ② 선방 판정 1회(경로가 준비된 상태의 Evaluate + Resolve, 경로 예측은 따로)
    /// ③ 결정 틱당 GK 2명 위치 갱신 — 실제 경기의 틱별 공 위치로 FR-04 주기(거리 안 매 틱, 밖 0.3 s)를 그대로 따라 잰다.
    /// </summary>
    public sealed class GoalkeeperBenchWorkloads
    {
        readonly GoalkeeperParams _p;
        readonly BallParams _bp;
        readonly MatchTuning _tuning;

        public GoalkeeperBenchWorkloads(string dataDir)
        {
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            var gk = TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("goalkeeper.json")));
            var ball = TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("ball.json")));
            _p = GoalkeeperParams.Create(gk);
            _bp = BallParams.Create(ball, SurfaceCondition.Dry, PitchSpec.Default);
            _tuning = new MatchTuning(ball, TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("match.json"))), goalkeeper: gk);
        }

        static double P95(long[] samples, int n)
        {
            var s = new long[n];
            Array.Copy(samples, s, n);
            Array.Sort(s);
            return s[(int)Math.Min(n - 1, Math.Ceiling(n * 0.95) - 1)] * 1e6 / Stopwatch.Frequency;
        }

        static double Avg(long[] samples, int n)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += samples[i];
            return sum / n * 1e6 / Stopwatch.Frequency;
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            Position(m);
            Save(m);
            PerTick(m);
            return m;
        }

        public void Position(Dictionary<string, double> m, int n = 20000)
        {
            var pos = new KeeperPositioner(_p, new GoalGrid(_p));
            var k = KeeperProfile.From(_p, KeeperAttributes.Uniform(20f)); // 후보 최대(15)
            var rng = new SeedRng(1101UL);
            var balls = new Vec2[n];
            for (int i = 0; i < n; i++) balls[i] = new Vec2(rng.Range(6f, 30f), 34f + rng.Range(-18f, 18f));
            var inp = new PositionInput { Goal = new Vec2(0f, 34f), DirIn = 1f, LineHeight = 30f };
            for (int i = 0; i < 2000; i++) { inp.Ball = balls[i]; pos.Target(in inp, in k); } // 워밍업
            var t = new long[n];
            for (int i = 0; i < n; i++)
            {
                inp.Ball = balls[i];
                var s = Stopwatch.GetTimestamp();
                pos.Target(in inp, in k);
                t[i] = Stopwatch.GetTimestamp() - s;
            }
            m["gk_position_p95_us"] = P95(t, n);
            m["gk_position_avg_us"] = Avg(t, n);
        }

        public void Save(Dictionary<string, double> m, int n = 4000)
        {
            var model = new SaveModel(_p, new KeeperPositioner(_p, new GoalGrid(_p)));
            model.Bind(_bp);
            var k = KeeperProfile.From(_p, KeeperAttributes.Uniform(10.5f));
            var ctx = new SaveContext { Goal = new Vec2(0f, 34f), DirIn = 1f };
            var rng = new SeedRng(7UL);
            var fx = new SeedRng(8UL);
            var tEval = new long[n];
            var tPred = new long[n];
            int c = 0;
            for (int i = 0; i < n + 500; i++)
            {
                var origin = new Vec3(fx.Range(10f, 30f), 34f + fx.Range(-12f, 12f), 0.11f);
                var s = model.AimedShot(origin, (int)(fx.NextFloat() * GoalGrid.Cells), fx.Range(16f, 30f), ctx.Goal, _bp);
                var a = Stopwatch.GetTimestamp();
                var on = model.Predict(in s, _bp, ctx.Goal, 1f);
                var b = Stopwatch.GetTimestamp();
                if (!on) continue;
                var gk = new KeeperState { Position = new Vec2(2f, 34f) };
                var j = model.Evaluate(in gk, in k, in ctx);
                model.Resolve(in j, in k, in ctx, rng);
                var e = Stopwatch.GetTimestamp();
                if (i < 500 || c >= n) continue; // 워밍업
                tPred[c] = b - a;
                tEval[c] = e - b;
                c++;
            }
            m["gk_save_p95_us"] = P95(tEval, c);
            m["gk_save_avg_us"] = Avg(tEval, c);
            m["gk_predict_p95_us"] = P95(tPred, c);
        }

        /// <summary>실제 경기 공 위치(틱별)로 GK 2명 위치 갱신 비용 — FR-04 주기 그대로 (거리 안 매 틱, 밖 0.3 s).</summary>
        public void PerTick(Dictionary<string, double> m)
        {
            var sim = new MatchSim(MatchBenchWorkloads.Setup(MatchBenchWorkloads.Seed), _tuning);
            var balls = new List<Vec2>(60000);
            while (!sim.IsFinished && balls.Count < 60000)
            {
                sim.StepTicks(1);
                balls.Add(sim.Context.Ball.Position.XY);
            }
            var pos = new KeeperPositioner(_p, new GoalGrid(_p));
            var k = KeeperProfile.From(_p, KeeperAttributes.Uniform(10.5f));
            var goals = new[] { new Vec2(0f, 34f), new Vec2(105f, 34f) };
            var dirs = new[] { 1f, -1f };
            var last = new long[] { long.MinValue / 2, long.MinValue / 2 };
            var low = (long)Math.Round(_p.PosLowRate * 10);
            var r2 = _p.PosShootRange * _p.PosShootRange;
            var t = new long[balls.Count];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < balls.Count; i++)
                {
                    var s = Stopwatch.GetTimestamp();
                    for (int team = 0; team < 2; team++)
                    {
                        var inRange = (balls[i] - goals[team]).LengthSq() <= r2;
                        if (!inRange && i - last[team] < low) continue;
                        last[team] = i;
                        var inp = new PositionInput { Goal = goals[team], DirIn = dirs[team], Ball = balls[i], LineHeight = 30f };
                        pos.Target(in inp, in k);
                    }
                    if (pass == 1) t[i] = Stopwatch.GetTimestamp() - s;
                }
            m["gk_tick_avg_us"] = Avg(t, t.Length);
            m["gk_tick_p95_us"] = P95(t, t.Length);
            m["gk_ticks"] = t.Length;
        }
    }
}
