using NUnit.Framework;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-06·14 판정기 부분 (FR-70~74, P6·P7·P9). 실경기 골 취소·상태 불변은 <see cref="RefereeStageTests"/>.</summary>
    public class VarReviewTests
    {
        [Test]
        public void ClearError_RedUpgrade()
        {
            // 진짜 퇴장(0.95)인데 경고만(인지 0.62) → 명백한 오류, 주관 → OFR
            Assert.IsTrue(VarReview.ClearError(P, 0.62f, 0.95f, EventCodes.DecisionYellow, EventCodes.DecisionRed));
            var rng = new SeedRng(5);
            var over = 0;
            for (int i = 0; i < 1000; i++) if (VarReview.Overturns(P, false, true, rng)) over++;
            Assert.That(over / 1000.0, Is.InRange(0.80, 0.90), "OFR 번복 0.85");
        }

        [Test]
        public void MinorDiff_NoIntervention()
        {
            Assert.IsFalse(VarReview.ClearError(P, 0.80f, 0.90f, EventCodes.DecisionYellow, EventCodes.DecisionRed), "차이 0.10 < 0.25");
            Assert.IsFalse(VarReview.ClearError(P, 0.30f, 0.70f, EventCodes.DecisionYellow, EventCodes.DecisionYellow), "단계 같음");
            var rng = new SeedRng(1);
            var before = rng.SaveState();
            Assert.IsFalse(VarReview.Overturns(P, false, false, rng));
            Assert.AreEqual(before.S0, rng.SaveState().S0, "오류 아니면 RNG 소비 없음");
        }

        [Test]
        public void NotReviewable_NoIntervention()
        {
            Assert.IsFalse(VarReview.Reviewable(0), "일반 파울 = 사안 아님");
            Assert.IsTrue(VarReview.Reviewable(EventCodes.VarGoalIncident));
            Assert.IsTrue(VarReview.Reviewable(EventCodes.VarPenaltyIncident));
            Assert.IsTrue(VarReview.Reviewable(EventCodes.VarRedIncident));
            Assert.IsTrue(VarReview.Reviewable(EventCodes.VarIdentityIncident));
        }

        [Test]
        public void Factual_AlwaysCorrected_CheckDuration()
        {
            var rng = new SeedRng(3);
            Assert.IsTrue(VarReview.Overturns(P, true, true, rng), "사실 판정 오류 → 항상 정정");
            for (int i = 0; i < 200; i++)
            {
                var t = VarReview.CheckTicks(P, true, true, rng);
                Assert.That(t, Is.InRange(600L, 1200L), "근소 오프사이드 60~120 s");
                var f = VarReview.CheckTicks(P, true, false, rng);
                Assert.That(f, Is.InRange(300L, 600L), "사실 30~60 s");
                var o = VarReview.CheckTicks(P, false, false, rng);
                Assert.That(o, Is.InRange(600L, 1200L), "주관 60~120 s");
            }
            var a = new SeedRng(9); var b = new SeedRng(9);
            Assert.AreEqual(VarReview.CheckTicks(P, true, true, a), VarReview.CheckTicks(P, true, true, b), "같은 시드 = 같은 체크 시간");
        }
    }
}
