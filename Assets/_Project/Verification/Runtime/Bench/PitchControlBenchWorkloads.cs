using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.PitchControl;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/007 성능 워크로드 (AC-12). 시계 API는 코어 밖에만. EditMode와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// ① 지점 질의 1개 평균 (22명 무작위 상태, 피치 안 지점, 공 도착 0~2 s — 100개 묶음/100)
    /// ② 지점 질의 최악 (22명 모두 지점에서 45~60 m, 비슷한 도달 시간 → 긴 경합 적분)
    /// ③ 궤적 질의 1회 (땅볼 12~22 m/s, 3 s 상한) ④ 32×20 그리드 1회 ⑤ ①~④ 할당
    /// ⑥ 가지치기 대 전체 적분 최대 차이 (상태 1,000개, AC-12 ≤ 1e-3).
    /// </summary>
    public sealed class PitchControlBenchWorkloads
    {
        const int States = 64;
        readonly PitchControlParams _p;
        readonly BallParams _bp;
        readonly KinematicsParams _kp;
        readonly ControlPlayers[] _states = new ControlPlayers[States];
        readonly Vec2[] _targets = new Vec2[States];
        readonly float[] _times = new float[States];
        readonly PitchControlQuery _q;
        readonly TrajectorySamples _tr = new TrajectorySamples();
        readonly PitchControlGrid _grid = new PitchControlGrid();

        public PitchControlBenchWorkloads(string dataDir)
        {
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            _p = PitchControlParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("action.json"))));
            _bp = BallParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("ball.json"))), SurfaceCondition.Dry, PitchSpec.Default);
            _kp = KinematicsParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))), PitchSpec.Default);
            _q = new PitchControlQuery(_p);
            var rng = new SeedRng(7016);
            for (int i = 0; i < States; i++)
            {
                _states[i] = new ControlPlayers();
                Fill(_states[i], rng);
                _targets[i] = new Vec2(rng.Range(0f, 105f), rng.Range(0f, 68f));
                _times[i] = rng.Range(0f, 2f);
            }
        }

        void Put(ControlPlayers pl, int slot, Vec2 pos, Vec2 vel, int display)
        {
            var prof = PlayerMotionProfile.Create(KinematicAttributes.Uniform(display), _kp);
            var ctx = new TimeToIntercept.Context(prof, pos, vel, 1f, 0f, _kp);
            ControlPlayers.Lambdas(_p, slot % 11 == 0, 0.5f, 0.5f, 0.5f, out var la, out var ld);
            pl.SetEngine(slot, slot / 11, slot % 11 == 0, pos, vel, in ctx, la, ld);
        }

        void Fill(ControlPlayers pl, SeedRng rng)
        {
            for (int s = 0; s < ControlPlayers.N; s++)
                Put(pl, s, new Vec2(rng.Range(0f, 105f), rng.Range(0f, 68f)), new Vec2(rng.Range(-6f, 6f), rng.Range(-6f, 6f)), 8 + (int)(rng.NextFloat() * 10f));
        }

        static double Us(long ticks, double div = 1) => ticks * 1e6 / Stopwatch.Frequency / div;

        static double P95(long[] s, int n, double div = 1)
        {
            var c = new long[n];
            Array.Copy(s, c, n);
            Array.Sort(c);
            return Us(c[(int)Math.Min(n - 1, Math.Ceiling(n * 0.95) - 1)], div);
        }

        static double Avg(long[] s, int n, double div = 1)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += s[i];
            return sum / n * 1e6 / Stopwatch.Frequency / div;
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            Point(m);
            PointWorst(m);
            Trajectory(m);
            Grid(m);
            m["pc_gc_bytes"] = GcBytes();
            m["pc_prune_maxdiff"] = PruneMaxDiff();
            m["pc_traj_interp_maxdiff"] = InterpMaxDiff();
            return m;
        }

        float _sink;

        /// <summary>① 지점 질의 평균 (100개 묶음 / 100).</summary>
        public void Point(Dictionary<string, double> m, int n = 2000)
        {
            const int Batch = 100;
            var t = new long[n];
            for (int k = 0; k < n + 50; k++)
            {
                var s = Stopwatch.GetTimestamp();
                for (int b = 0; b < Batch; b++)
                {
                    var i = (k * Batch + b) % States;
                    _sink += _q.Point(_states[i], i & 1, _targets[i], _times[i]).Att;
                }
                if (k >= 50) t[k - 50] = Stopwatch.GetTimestamp() - s;
            }
            m["pc_point_avg_us"] = Avg(t, n, Batch);
            m["pc_point_p95_us"] = P95(t, n, Batch);
        }

        /// <summary>② 최악: 22명이 지점에서 45~60 m (비슷한 도달 시간, 가지치기가 거의 안 걸리고 수렴이 늦다).</summary>
        public void PointWorst(Dictionary<string, double> m, int n = 2000)
        {
            var pl = new ControlPlayers();
            var rng = new SeedRng(7017);
            var target = new Vec2(52.5f, 34f);
            for (int s = 0; s < ControlPlayers.N; s++)
            {
                var ang = rng.Range(0f, DMath.TwoPi);
                var r = rng.Range(45f, 60f);
                var p = target + new Vec2(DMath.Cos(ang) * r * 0.9f, DMath.Sin(ang) * r * 0.5f);
                Put(pl, s, p, Vec2.Zero, 10);
            }
            var t = new long[n];
            for (int k = 0; k < n + 50; k++)
            {
                var s = Stopwatch.GetTimestamp();
                _sink += _q.Point(pl, 0, target, 0f).Att;
                if (k >= 50) t[k - 50] = Stopwatch.GetTimestamp() - s;
            }
            m["pc_point_worst_us"] = Avg(t, n);
        }

        /// <summary>③ 궤적 질의 1회 (땅볼, 패서 = 상태의 슬롯 1 → 무작위 조준점, 샘플 생성 포함).</summary>
        public void Trajectory(Dictionary<string, double> m, int n = 2000)
        {
            var rng = new SeedRng(7018);
            var t = new long[n];
            var a0 = _q.ArrivalCount;
            var s0 = _q.SampleCount;
            for (int k = 0; k < n + 50; k++)
            {
                var i = k % States;
                var pl = _states[i];
                var aim = _targets[(i + 7) % States];
                var v0 = rng.Range(12f, 22f);
                var s = Stopwatch.GetTimestamp();
                _tr.Ground(_bp, pl.Pos[1], aim, v0, _p.Dt, _p.Horizon, 105f, 68f);
                _sink += _q.Trajectory(pl, _tr, 1, 2).Receiver;
                if (k >= 50) t[k - 50] = Stopwatch.GetTimestamp() - s;
            }
            m["pc_traj_avg_us"] = Avg(t, n);
            m["pc_traj_tti_per_query"] = (_q.ArrivalCount - a0) / (double)(n + 50);
            m["pc_traj_samples_per_query"] = (_q.SampleCount - s0) / (double)(n + 50);
            m["pc_traj_p95_us"] = P95(t, n);
        }

        /// <summary>④ 32×20 그리드 1회 (ms).</summary>
        public void Grid(Dictionary<string, double> m, int n = 30)
        {
            var t = new long[n];
            for (int k = 0; k < n + 3; k++)
            {
                var i = k % States;
                var s = Stopwatch.GetTimestamp();
                _q.Grid(_states[i], 0, _targets[i], k, _grid);
                if (k >= 3) t[k - 3] = Stopwatch.GetTimestamp() - s;
            }
            m["pc_grid_ms"] = Avg(t, n) / 1000.0;
        }

        /// <summary>⑤ 지점·궤적·그리드 경로 할당 (워밍업 뒤, B).</summary>
        public long GcBytes()
        {
            for (int k = 0; k < 10; k++) { _q.Point(_states[k], 0, _targets[k], _times[k]); _tr.Ground(_bp, _states[k].Pos[1], _targets[k + 1], 15f, _p.Dt, _p.Horizon, 105f, 68f); _q.Trajectory(_states[k], _tr, 1, 2); }
            _q.Grid(_states[0], 0, _targets[0], 0, _grid);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < States; k++)
            {
                _sink += _q.Point(_states[k], k & 1, _targets[k], _times[k]).Att;
                _tr.Ground(_bp, _states[k].Pos[1], _targets[(k + 3) % States], 15f, _p.Dt, _p.Horizon, 105f, 68f);
                _sink += _q.Trajectory(_states[k], _tr, 1, 2).Opponent;
            }
            _q.Grid(_states[1], 0, _targets[1], 1, _grid);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>⑦ 궤적 TTI 보간(stride) 대 매 샘플 정확값의 최대 차이 (4종 확률, 궤적 1,000개).</summary>
        public double InterpMaxDiff(int n = 1000)
        {
            var rng = new SeedRng(7020);
            var worst = 0.0;
            for (int k = 0; k < n; k++)
            {
                var pl = _states[k % States];
                _tr.Ground(_bp, pl.Pos[1], new Vec2(rng.Range(0f, 105f), rng.Range(0f, 68f)), rng.Range(10f, 24f), _p.Dt, _p.Horizon, 105f, 68f);
                var a = _q.Trajectory(pl, _tr, 1, 2);
                var b = _q.Trajectory(pl, _tr, 1, 2, true, 1);
                worst = Math.Max(worst, Math.Max(Math.Max(Math.Abs(a.Receiver - b.Receiver), Math.Abs(a.Teammate - b.Teammate)), Math.Max(Math.Abs(a.Opponent - b.Opponent), Math.Abs(a.Out - b.Out))));
            }
            return worst;
        }

        /// <summary>⑥ 가지치기·조기 점프 결과와 전체 적분의 최대 차이 (지점 1,000개, 팀 컨트롤).</summary>
        public double PruneMaxDiff(int n = 1000)
        {
            var rng = new SeedRng(7019);
            var worst = 0.0;
            for (int k = 0; k < n; k++)
            {
                var i = k % States;
                var target = new Vec2(rng.Range(0f, 105f), rng.Range(0f, 68f));
                var tb = rng.Range(0f, 2f);
                var a = _q.Point(_states[i], k & 1, target, tb, true);
                var b = _q.Point(_states[i], k & 1, target, tb, false);
                worst = Math.Max(worst, Math.Max(Math.Abs(a.Att - b.Att), Math.Abs(a.Def - b.Def)));
            }
            return worst;
        }
    }
}
