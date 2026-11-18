using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/081 AC-10: 측면 소유자의 박스 지점 크로스 후보 (FR-23).</summary>
    public class CrossTargetsTests
    {
        const int N = 1000;

        /// <summary>측면 깊은 곳(92·60) 소유자 6, 동료는 박스 밖(86·30)의 9만. 상대는 기본 대형.</summary>
        static OnBallFixture WideNoMateInBox(PlayStyle style = null)
        {
            var f = new OnBallFixture();
            f.OnlyOwnerOf(6);
            f.Place(6, new Vec2(92f, 60f));
            f.Place(9, new Vec2(86f, 30f));
            f.GiveBall(6);
            if (style != null) f.Style.SetStyle(6, style);
            return f;
        }

        static int CrossesTo(OnBallFixture f, int target, out OnBallCandidate last)
        {
            var c = f.Decider.Candidates;
            var n = 0;
            last = default;
            for (int i = 0; i < c.Count; i++)
                if (c[i].Item == OnBallItem.Cross && (target < 0 || c[i].Target == target)) { n++; last = c[i]; }
            return n;
        }

        [Test]
        public void CrossTargets_WideNoMateInBox_CandidateWithTtiReceiver()
        {
            var f = WideNoMateInBox();
            f.Place(10, new Vec2(80f, 34f)); // 스폿(94·34)에서 9보다 먼 동료
            f.Generate(6);
            Assert.GreaterOrEqual(CrossesTo(f, 9, out var c), 1, "박스 밖 동료라도 지점에 먼저 닿으면 크로스 후보");
            Assert.IsTrue(c.Pass.Lofted, "크로스는 뜬 패스");
            var spot = CrossTargets.Point(f.P, f.W, 0, f.W.Pos[6], 2);
            var cands = f.Decider.Candidates;
            for (int i = 0; i < cands.Count; i++)
                if (cands[i].Item == OnBallItem.Cross && (cands[i].Point - spot).Length() < 0.01f)
                    Assert.AreEqual(9, cands[i].Target, "스폿 받는 선수 = TTI 최소 동료");
        }

        [Test]
        public void CrossTargets_NoReachableMate_NoCandidate()
        {
            var f = new OnBallFixture();
            f.OnlyOwnerOf(6);
            f.Place(6, new Vec2(92f, 60f));
            f.GiveBall(6);
            f.Generate(6);
            Assert.AreEqual(0, CrossesTo(f, -1, out _), "동료가 없으면 크로스 없음");

            f.Place(9, new Vec2(30f, 34f)); // 60 m 넘게 떨어진 동료 — 비행 시간 + 여유 안에 못 닿음
            f.Generate(6);
            Assert.AreEqual(0, CrossesTo(f, -1, out _), "빈 지점으로 차지 않는다");
        }

        [Test]
        public void CrossTargets_DedupSameReceiver()
        {
            var f = new OnBallFixture();
            f.OnlyOwnerOf(6);
            f.Place(6, new Vec2(92f, 60f));
            f.Place(9, new Vec2(99f, 38f)); // 박스 안(니어 포스트 근처) — 072 경로와 지점 경로가 같은 받는 선수
            f.GiveBall(6);
            f.Generate(6);
            Assert.AreEqual(1, CrossesTo(f, 9, out _), "같은 받는 선수의 크로스는 1개");
        }

        [Test]
        public void CrossTargets_Points_NearFarSpotBySide()
        {
            var f = new OnBallFixture();
            var cy = f.W.PitchW * 0.5f;
            var near = CrossTargets.Point(f.P, f.W, 0, new Vec2(92f, 60f), 0);
            var far = CrossTargets.Point(f.P, f.W, 0, new Vec2(92f, 60f), 1);
            var spot = CrossTargets.Point(f.P, f.W, 0, new Vec2(92f, 60f), 2);
            Assert.Greater(near.Y, cy);
            Assert.Less(far.Y, cy);
            Assert.AreEqual(cy, spot.Y, 1e-4f);
            Assert.AreEqual(f.W.PitchL - f.P.OnBallCrossNearX, near.X, 1e-4f);
            Assert.AreEqual(f.W.PitchL - f.P.OnBallCrossSpotX, spot.X, 1e-4f);
            var nearAway = CrossTargets.Point(f.P, f.W, 1, new Vec2(13f, 8f), 0); // 팀 1(왼쪽 공격), 아래쪽 측면
            Assert.AreEqual(f.P.OnBallCrossNearX, nearAway.X, 1e-4f);
            Assert.Less(nearAway.Y, cy);
        }

        [Test]
        public void Twin_CrossMachine_MoreCrosses_BoxPoints()
        {
            // 007 T21: 동료 패스가 궤적 재평가로 내려가 이 장면의 크로스 기본 비율이 ~90 %(천장 근처)라 1,000회로는 검정력이 모자람(898 → 918, Z 1.55) → 4,000회 (방향 그대로)
            const int N = 4000;
            var a = WideNoMateInBox().Distribution(6, 181, N);
            var b = WideNoMateInBox(PlayStyle.Neutral().WithHabit(Habit.CrossMachine, 1)).Distribution(6, 181, N);
            TestContext.WriteLine($"크로스 A {a[(int)OnBallItem.Cross]} B {b[(int)OnBallItem.Cross]}");
            Assert.Greater(a[(int)OnBallItem.Cross], 0);
            Assert.Greater(Stat.Z(b[(int)OnBallItem.Cross], N, a[(int)OnBallItem.Cross], N), Stat.Z01);
        }
    }
}
