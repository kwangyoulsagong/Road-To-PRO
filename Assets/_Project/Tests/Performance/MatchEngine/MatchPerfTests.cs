using System;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>
    /// specs/006 성능 (AC-21) — EditMode(Mono) 회귀 기준선. 판정 런타임은 IL2CPP(`farm.py bench --spec 006`, 2026-11-03 결정 D17).
    /// 틱당 할당 0 B만 Mono에서도 판정한다 (런타임과 무관). 워크로드는 IL2CPP 벤치와 같은 <see cref="MatchBenchWorkloads"/>.
    /// </summary>
    public class MatchPerfTests
    {
        static MatchBenchWorkloads W() => new MatchBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        static void Record(string group, SampleUnit unit, double v) => Measure.Custom(new SampleGroup(group, unit, false), v);

        [Test, Performance]
        public void SkeletonMatch_ms()
        {
            var w = W();
            w.SkeletonMatchMs();
            for (int i = 0; i < 3; i++) Record("ms", SampleUnit.Millisecond, w.SkeletonMatchMs());
        }

        [Test, Performance]
        public void FullMatch_PhysicsAndTick_us()
        {
            var w = W();
            for (int i = 0; i < 2; i++)
            {
                var d = w.FullMatch();
                Record("physics_us_per_tick", SampleUnit.Microsecond, d["physics_us_per_tick"]);
                Record("tick_avg_us", SampleUnit.Microsecond, d["tick_avg_us"]);
                Record("stages_sum_us", SampleUnit.Microsecond, d["stages_sum_us"]);
            }
        }

        [Test, Performance]
        public void Checkpoint_SizeAndRestore()
        {
            var w = W();
            for (int i = 0; i < 3; i++) // 표본 3개 중앙값 (단일 표본은 +10% 회귀 판정에 흔들림 — 006 깨끗한 트리 재검증에서 발견)
            {
                var d = w.Checkpoints();
                Record("checkpoint_max_kb", SampleUnit.Undefined, d["checkpoint_max_kb"]);
                Record("restore_avg_ms", SampleUnit.Millisecond, d["restore_avg_ms"]);
            }
        }

        [Test, Performance]
        public void Clip11s_ms()
        {
            var w = W();
            for (int i = 0; i < 3; i++) Record("ms", SampleUnit.Millisecond, w.Clip11sMs());
        }

        [Test, Performance]
        public void Memory_LogAndRing_MB()
        {
            var d = W().Memory();
            Record("event_log_mb", SampleUnit.Undefined, d["event_log_mb"]);
            Record("ring_buffer_mb", SampleUnit.Undefined, d["ring_buffer_mb"]);
        }

        /// <summary>
        /// 틱당 할당 0 (워밍업 후, 보관 모드 None). Mono 에디터는 GC.GetAllocatedBytesForCurrentThread가 0을 돌려줘서
        /// 005와 같은 Unity Profiler GC.Alloc 레코더로 6,000틱 동안의 할당 횟수를 센다 (대조군으로 측정 동작 확인).
        /// </summary>
        [Test, Performance]
        public void AllocPerTick_GcAllocCount()
        {
            var w = W();
            w.NewMatch().RunUntilTick(9000); // 워밍업: 처음 호출되는 경로
            var sim = w.NewMatch();
            sim.RunUntilTick(3000);
            object junk = null;
            Assert.That(CountAllocs(() => junk = new object[16]), Is.GreaterThan(0), "할당 측정이 동작하지 않는다 (대조군 0회)");
            GC.KeepAlive(junk);
            var allocs = CountAllocs(() => sim.RunUntilTick(9000));
            Record("gc_alloc_count", SampleUnit.Undefined, allocs);
            Assert.That(allocs, Is.EqualTo(0), $"6,000틱 동안 GC 할당 {allocs}회");
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
