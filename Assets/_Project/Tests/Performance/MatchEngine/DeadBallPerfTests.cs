using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Highlight.Selection;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>
    /// specs/058 AC-14 (Mono 회귀 기준선): 골 데드볼·재시작·걸어 나감 구간 틱 GC 0회, 장면 선정 시간. 판정은 IL2CPP 벤치(D17)와
    /// 같은 워크로드. 헤드리스 시뮬 시간 +5%는 `farm.py bench`의 006 항목 비교로 본다.
    /// </summary>
    public class DeadBallPerfTests
    {
        static MatchBenchWorkloads W() => new MatchBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        [Test, Performance]
        public void TickGc_GoalDeadBall_Zero()
        {
            var w = W();
            var full = w.NewMatch();
            full.RunToEnd();
            Assert.That(full.Result.Goals.Length, Is.GreaterThan(0), "벤치 경기에 골이 없다");
            var goal = full.Result.Goals[0].Tick;
            w.NewMatch().RunToEnd(); // 워밍업: 처음 호출되는 경로 전부
            var sim = w.NewMatch();
            sim.RunUntilTick(Math.Max(1, goal - 50));
            object junk = null;
            Assert.That(CountAllocs(() => junk = new object[16]), Is.GreaterThan(0), "할당 측정이 동작하지 않는다");
            GC.KeepAlive(junk);
            var allocs = CountAllocs(() => sim.RunUntilTick(goal + 1_200)); // 골 → 세리머니 → 복귀 → 킥오프 → 이어지는 재시작
            Measure.Custom(new SampleGroup("gc_alloc_count_deadball", SampleUnit.Undefined, false), allocs);
            Assert.That(allocs, Is.EqualTo(0), $"골 데드볼 구간 GC 할당 {allocs}회");
        }

        [Test, Performance]
        public void LiveSceneSelector_ms()
        {
            var sim = W().NewMatch();
            sim.RunToEnd();
            var events = sim.Result.EventLog;
            var t = new SceneScoreTable();
            LiveSceneSelector.Select(events, sim.Result.EndTick, PitchSpec.Default, -1, t); // 워밍업
            Measure.Method(() => LiveSceneSelector.Select(events, sim.Result.EndTick, PitchSpec.Default, -1, t))
                .WarmupCount(2).MeasurementCount(10).SampleGroup(new SampleGroup("scene_select_ms", SampleUnit.Millisecond)).Run();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) LiveSceneSelector.Select(events, sim.Result.EndTick, PitchSpec.Default, -1, t);
            sw.Stop();
            Assert.That(sw.Elapsed.TotalMilliseconds / 10d, Is.LessThanOrEqualTo(5d * 4d), "Mono 기준선이 IL2CPP 예산(5 ms)의 4배를 넘는다 — 벤치에서 확인");
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
