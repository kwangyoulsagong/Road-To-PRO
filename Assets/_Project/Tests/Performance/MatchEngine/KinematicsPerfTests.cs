using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>
    /// specs/005 성능 예산 (AC-13·15). 예산 수치는 Data/perf/005-player-kinematics.json — farm logic이 판정한다.
    /// 22명 시나리오: 결정 틱마다 Arrive(주변) 위주 + Pursue·저킹·마킹·Evade 혼합 (플랜 "22명 혼합 명령"). 명령 발행 비용은 측정에서 뺀다.
    /// </summary>
    public class KinematicsPerfTests
    {
        const int Repeats = 7;

        static string DataPath(string file) => Path.Combine(Directory.GetCurrentDirectory(), "Data", "tuning", file);

        static KinematicsParams Params() =>
            KinematicsParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(DataPath("kinematics.json"))), PitchSpec.Default);

        static KinematicsSystem Squad(KinematicsParams p, SeedRng rng)
        {
            var ps = new PlayerSetup[22];
            for (int i = 0; i < 22; i++)
            {
                var a = new KinematicAttributes(rng.Range(0f, 100f), rng.Range(0f, 100f), rng.Range(0f, 100f), rng.Range(0f, 100f),
                    rng.Range(0f, 100f), rng.Range(0f, 100f), rng.Range(0f, 100f));
                ps[i] = new PlayerSetup(i + 1, a, new Vec2(rng.Range(5f, 100f), rng.Range(5f, 63f)));
            }
            return new KinematicsSystem(p, ps);
        }

        static void Commands(KinematicsSystem k, SeedRng rng)
        {
            for (int i = 0; i < 22; i++)
            {
                var s = k.State(i);
                var other = 1 + (i + 1 + (int)(rng.NextFloat() * 21f)) % 22;
                var near = new Vec2(s.Position.X + rng.Range(-15f, 15f), s.Position.Y + rng.Range(-15f, 15f));
                var r = rng.NextFloat();
                SteeringCommand c;
                if (r < 0.6f) c = SteeringCommand.Arrive(near, rng.Bernoulli(0.5f) ? 0f : rng.Range(1f, 6f));
                else if (r < 0.7f) c = SteeringCommand.Pursue(other);
                else if (r < 0.8f) c = SteeringCommand.Jockey(other, new Vec2(0f, 34f), 1f);
                else if (r < 0.9f) c = SteeringCommand.Mark(other, new Vec2(105f, 34f)).WithFormation(near);
                else if (r < 0.95f) c = SteeringCommand.Evade(other);
                else c = SteeringCommand.Seek(near, 5f);
                k.SetCommand(s.Id, c);
            }
        }

        /// <summary>서브스텝 1회 시간을 n개 재고 평균·p95 (µs)를 돌려준다.</summary>
        static void MeasureSubsteps(int ticks, out double avgUs, out double p95Us)
        {
            var p = Params();
            var rng = new SeedRng(13UL);
            var k = Squad(p, rng);
            var samples = new double[ticks * 10];
            var toUs = 1e6 / Stopwatch.Frequency;
            int n = 0;
            double sum = 0;
            for (int t = 0; t < ticks; t++)
            {
                Commands(k, rng);
                for (int s = 0; s < 10; s++)
                {
                    var t0 = Stopwatch.GetTimestamp();
                    k.Step();
                    var us = (Stopwatch.GetTimestamp() - t0) * toUs;
                    samples[n++] = us;
                    sum += us;
                }
            }
            avgUs = sum / n;
            Array.Sort(samples);
            p95Us = samples[(int)(0.95 * (n - 1))];
        }

        [Test, Performance]
        public void Substep22_Avg_us()
        {
            MeasureSubsteps(1000, out _, out _); // 워밍업 (JIT)
            var sg = new SampleGroup("us_per_op", SampleUnit.Microsecond, false);
            for (int r = 0; r < Repeats; r++)
            {
                MeasureSubsteps(3000, out var avg, out _);
                Measure.Custom(sg, avg);
            }
        }

        [Test, Performance]
        public void Substep22_P95_us()
        {
            MeasureSubsteps(1000, out _, out _);
            var sg = new SampleGroup("us_per_op", SampleUnit.Microsecond, false);
            for (int r = 0; r < Repeats; r++)
            {
                MeasureSubsteps(3000, out _, out var p95);
                Measure.Custom(sg, p95);
            }
        }

        [Test, Performance]
        public void Tti_ns()
        {
            var p = Params();
            var pr = PlayerMotionProfile.Create(new KinematicAttributes(70, 70, 60, 50, 50, 60, 60), p);
            var rng = new SeedRng(5UL);
            var pos = new Vec2[256];
            var vel = new Vec2[256];
            var tgt = new Vec2[256];
            for (int i = 0; i < 256; i++)
            {
                pos[i] = new Vec2(rng.Range(0f, 105f), rng.Range(0f, 68f));
                vel[i] = new Vec2(rng.Range(-6f, 6f), rng.Range(-6f, 6f));
                tgt[i] = new Vec2(rng.Range(0f, 105f), rng.Range(0f, 68f));
            }
            Record("ns_per_op", SampleUnit.Nanosecond, 1e6, 5_000_000, n =>
            {
                float sink = 0f;
                for (long i = 0; i < n; i++)
                {
                    var j = (int)(i & 255);
                    sink += TimeToIntercept.Estimate(pr, pos[j], vel[j], 0.8f, 0.3f, p, tgt[j]);
                }
                return sink;
            });
        }

        [Test, Performance]
        public void BallIntercept_us()
        {
            var p = Params();
            var bp = BallParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(DataPath("ball.json"))), SurfaceCondition.Dry, PitchSpec.Default);
            var pred = new BallPrediction(64);
            BallPredictor.Predict(BallState.RollingAt(new Vec2(30f, 34f), new Vec2(14f, 2f), bp.Radius), bp, 0.04f, 2.0f, pred);
            var pr = PlayerMotionProfile.Create(new KinematicAttributes(60, 60, 60, 50, 50, 60, 60), p);
            // 최악: 끝까지 못 찾는 선수 (2 s / 0.04 s = 50 샘플 전부 검사)
            Record("us_per_op", SampleUnit.Microsecond, 1e3, 200_000, n =>
            {
                float sink = 0f;
                for (long i = 0; i < n; i++)
                {
                    TimeToIntercept.FindBallIntercept(pr, new Vec2(30f, 5f), Vec2.Zero, 1f, 0f, p, pred, 2.5f, out var pt, out var t);
                    sink += pt.X + t;
                }
                return sink;
            });
        }

        /// <summary>서브스텝(진단·모니터 끔)·TTI·인터셉트 경로 GC 할당 = 0 (AC-13·15).</summary>
        [Test, Performance]
        public void ZeroAlloc_StepTtiIntercept()
        {
            var p = Params();
            var rng = new SeedRng(17UL);
            var k = Squad(p, rng);
            var bp = BallParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(DataPath("ball.json"))), SurfaceCondition.Dry, PitchSpec.Default);
            var pred = new BallPrediction(64);
            BallPredictor.Predict(BallState.RollingAt(new Vec2(30f, 34f), new Vec2(14f, 2f), bp.Radius), bp, 0.04f, 2.0f, pred);
            float sink = 0f;

            void Work()
            {
                for (int t = 0; t < 50; t++)
                {
                    Commands(k, rng);
                    for (int s = 0; s < 10; s++) k.Step();
                    k.ApplyContact(1 + t % 22);
                }
                var pr = k.Profile(0);
                for (int i = 0; i < 100; i++)
                {
                    sink += TimeToIntercept.Estimate(pr, new Vec2(i, 30f), new Vec2(2f, 1f), 0.9f, 0.1f, p, new Vec2(50f, 30f));
                    TimeToIntercept.FindBallIntercept(pr, new Vec2(40f, 30f + i * 0.1f), Vec2.Zero, 1f, 0f, p, pred, 2.5f, out var pt, out _);
                    sink += pt.X;
                }
            }

            Work(); // 워밍업

            object junk = null;
            var control = CountAllocs(() => junk = new object[16]);
            Assert.That(control, Is.GreaterThan(0), "할당 측정이 동작하지 않는다 (대조군 0회)");
            GC.KeepAlive(junk);

            var sg = new SampleGroup("gc_alloc_count", SampleUnit.Undefined, false);
            for (int r = 0; r < 5; r++)
            {
                var allocs = CountAllocs(Work);
                Measure.Custom(sg, allocs);
                Assert.That(allocs, Is.EqualTo(0), $"GC 할당 {allocs}회");
            }
            Assert.That(float.IsNaN(sink), Is.False);
        }

        static void Record(string group, SampleUnit unit, double scale, long iterations, Func<long, float> loop)
        {
            var sg = new SampleGroup(group, unit, false);
            loop(iterations / 10);
            for (int r = 0; r < Repeats; r++)
            {
                var sw = Stopwatch.StartNew();
                var sink = loop(iterations);
                sw.Stop();
                Measure.Custom(sg, sw.Elapsed.TotalMilliseconds * scale / iterations);
                if (float.IsNaN(sink)) Assert.Fail("sink");
            }
        }

        static int CountAllocs(Action action)
        {
            var rec = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            rec.FilterToCurrentThread();
            rec.enabled = true;
            action();
            rec.enabled = false;
            rec.CollectFromAllThreads();
            return rec.sampleBlockCount;
        }
    }
}
