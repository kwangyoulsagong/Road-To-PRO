using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>specs/070 성능 (AC-09) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 070`, D17). 할당 0은 Mono에서도 판정한다.</summary>
    public class DuelPerfTests
    {
        static DuelBenchWorkloads W() => new DuelBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        static void Record(Dictionary<string, double> d, string key, SampleUnit unit) => Measure.Custom(new SampleGroup(key, unit, false), d[key]);

        [Test, Performance]
        public void Models_us()
        {
            var d = new Dictionary<string, double>();
            W().Models(d);
            foreach (var k in new[] { "duel_takeon_eval_us", "duel_tackle_eval_us", "duel_steal_judge_us", "duel_gap_substep_us", "duel_contact_us" })
            {
                Record(d, k, SampleUnit.Microsecond);
                TestContext.WriteLine($"{k} = {d[k]:F3}");
            }
        }

        [Test, Performance]
        public void Models_NoAlloc()
        {
            var w = W();
            var d = new Dictionary<string, double>();
            w.Models(d, 20); // 워밍업
            d.Clear();
            w.Models(d, 200);
            Record(d, "duel_gc_bytes_models", SampleUnit.Byte);
            Assert.That(d["duel_gc_bytes_models"], Is.EqualTo(0.0), "결투 평가·접촉 기록 경로 GC 할당 0 B");
        }

        [Test, Performance]
        public void MatchTick_NoAlloc()
        {
            var d = new Dictionary<string, double>();
            W().Match(d, 12_000);
            Record(d, "duel_gc_bytes_per_tick", SampleUnit.Byte);
            Record(d, "duel_match_tick_avg_us", SampleUnit.Microsecond);
            TestContext.WriteLine($"틱 평균 {d["duel_match_tick_avg_us"]:F1} µs, p95 {d["duel_match_tick_p95_us"]:F1}, 틱당 할당 {d["duel_gc_bytes_per_tick"]:F2} B, 터치 {d["duel_touches"]}, 태클 {d["duel_tackles"]}, 접촉 {d["duel_contacts"]}");
            Assert.That(d["duel_gc_bytes_per_tick"], Is.EqualTo(0.0), "실경기 틱(결투 포함) GC 할당 0 B");
        }
    }
}
