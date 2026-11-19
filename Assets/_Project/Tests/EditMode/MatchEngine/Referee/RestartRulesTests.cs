using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-11·12 (FR-60~66, IFAB Law 10·13~17): 재시작 표.</summary>
    public class RestartRulesTests
    {
        const float L = 105f, W = 68f;

        [Test]
        public void Law15to17_Restart_Table()
        {
            // 팀 0이 +x 공격 (팀 1이 x = L 골을 지킴)
            var t = RestartRules.Out(BallBoundary.TouchlineLow, new Vec2(40f, 0f), 0, 1, L, W);
            Assert.AreEqual((RestartKind.ThrowIn, 1), (t.Kind, t.Team), "터치라인 아웃(공격 마지막 터치) → 수비 팀 스로인");
            var gk = RestartRules.Out(BallBoundary.GoalLineHigh, new Vec2(L, 20f), 0, 1, L, W);
            Assert.AreEqual((RestartKind.GoalKick, 1), (gk.Kind, gk.Team), "골라인 아웃(공격 마지막 터치) → 골킥");
            var ck = RestartRules.Out(BallBoundary.GoalLineHigh, new Vec2(L, 20f), 1, 1, L, W);
            Assert.AreEqual((RestartKind.Corner, 0), (ck.Kind, ck.Team), "골라인 아웃(수비 마지막 터치) → 코너");
            Assert.AreEqual(new Vec2(L - 0.5f, 0.5f), ck.Spot);
            Assert.IsTrue(RestartRules.KeeperHoldViolation(8.01f, P.KeeperHoldMax), "GK 8.01 s → 위반");
            Assert.IsFalse(RestartRules.KeeperHoldViolation(8.0f, P.KeeperHoldMax), "8.0 s → 위반 없음");
            var kh = RestartRules.KeeperHold(RuleSet.Default, 1, new Vec2(100f, 40f), new Vec2(L, 34f), -1, L, W);
            Assert.AreEqual((RestartKind.Corner, 0), (kh.Kind, kh.Team), "8초 초과 → 상대 코너 (2025/26)");
            var pk = RestartRules.Foul(new Vec2(95f, 30f), 0, new Vec2(L, 34f), 1, true, L, W);
            Assert.AreEqual(RestartKind.Penalty, pk.Kind, "박스 안 직접 FK 반칙 → PK");
            Assert.AreEqual(new Vec2(L - 11f, 34f), pk.Spot);
            var fk = RestartRules.Foul(new Vec2(80f, 30f), 0, new Vec2(L, 34f), 1, true, L, W);
            Assert.AreEqual((RestartKind.FreeKick, true), (fk.Kind, fk.Direct), "박스 밖 → 직접 FK");
        }

        [Test]
        public void IndirectInGoalArea_MovedToGoalAreaLine()
        {
            var ifk = RestartRules.Foul(new Vec2(102f, 36f), 0, new Vec2(L, 34f), 1, false, L, W);
            Assert.AreEqual(RestartKind.FreeKick, ifk.Kind, "간접 FK는 박스 안이어도 PK 아님");
            Assert.AreEqual(new Vec2(L - 5.5f, 36f), ifk.Spot, "골 에어리어 선 위로");
            var bp = RestartRules.BackPass(1, new Vec2(103f, 30f), new Vec2(L, 34f), -1, L, W);
            Assert.AreEqual((RestartKind.FreeKick, 0, false), (bp.Kind, bp.Team, bp.Direct), "백패스 → 상대 간접 FK");
            Assert.AreEqual(L - 5.5f, bp.Spot.X, 1e-4f);
        }

        [Test]
        public void DropBall_And_PenaltyRetake()
        {
            var inBox = RestartRules.DropBall(new Vec2(100f, 34f), 0, 1, L, W);
            Assert.AreEqual(1, inBox.Team, "박스 안 → 그 박스 GK 팀");
            var mid = RestartRules.DropBall(new Vec2(50f, 34f), 0, 1, L, W);
            Assert.AreEqual(0, mid.Team, "그 밖 → 마지막 터치 팀");
            Assert.IsTrue(RestartRules.PenaltyRetake(true, false, false), "GK 조기 이탈 + 실패 → 재차기");
            Assert.IsFalse(RestartRules.PenaltyRetake(true, false, true), "골이면 그대로");
        }

        [Test]
        public void Law10_GoalLine_99_100()
        {
            const float r = 0.11f;
            Assert.IsFalse(RestartRules.WhollyOver(L + r * 0.98f, r, L, 1), "99 % → 득점 아님");
            Assert.IsTrue(RestartRules.WhollyOver(L + r, r, L, 1), "100 % → 득점");
            Assert.IsFalse(RestartRules.WhollyOver(L, r, L, 1), "골라인 위 정확히 → 아님");
        }
    }
}
