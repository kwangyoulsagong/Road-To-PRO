using System;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Time;
using RoadToPro.Core.Tuning;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.Core
{
    /// <summary>
    /// specs/001 성능 예산 (AC-18). 예산 수치는 Data/perf/001-core-foundation.json — farm logic이 판정한다.
    /// ns/op 항목은 루프 전체를 Stopwatch로 재서 1회 평균을 기록한다 (Stopwatch는 테스트 코드에서만).
    /// </summary>
    public class CorePerfTests
    {
        const int Repeats = 7;

        static void RecordNsPerOp(string group, long iterations, Func<long, float> loop)
        {
            var sg = new SampleGroup(group, SampleUnit.Nanosecond, false);
            loop(iterations / 10); // 워밍업 (JIT)
            for (int r = 0; r < Repeats; r++)
            {
                var sw = Stopwatch.StartNew();
                var sink = loop(iterations);
                sw.Stop();
                Measure.Custom(sg, sw.Elapsed.TotalMilliseconds * 1e6 / iterations);
                if (float.IsNaN(sink)) Assert.Fail("sink");
            }
        }

        [Test, Performance]
        public void Rng_Uniform_ns()
        {
            var rng = new SeedRng(1UL);
            RecordNsPerOp("ns_per_op", 100_000_000, n =>
            {
                float s = 0f;
                for (long i = 0; i < n; i++) s += rng.NextFloat();
                return s;
            });
        }

        [Test, Performance]
        public void Rng_Normal_ns()
        {
            var rng = new SeedRng(2UL);
            RecordNsPerOp("ns_per_op", 10_000_000, n =>
            {
                float s = 0f;
                for (long i = 0; i < n; i++) s += rng.Normal();
                return s;
            });
        }

        /// <summary>벡터 연산 1회 = 정규화 (가장 비싼 기본 연산: 제곱길이·sqrt·나눗셈).</summary>
        [Test, Performance]
        public void Vec_Normalize_ns()
        {
            RecordNsPerOp("ns_per_op", 10_000_000, n =>
            {
                float s = 0f;
                var v = new Vec2(0.3f, 0.7f);
                for (long i = 0; i < n; i++)
                {
                    v = new Vec2(v.Y + 0.001f, v.X);
                    s += v.Normalized().X;
                }
                return s;
            });
        }

        /// <summary>swept 판정 1회 (접촉하는 경우 — sqrt까지 가는 가장 긴 경로).</summary>
        [Test, Performance]
        public void Swept_ns()
        {
            var c = new Vec2(0.3f, 0.2f);
            RecordNsPerOp("ns_per_op", 10_000_000, n =>
            {
                float s = 0f;
                var a = new Vec2(-0.7f, 0.2f);
                var b = new Vec2(-0.1f, 0.2f);
                var d = new Vec2(0f, 1e-8f);
                for (long i = 0; i < n; i++)
                {
                    if (Geometry.SweptSegmentCircle(a, b, c, 0.5f, out var t)) s += t;
                    a += d;
                }
                return s;
            });
        }

        struct Ev
        {
            public long Tick;
            public int Type;
            public int Player;
            public float X, Y;
        }

        [Test, Performance]
        public void Hash_30kEvents_ms()
        {
            var events = new Ev[30_000];
            var rng = new SeedRng(3UL);
            for (int i = 0; i < events.Length; i++)
                events[i] = new Ev { Tick = i * 2, Type = rng.Range(0, 30), Player = rng.Range(1, 22), X = rng.NextFloat(), Y = rng.NextFloat() };
            ulong sink = 0;
            Measure.Method(() =>
                {
                    var h = StableHasher.Create();
                    for (int i = 0; i < events.Length; i++)
                    {
                        h.Add(events[i].Tick);
                        h.Add(events[i].Type);
                        h.Add(events[i].Player);
                        h.Add(events[i].X);
                        h.Add(events[i].Y);
                    }
                    sink ^= h.Value;
                })
                .WarmupCount(2)
                .MeasurementCount(10)
                .Run();
            Assert.That(sink, Is.Not.EqualTo(1UL));
        }

        [Test, Performance]
        public void Tuning_Load1000_ms()
        {
            var defs = new StringBuilder("{\"version\":1,\"definitions\":[");
            var vals = new StringBuilder("{\"version\":1,\"values\":[");
            for (int i = 0; i < 1000; i++)
            {
                if (i > 0)
                {
                    defs.Append(',');
                    vals.Append(',');
                }
                defs.Append("{\"id\":\"sys").Append(i % 17).Append(".param").Append(i)
                    .Append("\",\"default\":0.5,\"min\":0,\"max\":1,\"unit\":\"m/s\",\"tag\":\"[제안]\",\"source\":\"docs/index.html § 5\"}");
                vals.Append("{\"id\":\"sys").Append(i % 17).Append(".param").Append(i).Append("\",\"value\":0.75}");
            }
            defs.Append("]}");
            vals.Append("]}");
            var d = defs.ToString();
            var v = vals.ToString();
            Measure.Method(() =>
                {
                    var reg = TuningLoader.ParseDefinitions(d);
                    var b = TuningLoader.ParseBundle(reg, v);
                    if (b.Registry.Count != 1000) Assert.Fail("count");
                })
                // 할당이 많은 측정이라 GC 시점에 따라 흔들린다 → 측정마다 GC를 먼저 비우고 표본을 늘린다 (T24a)
                .SetUp(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                })
                .WarmupCount(5)
                .MeasurementCount(30)
                .Run();
        }

        sealed class Probe
        {
            public override string ToString() => "probe";
        }

        /// <summary>수학·RNG·시계·해시·비활성 로그 호출의 할당 = 0 B.</summary>
        [Test, Performance]
        public void ZeroAlloc_MathRngClockLog()
        {
            var rng = new SeedRng(4UL);
            var weights = new[] { 1f, 2f, 3f, 0f };
            var utils = new[] { 0.1f, 0.5f, 0.4f };
            var shuffle = new int[8];
            var timeline = new MatchTimeline(1800, 2400);
            var log = new MemoryLogger(LogLevel.Error);
            var probe = new Probe();
            float sink = 0f;

            void Work()
            {
                for (int i = 0; i < 10_000; i++)
                {
                    var a = new Vec2(i, 1f).Normalized();
                    var b = new Vec3(1f, i, 2f).Normalized();
                    sink += Angle.LerpShortest(a.X, b.Y, 0.3f) + Interp.Smoothstep(0f, 1f, a.Y) + DMath.Atan2(a.Y, a.X);
                    if (Geometry.SweptSegmentCircle(a, a + new Vec2(0.3f, 0f), Vec2.Zero, 0.5f, out var t)) sink += t;
                    sink += rng.NextFloat() + rng.Range(0, 9) + rng.Normal() + rng.PickWeighted(weights) + rng.PickSoftmax(utils, 0.2f);
                    rng.Shuffle(shuffle);
                    var mt = timeline.FromTick(i * 5);
                    sink += timeline.ToTick(mt);
                    var h = StableHasher.Create();
                    h.Add(i);
                    h.Add(sink);
                    sink += h.Value & 1;
                    log.Debug("x {0} {1}", i, sink);
                    log.Trace("p {0}", probe);
                }
            }

            Work(); // 워밍업 (JIT·정적 초기화)

            // Unity Mono는 GC.GetAllocatedBytesForCurrentThread가 0을 돌려준다 → 프로파일러 GC.Alloc 마커 횟수로 잰다
            // (Performance Testing 패키지 Measure.Method().GC()와 같은 방식). 대조군으로 측정이 동작하는지 먼저 확인.
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
