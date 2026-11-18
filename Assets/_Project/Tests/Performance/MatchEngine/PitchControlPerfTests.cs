using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Bench;
using Unity.PerformanceTesting;

namespace RoadToPro.Tests.Performance.MatchEngine
{
    /// <summary>
    /// specs/007 성능 (AC-12) — EditMode(Mono) 회귀 기준선. 판정은 IL2CPP(`farm.py bench --spec 007`, D17).
    /// 할당 0·가지치기 차이 ≤ 1e-3·경기 중 그리드 호출 0은 Mono에서도 판정한다.
    /// </summary>
    public class PitchControlPerfTests
    {
        static string Data => Path.Combine(Directory.GetCurrentDirectory(), "Data");
        static PitchControlBenchWorkloads W() => new PitchControlBenchWorkloads(Data);

        static void Record(Dictionary<string, double> d, string key, SampleUnit unit) => Measure.Custom(new SampleGroup(key, unit, false), d[key]);

        [Test, Performance]
        public void Queries_us()
        {
            var w = W();
            for (int i = 0; i < 3; i++)
            {
                var d = new Dictionary<string, double>();
                w.Point(d, 500);
                w.PointWorst(d, 500);
                w.Trajectory(d, 500);
                w.Grid(d, 5);
                Record(d, "pc_point_avg_us", SampleUnit.Microsecond);
                Record(d, "pc_point_worst_us", SampleUnit.Microsecond);
                Record(d, "pc_traj_avg_us", SampleUnit.Microsecond);
                Record(d, "pc_grid_ms", SampleUnit.Millisecond);
                if (i == 2) TestContext.WriteLine($"Mono: 지점 {d["pc_point_avg_us"]:F2} µs · 최악 {d["pc_point_worst_us"]:F2} µs · 궤적 {d["pc_traj_avg_us"]:F2} µs · 그리드 {d["pc_grid_ms"]:F2} ms · 궤적당 TTI {d["pc_traj_tti_per_query"]:F0}회·샘플 {d["pc_traj_samples_per_query"]:F0}");
            }
        }

        [Test, Performance]
        public void Gc_Zero()
        {
            var bytes = W().GcBytes();
            Measure.Custom(new SampleGroup("pc_gc_bytes", SampleUnit.Byte, false), bytes);
            Assert.AreEqual(0L, bytes, "지점·궤적·그리드 질의 할당 0 B");
        }

        [Test]
        public void Prune_MaxDiff_Below1e3()
        {
            var diff = W().PruneMaxDiff();
            TestContext.WriteLine($"가지치기 대 전체 적분 최대 차이 {diff:E2}");
            Assert.LessOrEqual(diff, 1e-3, "FR-04 결과 차이 ≤ 1e-3");
        }

        [Test]
        public void TrajectoryInterp_MaxDiff()
        {
            var diff = W().InterpMaxDiff();
            TestContext.WriteLine($"궤적 TTI 보간 대 정확값 최대 차이 {diff:E2}");
            Assert.LessOrEqual(diff, 0.01, "보간 오차는 판단에 의미 없는 크기여야 한다 (≤ 0.01)");
        }

        [Test]
        public void TrajectoryPrune_MaxDiff()
        {
            var diff = W().TrajPruneMaxDiff();
            TestContext.WriteLine($"궤적 가지치기 대 전체 적분 최대 차이 {diff:E2}");
            // 궤적 결과는 수렴 문턱(tol 0.01)에서 tol 크기 불연속이 있다(한 샘플 차이로 수렴/끝 지점 분배가 갈림) — 가지치기 자체 오차 1e-3을 그 위에 허용
            var tol = RoadToPro.MatchEngine.PitchControl.PitchControlParams.Defaults.Tol;
            Assert.LessOrEqual(diff, tol + 1e-3, "궤적 먼 선수 건너뛰기 차이 ≤ tol + 1e-3 (AC-12)");
        }

        [Test, Timeout(600_000)]
        public void Match_NoGridQueries()
        {
            var sim = new DecisionBenchWorkloads(Data).NewMatch();
            sim.RunToEnd();
            var q = sim.Context.Actions.PitchControl;
            TestContext.WriteLine($"경기 1개: 지점 {q.PointCount} · 궤적 {q.TrajectoryCount} · 그리드 {q.GridCount}");
            Assert.Greater(q.PointCount, 0);
            Assert.Greater(q.TrajectoryCount, 0);
            Assert.AreEqual(0, q.GridCount, "진단 끈 경기 중 그리드 질의 0 (FR-11)");
        }
    }
}
