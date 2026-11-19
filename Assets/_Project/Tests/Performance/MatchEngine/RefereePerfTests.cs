using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>specs/012 성능 (AC-21) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 012`, D17). 할당 0은 Mono에서도 판정한다.</summary>
    public class RefereePerfTests
    {
        static string Data => Path.Combine(Directory.GetCurrentDirectory(), "Data");

        static void Record(Dictionary<string, double> d, string key) => Measure.Custom(new SampleGroup(key, SampleUnit.Microsecond, false), d[key]);

        [Test, Performance]
        public void Perf_Referee()
        {
            var w = new RefereeBenchWorkloads(Data);
            for (int i = 0; i < 3; i++)
            {
                var d = new Dictionary<string, double>();
                w.Contact(d);
                w.Offside(d);
                w.Officials(d);
                Record(d, "ref_contact_p95_us");
                Record(d, "ref_offside_p95_us");
                Record(d, "ref_officials_p95_us");
                if (i == 2) TestContext.WriteLine($"Mono p95: 접촉 판정 {d["ref_contact_p95_us"]:F3} µs · 오프사이드 {d["ref_offside_p95_us"]:F3} µs · 심판 위치+시야 {d["ref_officials_p95_us"]:F3} µs");
            }
            var bytes = w.GcBytes();
            Measure.Custom(new SampleGroup("ref_gc_bytes", SampleUnit.Byte, false), bytes);
            Assert.AreEqual(0L, bytes, "심판 판정 할당 0 B");
        }
    }
}
