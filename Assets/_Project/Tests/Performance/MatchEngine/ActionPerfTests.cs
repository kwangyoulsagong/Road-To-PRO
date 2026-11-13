using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>specs/008 성능 (AC-14) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 008`, D17). 할당 0은 Mono에서도 판정한다.</summary>
    public class ActionPerfTests
    {
        static ActionBenchWorkloads W() => new ActionBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        static void Record(Dictionary<string, double> d, string key, SampleUnit unit) => Measure.Custom(new SampleGroup(key, unit, false), d[key]);

        [Test, Performance]
        public void Logistic_Once_us()
        {
            var w = W();
            for (int i = 0; i < 3; i++) { var d = new Dictionary<string, double>(); w.Logistic(d); Record(d, "act_xpass_logistic_us", SampleUnit.Microsecond); }
        }

        [Test, Performance]
        public void Physics_Once_us()
        {
            var w = W();
            for (int i = 0; i < 3; i++) { var d = new Dictionary<string, double>(); w.Physics(d, 500); Record(d, "act_xpass_physics_p95_us", SampleUnit.Microsecond); }
        }

        [Test, Performance]
        public void Pressure_Team_us()
        {
            var w = W();
            for (int i = 0; i < 3; i++) { var d = new Dictionary<string, double>(); w.Pressure(d); Record(d, "act_pressure_team_avg_us", SampleUnit.Microsecond); }
        }

        [Test, Performance]
        public void OwnerTick_us()
        {
            var d = new Dictionary<string, double>();
            W().OwnerTick(d, 18_000);
            Record(d, "act_owner_tick_avg_us", SampleUnit.Microsecond);
            Record(d, "act_owner_tick_p95_us", SampleUnit.Microsecond);
        }

        [Test, Performance]
        public void Gc_Zero()
        {
            var bytes = W().GcBytes();
            Measure.Custom(new SampleGroup("act_gc_bytes", SampleUnit.Byte, false), bytes);
            Assert.AreEqual(0L, bytes, "평가·실행 경로 할당 0 B");
        }
    }
}
