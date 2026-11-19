using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>specs/074 성능 (AC-05) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 074`, D17). 할당 0은 Mono에서도 판정한다.</summary>
    public class PenaltyDistributionPerfTests
    {
        static string Data => Path.Combine(Directory.GetCurrentDirectory(), "Data");

        static void Record(Dictionary<string, double> d, string key) => Measure.Custom(new SampleGroup(key, SampleUnit.Microsecond, false), d[key]);

        [Test, Performance]
        public void Perf_PenaltyDistribution()
        {
            var w = new PenaltyDistributionBenchWorkloads(Data);
            for (int i = 0; i < 3; i++)
            {
                var d = new Dictionary<string, double>();
                w.Distribution(d);
                w.PenaltyStrategy(d);
                Record(d, "pd_dist_p95_us");
                Record(d, "pd_pk_p95_us");
                if (i == 2) TestContext.WriteLine($"Mono p95: 배급 선택 {d["pd_dist_p95_us"]:F3} µs · PK 전략 {d["pd_pk_p95_us"]:F3} µs");
            }
            var bytes = w.GcBytes();
            Measure.Custom(new SampleGroup("pd_gc_bytes", SampleUnit.Byte, false), bytes);
            Assert.AreEqual(0L, bytes, "배급·PK 판단 할당 0 B");
        }
    }
}
