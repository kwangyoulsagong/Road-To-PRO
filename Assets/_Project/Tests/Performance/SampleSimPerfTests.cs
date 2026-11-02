using NUnit.Framework;
using RoadToPro.Verification;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance
{
    // specs/000 T28: 순수 C# 코어 성능 측정 경로 확인 (FR-41). 실제 경기 시뮬 측정은 001·006 이후.
    public class SampleSimPerfTests
    {
        [Test, Performance]
        public void Simulate_1000Ticks()
        {
            Measure.Method(() =>
                {
                    var sim = new SampleSim(42);
                    sim.Run();
                })
                .WarmupCount(3)
                .MeasurementCount(20)
                .GC()
                .Run();
        }
    }
}
