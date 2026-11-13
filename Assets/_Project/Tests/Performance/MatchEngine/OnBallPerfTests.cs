using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>
    /// specs/072 성능 (AC-12) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 072`, D17).
    /// 할당 0은 런타임과 무관해 Mono에서도 판정한다(경기 틱 경로 전체는 010 <c>DecisionTick_NoAlloc_GcAllocCount</c>가 072 포함으로 잰다).
    /// </summary>
    public class OnBallPerfTests
    {
        static OnBallBenchWorkloads W() => new OnBallBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        static void Record(Dictionary<string, double> d, string key, SampleUnit unit) => Measure.Custom(new SampleGroup(key, unit, false), d[key]);

        [Test, Performance]
        public void OwnerDecision_us()
        {
            var d = new Dictionary<string, double>();
            W().Owner(d, 18_000);
            Record(d, "onball_owner_avg_us", SampleUnit.Microsecond);
            Record(d, "onball_owner_p95_us", SampleUnit.Microsecond);
            Record(d, "onball_candidates_avg", SampleUnit.Undefined);
            Assert.Greater(d["onball_owner_samples"], 100);
            Assert.LessOrEqual(d["onball_candidates_avg"], 24.0, "후보 상한 24 (FR-03)");
        }

        [Test, Performance]
        public void VisionSet_us()
        {
            var d = new Dictionary<string, double>();
            W().Vision(d);
            Record(d, "onball_vision_avg_us", SampleUnit.Microsecond);
            Record(d, "onball_vision_p95_us", SampleUnit.Microsecond);
        }

        [Test, Performance]
        public void OwnerDecision_NoAlloc_GcAllocCount()
        {
            var w = W();
            var d = new Dictionary<string, double>();
            w.Owner(d, 2_000); // 워밍업 (JIT·정적 초기화)
            var rec = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            rec.FilterToCurrentThread();
            object junk = null;
            rec.enabled = true;
            junk = new object[16];
            rec.enabled = false;
            rec.CollectFromAllThreads();
            Assert.That(rec.sampleBlockCount, Is.GreaterThan(0), "할당 측정이 동작하지 않는다 (대조군 0회)");
            GC.KeepAlive(junk);
            d.Clear();
            w.Owner(d, 6_000);
            var perDecision = d["onball_gc_bytes_per_decision"];
            Measure.Custom(new SampleGroup("onball_gc_bytes_per_decision", SampleUnit.Byte, false), perDecision);
            Assert.That(perDecision, Is.EqualTo(0.0), "온볼 결정 경로 GC 할당 0 B");
        }
    }
}
