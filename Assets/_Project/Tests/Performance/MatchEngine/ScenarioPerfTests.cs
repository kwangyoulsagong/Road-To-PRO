using System;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>specs/013 성능 (AC-16) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 013`, D17). 틱당 할당 0만 Mono에서도 판정한다.</summary>
    public class ScenarioPerfTests
    {
        static ScenarioBenchWorkloads W() => new ScenarioBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        static RoadToPro.MatchEngine.Scenarios.ScenarioLibrary Lib(ScenarioBenchWorkloads w) => w.SynthLibrary(262);

        static void Record(string group, SampleUnit unit, double v) => Measure.Custom(new SampleGroup(group, unit, false), v);

        [Test, Performance]
        public void Tick_us()
        {
            var w = W();
            var lib = Lib(w);
            for (int i = 0; i < 2; i++)
            {
                var d = w.Tick(lib);
                Record("scenario_tick_avg_us", SampleUnit.Microsecond, d["scenario_tick_avg_us"]);
                Record("scenario_tick_p95_us", SampleUnit.Microsecond, d["scenario_tick_p95_us"]);
                Record("scenario_match_s", SampleUnit.Second, d["scenario_match_s"]);
            }
        }

        [Test, Performance]
        public void Load_270()
        {
            var w = W();
            for (int i = 0; i < 3; i++)
            {
                w.Load(262, out var ms, out _);
                Record("load_270_ms", SampleUnit.Millisecond, ms);
            }
        }

        [Test, Performance]
        public void Tick_NoAlloc_DiagnosticsOff()
        {
            var w = W();
            var lib = Lib(w);
            w.NewMatch(lib).RunUntilTick(9000);
            var sim = w.NewMatch(lib);
            sim.RunUntilTick(3000);
            object junk = null;
            Assert.That(CountAllocs(() => junk = new object[16]), Is.GreaterThan(0), "할당 측정이 동작하지 않는다 (대조군 0회)");
            GC.KeepAlive(junk);
            var allocs = CountAllocs(() => sim.RunUntilTick(9000));
            Measure.Custom(new SampleGroup("gc_alloc_count", SampleUnit.Undefined, false), allocs);
            Assert.That(allocs, Is.EqualTo(0), "결정 틱 경로 GC 할당 0 (AC-16·19: 진단을 끈 실행)");
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
