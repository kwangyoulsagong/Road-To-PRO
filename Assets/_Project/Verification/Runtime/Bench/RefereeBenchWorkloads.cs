using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Referee;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/012 성능 워크로드 (AC-21). 시계 API는 코어 밖에만. EditMode(Mono 회귀)와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// 단건이 µs 이하라 100건 묶음 시간 / 100의 p95(묶음 200개).
    /// ① 접촉 판정 1회 = 진짜 판정 + DOGSO + 시야 품질 + 인지 + 심판 임계 + 어드밴티지 점수
    /// ② 오프사이드 위치 판정 1회 = 스냅샷(22명, 뒤에서 두 번째 수비수 + 후보 마진)
    /// ③ 결정 틱당 심판 3인 목표 + 시야 품질 1회 ④ ①~③ 할당.
    /// </summary>
    public sealed class RefereeBenchWorkloads
    {
        const int Batch = 100, Batches = 200, States = 64;
        readonly RefereeParams _p;
        readonly RefereeProfile _r = RefereeProfile.LeagueAverage;
        readonly RefereeWorld[] _w = new RefereeWorld[States];
        readonly ContactDetail[] _c = new ContactDetail[States];
        readonly float[][] _x = new float[States][];
        readonly float[] _speed = new float[RefereeWorld.N];
        readonly OffsideTracker _t = new OffsideTracker();
        readonly SeedRng _rng = new SeedRng(1212);
        readonly long[] _samples = new long[Batches];
        float _sink;

        public RefereeBenchWorkloads(string dataDir)
        {
            _p = RefereeParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(dataDir, "tuning", "match.json"))));
            var rng = new SeedRng(7012);
            for (int i = 0; i < States; i++)
            {
                var w = new RefereeWorld();
                var x = new float[RefereeWorld.N];
                for (int s = 0; s < RefereeWorld.N; s++)
                {
                    w.Active[s] = true;
                    w.Pos[s] = new Vec2(rng.Range(0f, 105f), rng.Range(0f, 68f));
                    w.Vel[s] = new Vec2(rng.Range(-6f, 6f), rng.Range(-6f, 6f));
                    x[s] = w.Pos[s].X;
                }
                w.Officials[0] = new Vec2(rng.Range(20f, 85f), rng.Range(10f, 58f));
                w.Officials[1] = new Vec2(rng.Range(52f, 105f), -1.5f);
                w.Officials[2] = new Vec2(rng.Range(0f, 52f), 69.5f);
                w.Ball = new Vec3(rng.Range(0f, 105f), rng.Range(0f, 68f), 0.11f);
                _w[i] = w;
                _x[i] = x;
                _c[i] = new ContactDetail
                {
                    Kind = ContactKind.Tackle, Attacker = 3, Defender = 15, Intensity = rng.NextFloat(), FromBehind = rng.NextFloat(), StudsUp = rng.NextFloat() * 0.5f,
                    Lateness = rng.NextFloat() * 0.3f, BallFirst = rng.NextFloat(), Height = 0.3f, X = w.Pos[3].X, Y = w.Pos[3].Y, DogsoValid = true,
                    GoalDist = rng.Range(5f, 60f), DefendersBetween = (int)(rng.NextFloat() * 4f), ControlLikely = rng.NextFloat(),
                };
            }
        }

        static double Us(long ticks, double div) => ticks * 1e6 / Stopwatch.Frequency / div;

        double P95(double div)
        {
            Array.Sort(_samples);
            return Us(_samples[(int)Math.Ceiling(Batches * 0.95) - 1], div);
        }

        void Contact(int i)
        {
            ref readonly var c = ref _c[i % States];
            var w = _w[i % States];
            var score = FoulJudge.Score(_p, in c);
            var truth = FoulJudge.Decide(score, FoulJudge.Base(_p));
            var cos = DogsoJudge.GoalCos(w.Pos[3], w.Vel[3], w.Facing[3], w.OppGoal(0));
            var kind = DogsoJudge.Classify(_p, in c, cos);
            var sight = Perception.SightQuality(_p, w, new Vec2(c.X, c.Y), 15, 3, w.Vel[3]);
            var perceived = Perception.Perceive(_p, _r, score, sight, true, 0f, _rng);
            var th = FoulJudge.ForReferee(_p, _r, 1);
            var given = DogsoJudge.Apply(FoulJudge.Decide(perceived, th), kind, c.InBox, true, false, RuleSet.Default, out _);
            var adv = AdvantageQueue.Score(_p, true, (w.Ball.XY - w.OppGoal(0)).Length());
            _sink += given + truth + adv;
        }

        void Offside(int i)
        {
            var w = _w[i % States];
            _t.Snapshot(_p, i, 0, 5, w.Ball.X, _x[i % States], _speed, 1, 105f, false);
            _sink += _t.LineX;
        }

        void Officials(int i)
        {
            var w = _w[i % States];
            var b = w.Ball.XY;
            var t = OfficialsMotion.RefereeTarget(_p, b, 105f, 68f);
            var hi = OffsideJudge.SecondLastDefenderX(_x[i % States], 1, 1, 105f, 0f);
            var lo = OffsideJudge.SecondLastDefenderX(_x[i % States], 0, -1, 105f, 0f);
            var a1 = OfficialsMotion.AssistantHigh(_p, hi, b.X, 105f);
            var a2 = OfficialsMotion.AssistantLow(_p, lo, b.X, 105f, 68f);
            var q = Perception.SightQuality(_p, w, b, -1, -1, Vec2.Zero);
            _sink += t.X + a1.X + a2.X + q;
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

        public void Contact(Dictionary<string, double> m) => m["ref_contact_p95_us"] = Time(Contact);
        public void Offside(Dictionary<string, double> m) => m["ref_offside_p95_us"] = Time(Offside);
        public void Officials(Dictionary<string, double> m) => m["ref_officials_p95_us"] = Time(Officials);

        public long GcBytes()
        {
            for (int i = 0; i < 100; i++) { Contact(i); Offside(i); Officials(i); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) { Contact(i); Offside(i); Officials(i); }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            Contact(m);
            Offside(m);
            Officials(m);
            m["ref_gc_bytes"] = GcBytes();
            m["ref_sink"] = _sink > 0f ? 1 : 0;
            return m;
        }
    }
}
