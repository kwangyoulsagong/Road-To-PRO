using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/072 T02: 070·071·012·033 대체 입력의 방향(단조성)·오프사이드 위험(FR-15)·개인 지시·준수율 1 배수.</summary>
    public class OnBallEvalTests
    {
        static DecisionFixture Fx() => new DecisionFixture();

        [Test]
        public void Dribble_HigherDribblingOrLowerTackling_MoreSuccess()
        {
            var f = Fx();
            var e = new StandInOnBallEval(f.P);
            f.W.Tech[6] = TechnicalAttributes.Average.WithOnBall(dribbling: 10f);
            var low = e.DribbleSuccess(f.W, 6, 20, 0.3f);
            f.W.Tech[6] = TechnicalAttributes.Average.WithOnBall(dribbling: 90f);
            var high = e.DribbleSuccess(f.W, 6, 20, 0.3f);
            Assert.Greater(high, low);
            f.W.Tech[20] = TechnicalAttributes.Average.WithOnBall(tackling: 95f);
            Assert.Less(e.DribbleSuccess(f.W, 6, 20, 0.3f), high, "수비 태클이 높으면 낮다");
            Assert.Less(e.DribbleSuccess(f.W, 6, 20, 0.9f), e.DribbleSuccess(f.W, 6, 20, 0.1f), "압박이 높으면 낮다");
        }

        [Test]
        public void ShotXg_CloserCentralSkilled_Higher()
        {
            var f = Fx();
            var e = new StandInOnBallEval(f.P);
            var near = e.ShotXg(f.W, 6, new Vec2(94f, 34f), 0f);
            var far = e.ShotXg(f.W, 6, new Vec2(80f, 34f), 0f);
            var wide = e.ShotXg(f.W, 6, new Vec2(100f, 10f), 0f);
            Assert.Greater(near, far);
            Assert.Greater(near, wide);
            Assert.That(near, Is.InRange(0.1f, 0.5f), "11 m 정면 대체 xG");
            f.W.Tech[6] = TechnicalAttributes.Average.WithOnBall(finishing: 95f);
            Assert.Greater(e.ShotXg(f.W, 6, new Vec2(94f, 34f), 0f), near);
            f.W.Tech[6] = TechnicalAttributes.Average.WithOnBall(longShots: 95f);
            Assert.Greater(e.ShotXg(f.W, 6, new Vec2(80f, 34f), 0f), far, "20 m 밖은 중거리 능력치");
            Assert.Less(e.ShotXg(f.W, 6, new Vec2(94f, 34f), 1f), near, "압박이 높으면 낮다");
        }

        [Test]
        public void Cross_CrossingScales_LoftedXPass()
        {
            var f = Fx();
            var e = new StandInOnBallEval(f.P);
            f.W.Tech[6] = TechnicalAttributes.Average.WithOnBall(crossing: 5f);
            var low = e.CrossSuccess(f.W, 6, 0.5f);
            f.W.Tech[6] = TechnicalAttributes.Average.WithOnBall(crossing: 95f);
            Assert.Greater(e.CrossSuccess(f.W, 6, 0.5f), low);
            Assert.LessOrEqual(e.CrossSuccess(f.W, 6, 1f), 1f);
        }

        [Test]
        public void FoulValue_InBoxIsPk_OutsideIsFreeKick()
        {
            var f = Fx();
            var e = new StandInOnBallEval(f.P);
            f.W.Pos[6] = new Vec2(96f, 34f);
            Assert.AreEqual(f.P.OnBallDivePkValue, e.FoulValue(f.W, 6));
            f.W.Pos[6] = new Vec2(70f, 34f);
            Assert.AreEqual(f.P.OnBallDiveFkValue, e.FoulValue(f.W, 6));
        }

        [Test]
        public void Offside_ReceiverBeyondLine_RiskScaledByVisionDecisions_FR15()
        {
            var f = Fx();
            var e = new StandInOnBallEval(f.P);
            Assert.AreEqual(1f, e.OffsideFactor(f.W, 6, 9), "온사이드 동료는 위험 없음");
            f.W.Pos[9] = new Vec2(85f, 30f); // 라인 80 너머
            f.W.Mind[6] = DecisionAttributes.Average.With(vision: 5f, decisions: 5f);
            var lowSkill = e.OffsideFactor(f.W, 6, 9);
            f.W.Mind[6] = DecisionAttributes.Average.With(vision: 95f, decisions: 95f);
            var highSkill = e.OffsideFactor(f.W, 6, 9);
            Assert.Less(lowSkill, 1f);
            Assert.Less(highSkill, lowSkill, "시야·판단력이 높으면 오프사이드 패스를 더 피한다(성공 확률 배수가 더 작다)");
            Assert.Greater(highSkill, 0f - 1e-6f);
        }

        [Test]
        public void Chemistry_SymmetricDefaultZero_RejectsOutOfRange()
        {
            var c = new StandInChemistry();
            Assert.AreEqual(0f, c.Chemistry(6, 9));
            c.Set(6, 9, 0.8f);
            Assert.AreEqual(0.8f, c.Chemistry(9, 6));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => c.Set(6, 9, 1.5f));
        }

        [Test]
        public void PlayerInstruction_MultipliesTeam_FullComplianceIgnoresCompliance()
        {
            var src = new StandInStyleSource(22, DecisionParams.Defaults.TendencyBase);
            src.SetInstructionWeight(0, Consideration.Cross, 0.8f);
            src.SetPlayerInstructionWeight(7, Consideration.Cross, 0.5f);
            Assert.AreEqual(0.4f, src.InstructionWeight(7, Consideration.Cross), 1e-6f);
            Assert.AreEqual(0.8f, src.InstructionWeight(8, Consideration.Cross), 1e-6f);
            src.SetCompliance(7, 0.2f);
            var m = src.Multiplier(7, Consideration.Cross);
            var full = src.MultiplierFullCompliance(7, Consideration.Cross);
            Assert.AreEqual(1f + 0.2f * (0.4f - 1f), m, 1e-5f, "실효 지시 = lerp(1, w, 준수율)");
            Assert.AreEqual(0.4f, full, 1e-5f, "준수율 1이면 원시 가중치 그대로");
        }

        [Test]
        public void TechnicalAttributes_OnBallDefaults50_RangeChecked()
        {
            var t = new TechnicalAttributes(60f, 60f, 60f);
            Assert.AreEqual(50f, t.Dribbling);
            Assert.AreEqual(50f, t.Tackling);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TechnicalAttributes(50f, 50f, 50f, Foot.Right, 3, dribbling: 120f));
            Assert.AreEqual(t.Passing, t.WithOnBall(finishing: 70f).Passing);
            Assert.AreEqual(70f, t.WithOnBall(finishing: 70f).Finishing);
        }
    }
}
