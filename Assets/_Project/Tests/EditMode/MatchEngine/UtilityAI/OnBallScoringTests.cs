using NUnit.Framework;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/072 FR-10~13 점수 (AC-02·08), 지시 Directness·Risk·Tempo 연결.</summary>
    public class OnBallScoringTests
    {
        static OnBallCandidate Cand(OnBallItem item, float p, float v, float lossXt, float chem = 0f)
        {
            var c = new OnBallCandidate { Item = item, P = p, V = v, LossXt = lossXt, Chem = chem, Target = 7, Defender = -1 };
            if (OnBallItems.Primary(item, out var k)) c.Cons.Add(k);
            return c;
        }

        [Test]
        public void Multiplier_AppliesToGainOnly_AC02()
        {
            var w = new DecisionWorld();
            var sc = new OnBallScorer(DecisionParams.Defaults, new FixedStyle(Consideration.Pass, 2.376f));
            // 이득 = 0.5 × 0.2 = 0.10, 손실 = (1 − 0.5) × 0.04 × Risk 가중 1.0 = 0.02
            var c = Cand(OnBallItem.ShortPass, 0.5f, 0.2f, 0.04f);
            sc.Score(w, 6, false, ref c);
            Assert.AreEqual(0.2376f - 0.02f, c.Utility, 1e-6f);
            Assert.AreEqual(0.02f, c.Loss, 1e-7f, "손실 항에는 배수가 곱해지지 않는다");
        }

        [Test]
        public void HigherLossPointXt_LowerScore_AC02()
        {
            var w = new DecisionWorld();
            var sc = new OnBallScorer(DecisionParams.Defaults, new FixedStyle(Consideration.Pass, 1f));
            var a = Cand(OnBallItem.ShortPass, 0.7f, 0.05f, 0.02f);
            var b = Cand(OnBallItem.ShortPass, 0.7f, 0.05f, 0.20f);
            sc.Score(w, 6, false, ref a);
            sc.Score(w, 6, false, ref b);
            Assert.Less(b.Utility, a.Utility);
        }

        [Test]
        public void Chemistry_PassOnly_1p15_AC08()
        {
            var w = new DecisionWorld();
            var sc = new OnBallScorer(DecisionParams.Defaults, new FixedStyle(Consideration.Pass, 1f));
            var hi = Cand(OnBallItem.ProgPass, 0.6f, 0.08f, 0.05f, 1f);
            var lo = Cand(OnBallItem.ProgPass, 0.6f, 0.08f, 0.05f, 0f);
            sc.Score(w, 6, false, ref hi);
            sc.Score(w, 6, false, ref lo);
            Assert.AreEqual(1.15f, hi.Gain / lo.Gain, 1e-6f);
            foreach (var item in new[] { OnBallItem.Dribble, OnBallItem.Shoot })
            {
                var x = Cand(item, 0.6f, 0.08f, 0.05f, 1f);
                var y = Cand(item, 0.6f, 0.08f, 0.05f, 0f);
                sc.Score(w, 6, false, ref x);
                sc.Score(w, 6, false, ref y);
                Assert.AreEqual(y.Utility, x.Utility, 0f, item + "은 케미 영향 없음");
            }
        }

        [Test]
        public void Directness_Beta_RiskWeight_Mapping_FR11()
        {
            var p = DecisionParams.Defaults;
            var sc = new OnBallScorer(p, new FixedStyle(Consideration.Pass, 1f));
            Assert.AreEqual(1f, sc.Beta(0.5f), 1e-6f, "중립 지시 = α = β = 1 (FR-10)");
            Assert.AreEqual(p.OnBallBetaMin, sc.Beta(1f), 1e-6f);
            Assert.AreEqual(1f, sc.RiskWeight(0.5f), 1e-6f);
            Assert.Greater(sc.RiskWeight(0f), sc.RiskWeight(1f), "모험 지시가 낮을수록 손실을 무겁게");
            // 직선 지시가 높으면 같은 P에서 큰 V의 상대 이득이 커진다(작은 V가 β < 1로 함께 커져도 음수 V는 더 음수)
            Assert.Less(OnBallScorer.GainOf(0.8f, -0.05f, 1f, 0.7f), OnBallScorer.GainOf(0.8f, -0.05f, 1f, 1f));
            Assert.AreEqual(-0.04f, OnBallScorer.GainOf(0.8f, -0.05f, 1f, 1f), 1e-6f, "ΔxT < 0이면 sign(ΔxT)·|ΔxT|^β");
        }

        [Test]
        public void Tempo_OneTouchBonus_PassOnly_FR11()
        {
            var w = new DecisionWorld();
            var sc = new OnBallScorer(DecisionParams.Defaults, new FixedStyle(Consideration.Pass, 1f));
            w.Tempo[0] = 1f;
            var a = Cand(OnBallItem.ShortPass, 0.8f, 0.05f, 0f);
            var b = Cand(OnBallItem.ShortPass, 0.8f, 0.05f, 0f);
            sc.Score(w, 6, true, ref a);
            sc.Score(w, 6, false, ref b);
            Assert.Greater(a.Gain, b.Gain);
            w.Tempo[0] = 0.5f;
            sc.Score(w, 6, true, ref a);
            Assert.AreEqual(b.Gain, a.Gain, 1e-7f, "중립 템포면 보너스 0");
        }

        [Test]
        public void Suppressed_FlagAndFullComplianceUtility_FR32()
        {
            var w = new DecisionWorld();
            var src = new StandInStyleSource(22, DecisionParams.Defaults.TendencyBase);
            src.SetPlayerInstructionWeight(6, Consideration.Cross, 0.3f);
            src.SetCompliance(6, 0.5f);
            var sc = new OnBallScorer(DecisionParams.Defaults, src);
            var c = Cand(OnBallItem.Cross, 0.5f, 0.1f, 0.01f);
            sc.Score(w, 6, false, ref c);
            Assert.IsTrue(c.Suppressed);
            Assert.AreEqual(Consideration.Cross, c.SuppressedKind);
            Assert.Less(OnBallScorer.FullComplianceUtility(in c), c.Utility, "준수율 1이면 크로스 점수가 더 낮다");
        }
    }
}
