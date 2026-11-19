using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/074 성능 워크로드 (AC-05). 시계 API는 코어 밖에만. EditMode(Mono 회귀)와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// ① 배급 대상 선택 1회(같은 팀 필드 10명 × 방식 6 평가 + softmax 선택, 손에 든 공) ② PK GK 전략 1회(기울기·t_e·라인 이탈 표본) ③ ①② 할당.
    /// 배급은 단건이 수 µs라 10건 묶음, PK는 100건 묶음 시간 / 묶음 크기의 p95(묶음 200개). 상황 64개(무작위 배치, 고정 시드)를 돌려 쓴다.
    /// </summary>
    public sealed class PenaltyDistributionBenchWorkloads
    {
        const int Batches = 200, States = 64;
        readonly GoalkeeperParams _p;
        readonly ActionParams _ap;
        readonly BallParams _bp;
        readonly DistributionModel _dist;
        readonly PenaltyKeeperModel _pk;
        readonly ActionWorld[] _w = new ActionWorld[States];
        readonly Vec2[] _gk = new Vec2[States];
        readonly SeedRng _rng = new SeedRng(7402UL);
        readonly long[] _samples = new long[Batches];
        float _sink;

        public PenaltyDistributionBenchWorkloads(string dataDir)
        {
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            _p = GoalkeeperParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("goalkeeper.json"))));
            _ap = ActionParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("action.json"))));
            _bp = BallParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("ball.json"))), SurfaceCondition.Dry, PitchSpec.Default);
            var kp = KinematicsParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))), PitchSpec.Default);
            _dist = new DistributionModel(_p);
            _pk = new PenaltyKeeperModel(_p);
            var rng = new SeedRng(7401UL);
            for (int i = 0; i < States; i++)
            {
                var w = new ActionWorld();
                w.PrepareRoll(_ap, _bp);
                var kin = KinematicAttributes.Uniform(10);
                var tech = TechnicalAttributes.Uniform(10);
                _gk[i] = new Vec2(rng.Range(3f, 14f), rng.Range(26f, 42f));
                w.SetPlayer(0, in kin, kp, _gk[i], Vec2.Zero, in tech);
                for (int s = 1; s < ActionWorld.N; s++)
                    w.SetPlayer(s, in kin, kp, new Vec2(rng.Range(8f, 100f), rng.Range(3f, 65f)), new Vec2(rng.Range(-3f, 3f), rng.Range(-3f, 3f)), in tech);
                _w[i] = w;
            }
        }

        static double Us(long ticks, double div) => ticks * 1e6 / Stopwatch.Frequency / div;

        double P95(double div)
        {
            Array.Sort(_samples);
            return Us(_samples[(int)Math.Ceiling(Batches * 0.95) - 1], div);
        }

        void Distribute(int i)
        {
            var s = i % States;
            var c = new DistributionContext { Source = DistributionSource.Hands, Quick = (i & 3) == 0, HighPress = (i & 7) == 0, Directness = 0.5f, Role = KeeperRole.Standard, DecisionsN = 0.5f };
            var n = _dist.Evaluate(_w[s], _ap, _bp, 0, _gk[s], 0.3f, in c);
            var pick = n == 0 ? _dist.Forced(_w[s], _bp, 0, _gk[s], DistributionSource.Hands) : _dist.Candidate(_dist.Choose(in c, _rng));
            _sink += pick.Eu + pick.Aim.X;
        }

        void Penalty(int i)
        {
            var mul = PenaltyKeeperModel.PressureMul(_p, (i & 1) == 0, 0.5f, 0.5f);
            var st = _pk.Draw((i & 2) == 0 ? 1 : -1, i % 5 - 2, mul, _rng);
            _sink += st.Lean + st.Lead + st.Dir;
        }

        double Time(Action<int> f, int batch)
        {
            for (int i = 0; i < batch * 10; i++) f(i); // 워밍업
            var sw = new Stopwatch();
            for (int b = 0; b < Batches; b++)
            {
                sw.Restart();
                for (int i = 0; i < batch; i++) f(b * batch + i);
                _samples[b] = sw.ElapsedTicks;
            }
            return P95(batch);
        }

        public void Distribution(Dictionary<string, double> m) => m["pd_dist_p95_us"] = Time(Distribute, 10);
        public void PenaltyStrategy(Dictionary<string, double> m) => m["pd_pk_p95_us"] = Time(Penalty, 100);

        public long GcBytes()
        {
            for (int i = 0; i < 100; i++) { Distribute(i); Penalty(i); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) { Distribute(i); Penalty(i); }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            Distribution(m);
            PenaltyStrategy(m);
            m["pd_gc_bytes"] = GcBytes();
            return m;
        }
    }
}
