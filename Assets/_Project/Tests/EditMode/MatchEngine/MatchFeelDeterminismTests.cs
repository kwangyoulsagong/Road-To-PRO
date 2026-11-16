using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;
using RoadToPro.Tests.EditMode.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine
{
    /// <summary>specs/081 AC-08: 박스 지점 크로스·뜬 공 착지점 추격·항력 포함 착지 예측이 들어간 90분 경기의 결정론.</summary>
    public class MatchFeelDeterminismTests
    {
        /// <summary>새 규칙이 실제로 도는 시드 (7400 = 측정 묶음 첫 경기).</summary>
        const ulong Seed = 7400UL;

        [Test, Category("Determinism"), Timeout(900_000)]
        public void Determinism_90min_FeelHashSame()
        {
            var sa = new MatchSim(ShapeTestKit.Setup(seed: Seed), ShapeTestKit.TuningWithShape());
            var a = sa.RunToEnd();
            var b = new MatchSim(ShapeTestKit.Setup(seed: Seed), ShapeTestKit.TuningWithShape()).RunToEnd();
            var t = MatchFeelMetrics.Count(sa.Log);
            MatchFeelMetrics.AddCounters(t, sa.Context);
            TestContext.WriteLine($"크로스 {t.Crosses}, 양 팀 경합 {t.BothTeamContests}, 헤더 {t.Headers}");
            Assert.Greater(t.Crosses + t.BothTeamContests, 0, "081 규칙이 이 경기에서 실제로 돈다");
            Assert.AreEqual(a.EventLogHash, b.EventLogHash);
            Assert.AreEqual(a.FinalStateHash, b.FinalStateHash);
        }

        [Test, Category("Determinism"), Timeout(900_000)]
        public void Determinism_DiagnosticsOnOff_SameHash()
        {
            DecisionRecorder r = null;
            var on = new MatchSim(ShapeTestKit.Setup(seed: Seed), ShapeTestKit.TuningWithShape(),
                new MatchSimOptions { DecisionDiagnostics = () => r = new DecisionRecorder() }).RunToEnd();
            var off = new MatchSim(ShapeTestKit.Setup(seed: Seed), ShapeTestKit.TuningWithShape()).RunToEnd();
            Assert.Greater(r.OnBallDecisions, 0);
            Assert.AreEqual(on.EventLogHash, off.EventLogHash, "진단 켬/끔이 이벤트 해시를 바꾸지 않는다");
            Assert.AreEqual(on.FinalStateHash, off.FinalStateHash);
        }
    }
}
