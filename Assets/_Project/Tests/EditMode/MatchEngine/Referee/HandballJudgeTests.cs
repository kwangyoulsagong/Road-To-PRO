using NUnit.Framework;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-12 일부 (FR-50·51, IFAB Law 12): 핸드볼 진짜 판정·면제·공격 측 우연 핸드볼.</summary>
    public class HandballJudgeTests
    {
        static ContactDetail Hand(float sil, float dist, float react, bool self = false)
        {
            var c = Contact(0.3f, kind: ContactKind.Arm);
            c.HasHand = true; c.HandSilhouette = sil; c.HandBallDist = dist; c.HandReactTime = react; c.HandSelfDeflect = self;
            return c;
        }

        [Test]
        public void Law12_Handball_Table()
        {
            var big = Hand(0.9f, 6f, 0.5f);
            Assert.IsTrue(HandballJudge.IsOffence(P, in big), "팔이 몸을 크게 + 반응 시간 충분 → 반칙");
            var natural = Hand(0.2f, 6f, 0.5f);
            Assert.IsFalse(HandballJudge.IsOffence(P, in natural), "자연스러운 팔 → 아님");
            var self = Hand(0.9f, 6f, 0.5f, self: true);
            Assert.IsFalse(HandballJudge.IsOffence(P, in self), "자기 몸 굴절 면제");
            var close = Hand(0.9f, 0.3f, 0.05f);
            Assert.IsTrue(HandballJudge.Exempt(P, in close), "근거리 + 반응 부족 면제");
            var noHand = Contact(0.5f);
            Assert.AreEqual(0f, HandballJudge.Score(P, in noHand));
        }

        [Test]
        public void Law12_AttackAccidentalBeforeGoal_Disallow()
        {
            var accidental = Hand(0.1f, 0.3f, 0.05f);
            Assert.IsTrue(HandballJudge.Exempt(P, in accidental), "수비 측이면 면제될 우연 핸드볼");
            Assert.IsTrue(HandballJudge.DisallowsAttackGoal(in accidental), "공격 측 득점 직전이면 취소");
        }
    }
}
