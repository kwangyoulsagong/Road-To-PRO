using NUnit.Framework;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.Tests.EditMode.Presentation.Highlight;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/082 FR-03 ①·③ · AC-06: 헤더 접촉은 머리 도달 높이(키 × headK + 점프) 아래, 승자–공 수평 거리 ≤ headRadius에서만.
    /// 경합 점수(071 FR-03·04)는 팔 reach 그대로.
    /// </summary>
    public class AerialHeadReachTests
    {
        static ActionParams P => ActionTestKit.P;

        [Test]
        public void HeadReach_Formula()
        {
            Assert.AreEqual(0.93f, P.AerialHeadK, 1e-6f);
            Assert.AreEqual(1.0f, P.AerialHeadRadius, 1e-6f);
            Assert.AreEqual(1.80f * 0.93f + 0.5f, AerialModel.HeadReach(P, 180f, 0.5f), 1e-5f);
            Assert.AreEqual(1.50f * 0.93f + 0.35f, AerialModel.HeadReach(P, 150f, 0.35f), 1e-5f);
            Assert.Less(AerialModel.HeadReach(P, 180f, 0.5f), AerialModel.Reach(P, 180f, 0.5f, false), "머리는 팔 든 높이보다 낮다");
        }

        [Test]
        public void HeadLow_Formula()
        {
            Assert.AreEqual(0.2f, P.AerialHeadLow, 1e-6f);
            Assert.AreEqual(1.80f * 0.93f - 0.2f, AerialModel.HeadLow(P, 180f), 1e-5f);
        }

        [Test]
        public void KeeperHands_UsesArmReach()
        {
            // GK 손 처리는 011 몫 — 손 reach(팔 + GK 보너스)는 그대로, 머리 기준을 쓰지 않는다
            var hands = AerialModel.Reach(P, 190f, 0.5f, true);
            Assert.AreEqual(1.90f * P.AerialHeightK + 0.5f + P.AerialGkBonus, hands, 1e-5f);
            Assert.AreEqual(hands, AerialModel.ContactReach(P, 190f, 0.5f, true), 1e-6f);
            Assert.AreEqual(AerialModel.HeadReach(P, 190f, 0.5f), AerialModel.ContactReach(P, 190f, 0.5f, false), 1e-6f);
        }

        [Test]
        public void WinProbability_Unchanged()
        {
            // 경합 점수는 071 팔 reach(키×1.33 + 점프) 기준 그대로 (082 제외 항목: 승패 확률 변경 없음)
            var reach = AerialModel.Reach(P, 185f, 0.55f, false);
            Assert.AreEqual(1.85f * 1.33f + 0.55f, reach, 1e-5f);
        }

        /// <summary>AC-06: 실경기 고정 시드 묶음의 모든 헤더 — 접촉 높이 ≤ 머리 도달(+ 1 서브스텝 낙하 여유), 수평 ≤ headRadius.</summary>
        [Test]
        public void ContactHeight_NeverAboveHeadReach_HorizontalWithinRadius()
        {
            long headers = 0;
            float overHead = float.MinValue, horizontal = 0f, underLow = float.MinValue;
            foreach (var seed in new ulong[] { 20261247UL, 20261220UL, 20261142UL })
            {
                var sim = LiveContactAudit.NewSim(seed);
                sim.RunToEnd();
                var a = sim.Context.Actions.Aerials;
                headers += a.Headers;
                if (a.Headers == 0) continue;
                if (a.MaxContactOverHead > overHead) overHead = a.MaxContactOverHead;
                if (a.MaxContactHorizontal > horizontal) horizontal = a.MaxContactHorizontal;
                if (a.MaxContactUnderLow > underLow) underLow = a.MaxContactUnderLow;
            }
            Assert.Greater(headers, 0, "헤더가 있어야 한다");
            TestContext.WriteLine($"headers {headers} maxOverHead {overHead:0.000} m maxHorizontal {horizontal:0.000} m maxUnderLow {underLow:0.000} m");
            Assert.LessOrEqual(underLow, 0f, "공 높이 ≥ 머리 최저 (082 T05a)");
            Assert.LessOrEqual(overHead, 0.05f, "접촉 높이 − 머리 도달 (1 서브스텝 낙하 여유 5 cm)");
            Assert.LessOrEqual(horizontal, P.AerialHeadRadius + 1e-4f, "승자–공 수평 거리");
        }
    }
}
