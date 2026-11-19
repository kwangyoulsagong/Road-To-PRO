using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.PitchControl;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/073 성능 워크로드 (AC-06). 시계 API는 코어 밖에만. EditMode(Mono 회귀)와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// 단건이 µs 이하라 100건 묶음 시간 / 100의 p95(묶음 200개). 상황 64개(무작위 배치, 고정 시드)를 돌려 쓴다.
    /// ① 스위퍼 게이트 1회(경로 30샘플 × 상대 11명, 경로 예측은 따로 — 004) ② 게이트 + 007 지점 질의 1회 ③ 크로스 판단 1회 ④ 1v1 판단 1회 ⑤ ①~④ 할당.
    /// </summary>
    public sealed class KeeperLeaveBenchWorkloads
    {
        const int Batch = 100, Batches = 200, States = 64, Samples = 30;
        static readonly Vec2 Goal = new Vec2(0f, 34f);
        readonly GoalkeeperParams _p;
        readonly SweeperModel _sweeper;
        readonly CrossClaimModel _cross;
        readonly OneOnOneModel _one;
        readonly PitchControlQuery _q = new PitchControlQuery(PitchControlParams.Defaults);
        readonly ActionWorld[] _w = new ActionWorld[States];
        readonly float[][] _t = new float[States][];
        readonly Vec3[][] _pos = new Vec3[States][];
        readonly Vec2[] _landing = new Vec2[States], _ball = new Vec2[States];
        readonly KeeperProfile[] _k = new KeeperProfile[States];
        readonly long[] _samples = new long[Batches];
        float _sink;

        public KeeperLeaveBenchWorkloads(string dataDir)
        {
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            _p = GoalkeeperParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("goalkeeper.json"))));
            var kp = KinematicsParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))), PitchSpec.Default);
            _sweeper = new SweeperModel(_p);
            _cross = new CrossClaimModel(_p);
            _one = new OneOnOneModel(_p);
            var rng = new SeedRng(7306UL);
            for (int i = 0; i < States; i++)
            {
                var w = new ActionWorld();
                var kin = KinematicAttributes.Uniform(10);
                var tech = TechnicalAttributes.Uniform(10);
                w.SetPlayer(0, in kin, kp, new Vec2(rng.Range(2f, 18f), rng.Range(28f, 40f)), Vec2.Zero, in tech);
                for (int s = 1; s < ActionWorld.N; s++)
                    w.SetPlayer(s, in kin, kp, new Vec2(rng.Range(5f, 60f), rng.Range(4f, 64f)), new Vec2(rng.Range(-6f, 6f), rng.Range(-3f, 3f)), in tech);
                _w[i] = w;
                _t[i] = new float[Samples];
                _pos[i] = new Vec3[Samples];
                var from = new Vec2(rng.Range(35f, 55f), rng.Range(20f, 48f));
                var v = new Vec2(rng.Range(-16f, -8f), rng.Range(-3f, 3f));
                for (int k = 0; k < Samples; k++)
                {
                    var t = (k + 1) * 0.1f;
                    _t[i][k] = t;
                    _pos[i][k] = new Vec3(from.X + v.X * t, from.Y + v.Y * t, 0.11f);
                }
                _landing[i] = new Vec2(rng.Range(3f, 14f), rng.Range(26f, 42f));
                _ball[i] = new Vec2(rng.Range(6f, 18f), rng.Range(26f, 42f));
                var a = KeeperAttributes.InternalFromDisplay(rng.Range(1f, 20f));
                _k[i] = KeeperProfile.From(_p, new KeeperAttributes(a, a, a, a, a, a, a, a, a, a, a, 1.88f));
            }
        }

        static double Us(long ticks, double div) => ticks * 1e6 / Stopwatch.Frequency / div;

        double P95(double div)
        {
            Array.Sort(_samples);
            return Us(_samples[(int)Math.Ceiling(Batches * 0.95) - 1], div);
        }

        void Gate(int i)
        {
            var s = i % States;
            var g = _sweeper.Gate(_w[s], 0, in _k[s], KeeperRole.Standard, Goal, 1f, _t[s], _pos[s], Samples, 0f, 2.5f);
            _sink += g.Delta + (g.Pass ? 1f : 0f);
        }

        void GateQuery(int i)
        {
            var s = i % States;
            var w = _w[s];
            var g = _sweeper.Gate(w, 0, in _k[s], KeeperRole.Sweeper, Goal, 1f, _t[s], _pos[s], Samples, 0f, 2.5f);
            // 최악 가정: 게이트 통과 여부와 상관없이 지점 질의 1회 (첫 접촉점이 없으면 경로 중간점)
            var pt = g.Has ? g.Point : new Vec2(_pos[s][Samples / 2].X, _pos[s][Samples / 2].Y);
            var pw = SweeperModel.PWin(_q, w.ControlPlayersView, 0, pt, g.Has ? g.BallTime : 1.5f, g.OutsideBox);
            _sink += _sweeper.Utility(pw, 0.05f, _sweeper.EmptyXg(pt, Goal));
        }

        void Cross(int i)
        {
            var s = i % States;
            var e = _cross.Evaluate(_w[s], 0, in _k[s], 1.88f, Goal, 1f, _landing[s], 1.6f, 2.5f, 0.3f);
            _sink += e.UClaim + e.UPunch;
        }

        void OneOnOne(int i)
        {
            var s = i % States;
            var e = _one.Evaluate(_w[s], 0, in _k[s], Goal, 1f, 12, _ball[s], (i & 1) == 0);
            _sink += e.USmother + e.SetPoint.X;
        }

        double Time(Action<int> f)
        {
            for (int i = 0; i < Batch * 10; i++) f(i); // 워밍업
            var sw = new Stopwatch();
            for (int b = 0; b < Batches; b++)
            {
                sw.Restart();
                for (int i = 0; i < Batch; i++) f(b * Batch + i);
                _samples[b] = sw.ElapsedTicks;
            }
            return P95(Batch);
        }

        public void SweepGate(Dictionary<string, double> m) => m["kl_sweep_gate_p95_us"] = Time(Gate);
        public void SweepQuery(Dictionary<string, double> m) => m["kl_sweep_query_p95_us"] = Time(GateQuery);
        public void CrossDecision(Dictionary<string, double> m) => m["kl_cross_p95_us"] = Time(Cross);
        public void OneOnOneDecision(Dictionary<string, double> m) => m["kl_1v1_p95_us"] = Time(OneOnOne);

        public long GcBytes()
        {
            for (int i = 0; i < 100; i++) { Gate(i); GateQuery(i); Cross(i); OneOnOne(i); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) { Gate(i); GateQuery(i); Cross(i); OneOnOne(i); }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            SweepGate(m);
            SweepQuery(m);
            CrossDecision(m);
            OneOnOneDecision(m);
            m["kl_gc_bytes"] = GcBytes();
            return m;
        }
    }
}
