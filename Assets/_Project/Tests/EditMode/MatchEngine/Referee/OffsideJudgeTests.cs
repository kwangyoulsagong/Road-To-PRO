using NUnit.Framework;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-01·02 (FR-01~03, IFAB Law 11): 위치·면제 표.</summary>
    public class OffsideJudgeTests
    {
        const float L = 105f;

        static OffsidePosition At(float attackerX, float defLine, float ballX = 60f)
        {
            var x = Line(defLine);
            var line = OffsideJudge.SecondLastDefenderX(x, 1, 1, L, P.OffsideBodyReach);
            return OffsideJudge.Position(attackerX, ballX, line, 1, L, P.OffsideBodyReach);
        }

        [Test]
        public void Law11_Position_Table()
        {
            // 같은 몸 기준점(둘 다 +0.25 m) → 위치 차 = 몸 앞쪽 끝 차
            Assert.IsTrue(At(80.01f, 80f).IsOffside, "+0.01 m → 오프사이드 위치");
            Assert.AreEqual(0.01f, At(80.01f, 80f).D, 1e-4f);
            Assert.IsFalse(At(80f, 80f).IsOffside, "0 m(같은 선상) → 온사이드");
            Assert.IsFalse(At(79.99f, 80f).IsOffside, "−0.01 m → 온사이드");
            // 하프라인 정확히 위: 몸 앞쪽 끝 = 52.5 → 자기 진영
            var half = At(52.5f - P.OffsideBodyReach, 40f, 30f);
            Assert.IsFalse(half.InOpponentHalf, "하프라인 위 = 자기 진영");
            Assert.IsFalse(half.IsOffside);
            Assert.IsFalse(At(40f, 30f, 20f).IsOffside, "자기 진영 → 온사이드");
            // 공보다 뒤 → 온사이드
            Assert.IsFalse(At(80.5f, 80f, 82f).IsOffside, "공보다 뒤");
        }

        [Test]
        public void Law11_ArmOnlyAhead_Onside()
        {
            // 팔은 판정 부위가 아니다: 기준점 함수는 팔 오프셋을 받지 않는다 → 몸이 같은 선상이면 팔이 앞서도 온사이드
            var bodyEqual = At(80f, 80f);
            Assert.IsFalse(bodyEqual.IsOffside, "팔만 앞 → 온사이드");
            Assert.AreEqual(80f + P.OffsideBodyReach, OffsideJudge.FrontX(80f, 1, P.OffsideBodyReach), 1e-5f);
        }

        [Test]
        public void Law11_SecondLast_GkForward_And_Inactive()
        {
            // GK 전진(x 60), 수비수 80·75 → 뒤에서 두 번째 = 75
            var x = EmptyX();
            x[11] = 60f; x[12] = 80f; x[13] = 75f;
            Assert.AreEqual(75f + P.OffsideBodyReach, OffsideJudge.SecondLastDefenderX(x, 1, 1, L, P.OffsideBodyReach), 1e-4f);
            // 활성 수비수 1명 → 골라인
            var one = EmptyX(); one[11] = 100f;
            Assert.AreEqual(L, OffsideJudge.SecondLastDefenderX(one, 1, 1, L, P.OffsideBodyReach));
            // 골라인 밖 수비수 → 골라인 위로 (P12)
            var outside = EmptyX(); outside[11] = 107f; outside[12] = 106f;
            Assert.AreEqual(L, OffsideJudge.SecondLastDefenderX(outside, 1, 1, L, P.OffsideBodyReach), 1e-4f);
            // 반대 방향 공격(−x)
            var rev = EmptyX(); rev[11] = 5f; rev[12] = 20f; rev[13] = 30f;
            Assert.AreEqual(20f - P.OffsideBodyReach, OffsideJudge.SecondLastDefenderX(rev, 1, -1, L, P.OffsideBodyReach), 1e-4f);
        }

        [Test]
        public void Law11_Exempt_ThrowGoalKickCorner()
        {
            Assert.IsTrue(OffsideJudge.ExemptRestart(RestartKind.ThrowIn));
            Assert.IsTrue(OffsideJudge.ExemptRestart(RestartKind.GoalKick));
            Assert.IsTrue(OffsideJudge.ExemptRestart(RestartKind.Corner));
            Assert.IsFalse(OffsideJudge.ExemptRestart(RestartKind.FreeKick));
            Assert.IsFalse(OffsideJudge.ExemptRestart(RestartKind.Kickoff));
            // 면제 스냅샷: 오프사이드 위치 선수가 받아도 반칙 아님
            var t = new OffsideTracker();
            var x = Line(80f); x[9] = 85f; x[5] = 60f;
            t.Snapshot(P, 0, 0, 5, 60f, x, Zeros(), 1, L, exempt: true);
            Assert.IsFalse(t.TryTouch(9, OffsideTouch.Deliberate, out _));
        }

        [Test]
        public void Law11_GkThrow_JudgedAtRelease()
        {
            // GK 스로: 손을 떠나는 순간(= 스냅샷 시점) 위치로 판정. 스냅샷 뒤 이동은 무관.
            var t = new OffsideTracker();
            var x = Line(80f); x[0] = 30f; x[9] = 79f; // 놓는 순간 온사이드
            t.Snapshot(P, 0, 0, 0, 30f, x, Zeros(), 1, L, false);
            Assert.IsTrue(t.IsCandidate(9), "온사이드 근처 = 후보(부심 오판 가능)");
            Assert.LessOrEqual(t.MarginOf(9), 0f, "놓는 순간 d ≤ 0 → 실제 판정 온사이드");
        }
    }
}
