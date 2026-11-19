using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>specs/073 성능 (AC-06) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 073`, D17). 할당 0은 Mono에서도 판정한다.</summary>
    public class KeeperLeavePerfTests
    {
        static string Data => Path.Combine(Directory.GetCurrentDirectory(), "Data");

        static void Record(Dictionary<string, double> d, string key) => Measure.Custom(new SampleGroup(key, SampleUnit.Microsecond, false), d[key]);

        [Test, Performance]
        public void Perf_KeeperLeave()
        {
            var w = new KeeperLeaveBenchWorkloads(Data);
            for (int i = 0; i < 3; i++)
            {
                var d = new Dictionary<string, double>();
                w.SweepGate(d);
                w.SweepQuery(d);
                w.CrossDecision(d);
                w.OneOnOneDecision(d);
                Record(d, "kl_sweep_gate_p95_us");
                Record(d, "kl_sweep_query_p95_us");
                Record(d, "kl_cross_p95_us");
                Record(d, "kl_1v1_p95_us");
                if (i == 2) TestContext.WriteLine($"Mono p95: 스위퍼 게이트 {d["kl_sweep_gate_p95_us"]:F3} µs · 게이트+질의 {d["kl_sweep_query_p95_us"]:F3} µs · 크로스 {d["kl_cross_p95_us"]:F3} µs · 1v1 {d["kl_1v1_p95_us"]:F3} µs");
            }
            var bytes = w.GcBytes();
            Measure.Custom(new SampleGroup("kl_gc_bytes", SampleUnit.Byte, false), bytes);
            Assert.AreEqual(0L, bytes, "GK 판단 할당 0 B");
        }
    }
}
