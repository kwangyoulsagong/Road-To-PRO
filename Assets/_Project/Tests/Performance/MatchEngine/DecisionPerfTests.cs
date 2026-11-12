using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>
    /// specs/010 성능 (AC-17) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 010`, D17).
    /// 질의 수·할당 0은 런타임과 무관해 Mono에서도 판정한다.
    /// </summary>
    public class DecisionPerfTests
    {
        static DecisionBenchWorkloads W() => new DecisionBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        static void Record(Dictionary<string, double> d, string key, SampleUnit unit) => Measure.Custom(new SampleGroup(key, unit, false), d[key]);

        [Test, Performance]
        public void DecisionTick_us_Queries()
        {
            var d = new Dictionary<string, double>();
            W().Tick(d);
            Record(d, "dec_tick_avg_us", SampleUnit.Microsecond);
            Record(d, "dec_tick_p95_us", SampleUnit.Microsecond);
            Record(d, "dec_match_s", SampleUnit.Second);
            Record(d, "dec_space_queries_avg", SampleUnit.Undefined);
            Record(d, "dec_space_queries_max", SampleUnit.Undefined);
            Assert.LessOrEqual(d["dec_space_queries_avg"], 3.0, "결정 틱당 피치 컨트롤 질의 평균 ≤ 3");
            Assert.LessOrEqual(d["dec_space_queries_max"], 8.0, "최대 ≤ 8");
        }

        [Test, Performance]
        public void OffBallPlayer_us()
        {
            var w = W();
            for (int i = 0; i < 3; i++)
            {
                var d = new Dictionary<string, double>();
                w.OffBall(d);
                Record(d, "dec_offball_avg_us", SampleUnit.Microsecond);
                Record(d, "dec_offball_p95_us", SampleUnit.Microsecond);
            }
        }

        [Test, Performance]
        public void DecisionTick_NoAlloc_GcAllocCount()
        {
            var w = W();
            w.NewMatch().RunUntilTick(9000);
            var sim = w.NewMatch();
            sim.RunUntilTick(3000);
            object junk = null;
            Assert.That(CountAllocs(() => junk = new object[16]), Is.GreaterThan(0), "할당 측정이 동작하지 않는다 (대조군 0회)");
            GC.KeepAlive(junk);
            var allocs = CountAllocs(() => sim.RunUntilTick(9000));
            Measure.Custom(new SampleGroup("dec_gc_alloc_count", SampleUnit.Undefined, false), allocs);
            Assert.That(allocs, Is.EqualTo(0), "결정 틱 경로 GC 할당 0 (진단을 끈 실행, AC-17·19)");
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
