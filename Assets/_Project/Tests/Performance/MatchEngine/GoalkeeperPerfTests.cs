using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>specs/011 성능 (AC-17) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 011`, D17). 할당 0은 Mono에서도 판정한다.</summary>
    public class GoalkeeperPerfTests
    {
        static GoalkeeperBenchWorkloads W() => new GoalkeeperBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        static void Record(Dictionary<string, double> d, string key, SampleUnit unit) => Measure.Custom(new SampleGroup(key, unit, false), d[key]);

        [Test, Performance]
        public void Position_Once_us()
        {
            var w = W();
            for (int i = 0; i < 3; i++)
            {
                var d = new Dictionary<string, double>();
                w.Position(d);
                Record(d, "gk_position_p95_us", SampleUnit.Microsecond);
                Record(d, "gk_position_avg_us", SampleUnit.Microsecond);
            }
        }

        [Test, Performance]
        public void Save_Once_us()
        {
            var w = W();
            for (int i = 0; i < 3; i++)
            {
                var d = new Dictionary<string, double>();
                w.Save(d);
                Record(d, "gk_save_p95_us", SampleUnit.Microsecond);
                Record(d, "gk_predict_p95_us", SampleUnit.Microsecond);
            }
        }

        [Test, Performance]
        public void PerTick_TwoKeepers_us()
        {
            var d = new Dictionary<string, double>();
            W().PerTick(d);
            Record(d, "gk_tick_avg_us", SampleUnit.Microsecond);
            Record(d, "gk_tick_p95_us", SampleUnit.Microsecond);
        }

        [Test, Performance]
        public void Gc_Zero()
        {
            var p = GoalkeeperParams.Defaults;
            var pos = new KeeperPositioner(p, new GoalGrid(p));
            var k = KeeperProfile.From(p, KeeperAttributes.Uniform(10.5f));
            var inp = new PositionInput { Goal = new Vec2(0f, 34f), DirIn = 1f, Ball = new Vec2(18f, 30f), LineHeight = 30f };
            pos.Target(in inp, in k);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5000; i++) { inp.Ball = new Vec2(8f + (i % 200) * 0.1f, 30f); pos.Target(in inp, in k); }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Measure.Custom(new SampleGroup("gk_gc_bytes", SampleUnit.Byte, false), bytes);
            Assert.AreEqual(0L, bytes, "위치 탐색 경로 할당 0 B");
        }
    }
}
