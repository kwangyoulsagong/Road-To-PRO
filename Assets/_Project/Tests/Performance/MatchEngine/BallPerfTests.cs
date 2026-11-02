using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>
    /// specs/004 성능 예산 (AC-14·16). 예산 수치는 Data/perf/004-ball-physics.json — farm logic이 판정한다.
    /// 루프 전체를 Stopwatch로 재서 1회 평균을 기록 (Stopwatch는 테스트 코드에서만).
    /// </summary>
    public class BallPerfTests
    {
        const int Repeats = 7;

        static BallParams Params()
        {
            var reg = TuningLoader.LoadDefinitionsFile(Path.Combine(Directory.GetCurrentDirectory(), "Data", "tuning", "ball.json"));
            return BallParams.Create(TuningBundle.Defaults(reg), SurfaceCondition.Dry, PitchSpec.Default);
        }

        static void Record(string group, SampleUnit unit, double scale, long iterations, Func<long, float> loop)
        {
            var sg = new SampleGroup(group, unit, false);
            loop(iterations / 10); // 워밍업 (JIT)
            for (int r = 0; r < Repeats; r++)
            {
                var sw = Stopwatch.StartNew();
                var sink = loop(iterations);
                sw.Stop();
                Measure.Custom(sg, sw.Elapsed.TotalMilliseconds * scale / iterations);
                if (float.IsNaN(sink)) Assert.Fail("sink");
            }
        }

        static void RecordNs(long n, Func<long, float> loop) => Record("ns_per_op", SampleUnit.Nanosecond, 1e6, n, loop);
        static void RecordUs(long n, Func<long, float> loop) => Record("us_per_op", SampleUnit.Microsecond, 1e3, n, loop);

        static BallState AirKick(BallParams p) =>
            BallState.InAir(new Vec3(30f, 34f, 1.5f), new Vec3(24f, 6f, 12f), new Vec3(10f, -40f, 60f));

        static BallState RollKick(BallParams p) => BallState.RollingAt(new Vec2(30f, 34f), new Vec2(14f, 4f), p.Radius);

        /// <summary>Air 서브스텝 (스핀 있음 = 항력 + 마그누스, 최악 경로).</summary>
        [Test, Performance]
        public void Substep_Air_ns()
        {
            var p = Params();
            var ev = new BallEventBuffer();
            RecordNs(5_000_000, n =>
            {
                var s = AirKick(p);
                for (long i = 0; i < n; i++)
                {
                    ev.Clear();
                    BallStepper.Step(ref s, p, ev, out _);
                    if (s.Phase != BallPhase.Air) s = AirKick(p);
                }
                return s.Position.X;
            });
        }

        [Test, Performance]
        public void Substep_Rolling_ns()
        {
            var p = Params();
            var ev = new BallEventBuffer();
            RecordNs(5_000_000, n =>
            {
                var s = RollKick(p);
                for (long i = 0; i < n; i++)
                {
                    ev.Clear();
                    BallStepper.Step(ref s, p, ev, out _);
                    if (s.Phase != BallPhase.Rolling) s = RollKick(p);
                }
                return s.Position.X;
            });
        }

        /// <summary>Controlled·Rest (적분 없음).</summary>
        [Test, Performance]
        public void Substep_Idle_ns()
        {
            var p = Params();
            var ev = new BallEventBuffer();
            RecordNs(10_000_000, n =>
            {
                var c = BallState.Controlled(3, new Vec3(30f, 34f, p.Radius));
                var r = BallState.AtRest(new Vec2(40f, 34f), p.Radius);
                for (long i = 0; i < n; i++)
                {
                    ev.Clear();
                    if ((i & 1) == 0) BallStepper.Step(ref c, p, ev, out _);
                    else BallStepper.Step(ref r, p, ev, out _);
                }
                return c.Position.X + r.Position.X;
            });
        }

        /// <summary>경기 중 상태 가중 평균: Controlled 50 · Rolling 25 · Air 15 · Rest 10 % (plan [제안] 승인).</summary>
        [Test, Performance]
        public void Substep_WeightedAvg_ns()
        {
            var p = Params();
            var ev = new BallEventBuffer();
            // 20칸 패턴: 0 = Controlled ×10, 1 = Rest ×2, 2 = Rolling ×5, 3 = Air ×3
            var pattern = new byte[] { 0, 2, 0, 3, 0, 2, 1, 0, 2, 0, 3, 0, 2, 0, 1, 2, 0, 3, 0, 0 };
            RecordNs(10_000_000, n =>
            {
                var c = BallState.Controlled(3, new Vec3(30f, 34f, p.Radius));
                var r = BallState.AtRest(new Vec2(40f, 34f), p.Radius);
                var roll = RollKick(p);
                var air = AirKick(p);
                for (long i = 0; i < n; i++)
                {
                    ev.Clear();
                    switch (pattern[i % 20])
                    {
                        case 0: BallStepper.Step(ref c, p, ev, out _); break;
                        case 1: BallStepper.Step(ref r, p, ev, out _); break;
                        case 2:
                            BallStepper.Step(ref roll, p, ev, out _);
                            if (roll.Phase != BallPhase.Rolling) roll = RollKick(p);
                            break;
                        default:
                            BallStepper.Step(ref air, p, ev, out _);
                            if (air.Phase != BallPhase.Air) air = AirKick(p);
                            break;
                    }
                }
                return air.Position.X + roll.Position.X;
            });
        }

        /// <summary>궤적 예측 10 s / 0.04 s (뜬 킥 → 바운스 → 구름).</summary>
        [Test, Performance]
        public void Predict10s_us()
        {
            var p = Params();
            var pred = new BallPrediction(250);
            var k = BallState.InAir(new Vec3(30f, 20f, p.Radius), new Vec3(16f, 4f, 8f), new Vec3(0f, -20f, 30f));
            RecordUs(2_000, n =>
            {
                float sink = 0f;
                for (long i = 0; i < n; i++)
                {
                    BallPredictor.Predict(k, p, 0.04f, 10f, pred);
                    sink += pred.Count;
                }
                return sink;
            });
        }

        /// <summary>땅볼 역산 1회 (목표 5~45 m, 고정 시드).</summary>
        [Test, Performance]
        public void GroundInverse_us()
        {
            var p = Params();
            var rng = new SeedRng(4UL);
            var targets = new float[256];
            for (int i = 0; i < targets.Length; i++) targets[i] = rng.Range(5f, 45f);
            RecordUs(5_000, n =>
            {
                float sink = 0f;
                for (long i = 0; i < n; i++) sink += BallInverse.GroundSpeedFor(p, targets[i & 255]).Speed;
                return sink;
            });
        }

        /// <summary>서브스텝(진단 끔)·예측·역산 경로 GC 할당 = 0 (AC-14·16).</summary>
        [Test, Performance]
        public void ZeroAlloc_StepPredictInverse()
        {
            var p = Params();
            var ev = new BallEventBuffer();
            var pred = new BallPrediction(250);
            float sink = 0f;

            void Work()
            {
                var air = AirKick(p);
                var roll = RollKick(p);
                var goal = BallState.RollingAt(new Vec2(3f, 34f), new Vec2(-25f, 0f), p.Radius);
                for (int i = 0; i < 2000; i++)
                {
                    ev.Clear();
                    BallStepper.Step(ref air, p, ev, out _);
                    BallStepper.Step(ref roll, p, ev, out _);
                    BallStepper.Step(ref goal, p, ev, out _, null);
                    ev.Clear();
                }
                BallPredictor.Predict(AirKick(p), p, 0.04f, 10f, pred);
                sink += pred.Count + air.Position.X + roll.Position.X + goal.Position.X;
                for (int i = 0; i < 20; i++) sink += BallInverse.GroundSpeedFor(p, 5f + i * 2f).Speed;
                sink += BallInverse.LoftedSpeedFor(p, 30f, 0.5f, Vec3.Zero, 100f, ev).Speed;
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
