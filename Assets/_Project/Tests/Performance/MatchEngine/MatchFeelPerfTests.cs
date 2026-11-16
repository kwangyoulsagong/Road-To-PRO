using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>
    /// specs/081 성능 (AC-09) — EditMode(Mono) 회귀 기준선. 010 결정 틱(착지점 추격·박스 지점 크로스 후보·항력 포함 착지 예측 포함)과 실경기 틱 할당.
    /// 판정은 IL2CPP(`farm.py bench --spec 081`, D17). 할당 0은 Mono에서도 판정한다. 장면 선정(≤ 5 ms)은 `DeadBallPerfTests.LiveSceneSelector_ms`.
    /// </summary>
    public class MatchFeelPerfTests
    {
        static string Data => Path.Combine(Directory.GetCurrentDirectory(), "Data");

        static void Record(Dictionary<string, double> d, string key, SampleUnit unit) => Measure.Custom(new SampleGroup(key, unit, false), d[key]);

        [Test, Performance]
        public void Perf_DecisionTick_WithAerialChase()
        {
            var d = new Dictionary<string, double>();
            new DecisionBenchWorkloads(Data).Tick(d);
            Record(d, "dec_tick_avg_us", SampleUnit.Microsecond);
            Record(d, "dec_tick_p95_us", SampleUnit.Microsecond);
            TestContext.WriteLine($"결정 틱 평균 {d["dec_tick_avg_us"]:F2} µs, p95 {d["dec_tick_p95_us"]:F2} µs, 틱당 할당 {d["dec_gc_bytes_per_tick"]:F2} B");
            Assert.That(d["dec_gc_bytes_per_tick"], Is.EqualTo(0.0), "결정 틱(081 규칙 포함) GC 할당 0 B");
        }

        [Test, Performance]
        public void Perf_Tick_ZeroGc()
        {
            var d = new Dictionary<string, double>();
            new ShotAerialBenchWorkloads(Data).Match(d, 12_000);
            Record(d, "shot_gc_bytes_per_tick", SampleUnit.Byte);
            Record(d, "shot_match_tick_avg_us", SampleUnit.Microsecond);
            TestContext.WriteLine($"실경기 틱 평균 {d["shot_match_tick_avg_us"]:F1} µs, 틱당 할당 {d["shot_gc_bytes_per_tick"]:F2} B, 경합 {d["aerial_contests"]}, 헤더 {d["aerial_headers"]}");
            Assert.That(d["shot_gc_bytes_per_tick"], Is.EqualTo(0.0), "실경기 틱(081 포함) GC 할당 0 B");
        }
    }
}
