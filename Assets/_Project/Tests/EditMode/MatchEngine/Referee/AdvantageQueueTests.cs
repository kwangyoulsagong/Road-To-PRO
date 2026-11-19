using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-10 (FR-40~43, P5): 보류 창 2.9 s 소멸 · 3 s 유지 해제 · 창 안 골 · 경계.</summary>
    public class AdvantageQueueTests
    {
        static PendingFoul Foul(long tick) => new PendingFoul { Tick = tick, Offender = 14, Victim = 5, OffenderId = 114, VictimId = 105, VictimTeam = 0, Spot = new Vec2(70f, 30f), Decision = 2, FinalDecision = 2 };

        [Test]
        public void Law12_Advantage_ShouldPlay_ScoreAboveThreshold()
        {
            var avg = RefereeProfile.LeagueAverage;
            var near = AdvantageQueue.Score(P, true, 15f);
            var far = AdvantageQueue.Score(P, true, 60f);
            Assert.IsTrue(AdvantageQueue.ShouldPlay(avg, near), "골 15 m + 점유 유지 → 어드밴티지");
            Assert.IsFalse(AdvantageQueue.ShouldPlay(avg, far), "먼 지역 → 휘슬");
            Assert.AreEqual(0f, AdvantageQueue.Score(P, false, 10f), "점유 잃음 → 0");
        }

        [Test]
        public void Law12_Advantage_LossAt2p9_ReturnsToSpot()
        {
            var f = Foul(100);
            Assert.AreEqual(AdvantageResult.None, AdvantageQueue.Evaluate(P, in f, 128, 10, false, false, false));
            Assert.AreEqual(AdvantageResult.Lost, AdvantageQueue.Evaluate(P, in f, 129, 10, true, false, false), "2.9 s 공 상실 → 원래 지점");
        }

        [Test]
        public void Law12_Advantage_Hold3s_Clears()
        {
            var f = Foul(100);
            Assert.AreEqual(AdvantageResult.None, AdvantageQueue.Evaluate(P, in f, 129, 10, false, false, false));
            Assert.AreEqual(AdvantageResult.Cleared, AdvantageQueue.Evaluate(P, in f, 130, 10, false, false, false), "정확히 3.0 s = 해제");
            Assert.AreEqual(AdvantageResult.Lost, AdvantageQueue.Evaluate(P, in f, 110, 10, false, false, true), "창 안 데드볼(아웃) → 원래 반칙");
        }

        [Test]
        public void Law12_Advantage_GoalInWindow()
        {
            var f = Foul(100);
            Assert.AreEqual(AdvantageResult.Goal, AdvantageQueue.Evaluate(P, in f, 120, 10, false, true, false));
        }

        [Test]
        public void Queue_Capacity_And_SecondOffence()
        {
            var q = new AdvantageQueue();
            for (int i = 0; i < AdvantageQueue.Capacity; i++) Assert.IsTrue(q.Add(Foul(i)));
            Assert.IsFalse(q.Add(Foul(9)), "가득 참");
            Assert.AreEqual(0, q.IndexOfOffender(114), "같은 반칙한 선수 찾기 (FR-42)");
            q.RemoveAt(0);
            Assert.AreEqual(AdvantageQueue.Capacity - 1, q.Count);
            Assert.AreEqual(1, q[0].Tick);
        }
    }
}
