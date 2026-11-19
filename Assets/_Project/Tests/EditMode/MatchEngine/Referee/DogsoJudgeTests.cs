using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-08 (FR-23~25, IFAB Law 12): DOGSO 4요소·박스 안 완화·어드밴티지 후 골.</summary>
    public class DogsoJudgeTests
    {
        static ContactDetail Chance(float dist, int defenders, float control = 0.9f, bool inBox = false, ContactKind kind = ContactKind.Tackle)
        {
            var c = Contact(0.7f, kind: kind);
            c.DogsoValid = true; c.GoalDist = dist; c.DefendersBetween = defenders; c.ControlLikely = control; c.InBox = inBox;
            return c;
        }

        static int Final(in ContactDetail c, float cos, bool advGoal, RuleSet rules, out int reason) =>
            DogsoJudge.Apply(EventCodes.DecisionFoul, DogsoJudge.Classify(P, in c, cos), c.InBox, DogsoJudge.GenuineChallenge(in c), advGoal, rules, out reason);

        [Test]
        public void Law12_Dogso_Table()
        {
            var rules = RuleSet.Season2026_27;
            var outside = Chance(25f, 0);
            Assert.AreEqual(OpportunityKind.Dogso, DogsoJudge.Classify(P, in outside, 0.95f));
            Assert.AreEqual(EventCodes.DecisionRed, Final(in outside, 0.95f, false, rules, out var r1), "4요소 + 박스 밖 → 퇴장");
            Assert.AreEqual(EventCodes.ReasonDogso, r1);

            var boxChallenge = Chance(12f, 0, inBox: true, kind: ContactKind.Tackle);
            Assert.AreEqual(EventCodes.DecisionYellow, Final(in boxChallenge, 0.95f, false, rules, out _), "박스 안 + 공을 향한 도전 → PK + 경고");

            var boxHold = Chance(12f, 0, inBox: true, kind: ContactKind.Shield);
            Assert.AreEqual(EventCodes.DecisionRed, Final(in boxHold, 0.95f, false, rules, out _), "박스 안 + 잡기(공과 무관) → PK + 퇴장");

            var covered = Chance(25f, 1);
            Assert.AreNotEqual(OpportunityKind.Dogso, DogsoJudge.Classify(P, in covered, 0.95f), "커버 수비수 1명 → DOGSO 아님");
            Assert.AreEqual(OpportunityKind.Spa, DogsoJudge.Classify(P, in covered, 0.95f), "대신 SPA");
            Assert.AreEqual(EventCodes.DecisionYellow, Final(in covered, 0.95f, false, rules, out var r2));
            Assert.AreEqual(EventCodes.ReasonSpa, r2);

            Assert.AreEqual(OpportunityKind.None, DogsoJudge.Classify(P, in outside, 0.3f), "골 반대 방향 → 아님");
            var far = Chance(45f, 0);
            Assert.AreEqual(OpportunityKind.None, DogsoJudge.Classify(P, in far, 0.95f), "45 m → 아님");

            Assert.AreEqual(EventCodes.DecisionFoul, Final(in outside, 0.95f, true, RuleSet.Season2026_27, out _), "어드밴티지 후 골(2026/27) → 경고·퇴장 없음");
            Assert.AreEqual(EventCodes.DecisionRed, Final(in outside, 0.95f, true, RuleSet.Season2025_26, out _), "2025/26 규칙 세트는 적용 안 함");
        }

        [Test]
        public void SeriousFoulPlay_StaysRed_ThroughMitigation()
        {
            var c = Chance(12f, 0, inBox: true);
            var d = DogsoJudge.Apply(EventCodes.DecisionRed, OpportunityKind.Dogso, true, true, false, RuleSet.Default, out var reason);
            Assert.AreEqual(EventCodes.DecisionRed, d, "과도한 힘 퇴장은 완화되지 않는다");
            Assert.AreEqual(EventCodes.ReasonSeriousFoulPlay, reason);
        }

        [Test]
        public void GoalCos_UsesVelocity_ElseFacing()
        {
            var goal = new Vec2(105f, 34f);
            Assert.AreEqual(1f, DogsoJudge.GoalCos(new Vec2(80f, 34f), new Vec2(5f, 0f), new Vec2(-1f, 0f), goal), 1e-4f);
            Assert.AreEqual(-1f, DogsoJudge.GoalCos(new Vec2(80f, 34f), Vec2.Zero, new Vec2(-1f, 0f), goal), 1e-4f, "느리면 몸 방향");
        }
    }
}
