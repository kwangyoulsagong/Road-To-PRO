using NUnit.Framework;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 T13: 점수 분해 상위 3·출처 집계·활동 집계 (FR-60·61·62·99·104).</summary>
    public class DecisionTelemetryTests
    {
        [Test]
        public void Trace_Top3_SortedWithBreakdown()
        {
            var f = new DecisionFixture();
            f.Style.SetStyle(9, new PlayStyle { Runs = 80f }.WithHabit(Habit.LineBreaker, 1));
            var tel = new DecisionTelemetry(16);
            var rng = new SeedRng(60UL);
            for (int i = 0; i < 5; i++)
            {
                f.ResetState(9);
                f.Decide(9, rng, true);
                tel.OnDecision(f.W.Tick, 9, in f.State[9], f.Decider, f.W);
            }
            Assert.AreEqual(5, tel.Count);
            var r = tel.At(4);
            Assert.That(r.TopCount, Is.InRange(1, 3));
            if (r.TopCount > 1) Assert.GreaterOrEqual(r.Top(0).Score, r.Top(1).Score);
            if (r.TopCount > 2) Assert.GreaterOrEqual(r.Top(1).Score, r.Top(2).Score);
            Assert.Greater(r.Temperature, 0f);
            for (int i = 0; i < r.TopCount; i++)
                if (r.Top(i).Kind == Consideration.RunInBehind) { Assert.AreEqual(2f, r.Top(i).Habit, 1e-6f); Assert.AreEqual(1.3f, r.Top(i).Tendency, 1e-6f); }
        }

        [Test]
        public void SlotFilter_RecordsOnlyThatPlayer_ButAggregatesAll()
        {
            var f = new DecisionFixture();
            var tel = new DecisionTelemetry(16) { SlotFilter = 9 };
            var rng = new SeedRng(61UL);
            f.Decide(9, rng, true); tel.OnDecision(1, 9, in f.State[9], f.Decider, f.W);
            f.Decide(10, rng, true); tel.OnDecision(1, 10, in f.State[10], f.Decider, f.W);
            Assert.AreEqual(1, tel.Count);
            Assert.AreEqual(1, tel.OffBallDecisions[10], "집계는 전원");
        }

        [Test]
        public void Match_AggregatesFilled()
        {
            DecisionTelemetry tel = null;
            var o = new MatchSimOptions { DecisionDiagnostics = () => tel = new DecisionTelemetry(1024) };
            var sim = ShapeTestKit.Sim(o);
            sim.StepTicks(6000);
            Assert.NotNull(tel);
            long attempts = 0, press = 0;
            float sprint = 0f, hi = 0f;
            for (int s = 0; s < 22; s++)
            {
                for (int k = 0; k < Considerations.Count; k++) attempts += tel.Attempts[s, k];
                press += tel.PressAttempts[s];
                sprint += tel.SprintDistance[s];
                hi += tel.HighIntensitySeconds[s];
            }
            Assert.Greater(attempts, 100);
            Assert.Greater(press, 0);
            Assert.Greater(sprint, 0f);
            Assert.Greater(hi, 0f);
            Assert.Greater(tel.Ticks, 5000);
            TestContext.WriteLine($"질의 평균 {(double)tel.SpaceQueries / tel.Ticks:F2}/틱, 최대 {tel.MaxSpaceQueries}");
        }
    }
}
