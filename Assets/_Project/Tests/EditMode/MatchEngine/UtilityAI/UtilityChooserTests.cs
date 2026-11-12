using System;
using NUnit.Framework;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 FR-20·42·45·92·94 공통 선택기 (AC-05·09, 경계: 음수·τ→0·동점).</summary>
    public class UtilityChooserTests
    {
        static DecisionParams P => DecisionParams.Defaults;

        static float S(int display) => DecisionAttributes.N(KinematicAttributes.InternalFromDisplay(display));

        static int Share(float tau, ulong seed, int n = 10000)
        {
            var ch = new UtilityChooser();
            var rng = new SeedRng(seed);
            var u = new[] { 1.0f, 0.9f };
            int hits = 0;
            for (int i = 0; i < n; i++)
            {
                ch.Evaluate(u, default, default, default, 0f, P.NormEps);
                if (ch.Pick(tau, rng) == 0) hits++;
            }
            return hits;
        }

        [Test]
        public void Softmax_Decisions20_BestShare88pct_AC05()
        {
            var tau = UtilityChooser.Temperature(P.TauMax, P.TauMin, S(20), 0f);
            Assert.AreEqual(0.05f, tau, 1e-6f);
            var expected = 1.0 / (1.0 + Math.Exp(-0.1 / tau));
            Assert.AreEqual(expected, Share(tau, 501UL) / 10000.0, 0.03, $"기대 {expected:P1}");
        }

        [Test]
        public void Softmax_Decisions1_BestShare57pct_AC05()
        {
            var tau = UtilityChooser.Temperature(P.TauMax, P.TauMin, S(1), 0f);
            Assert.AreEqual(0.35f, tau, 1e-6f);
            var expected = 1.0 / (1.0 + Math.Exp(-0.1 / tau));
            Assert.AreEqual(expected, Share(tau, 502UL) / 10000.0, 0.03, $"기대 {expected:P1}");
        }

        [Test]
        public void ShouldReroll_10pctNo_20pctYes_AC09()
        {
            Assert.IsFalse(UtilityChooser.ShouldReroll(1.0f, 1.1f, P.HoldThreshold, P.NormEps));
            Assert.IsTrue(UtilityChooser.ShouldReroll(1.0f, 1.2f, P.HoldThreshold, P.NormEps));
            Assert.IsFalse(UtilityChooser.ShouldReroll(0.5f, 0.55f, P.HoldThreshold, P.NormEps), "비율 기준");
            Assert.IsTrue(UtilityChooser.ShouldReroll(-0.5f, -0.4f, P.HoldThreshold, P.NormEps), "음수 현재 점수도 |현재| 기준");
        }

        [Test]
        public void Normalize_MaxScaled()
        {
            var ch = new UtilityChooser();
            ch.Evaluate(new[] { 0.4f, 0.2f, 0.1f }, default, default, default, 0f, P.NormEps);
            Assert.AreEqual(1f, ch.Score(0), 1e-6f);
            Assert.AreEqual(0.5f, ch.Score(1), 1e-6f);
            Assert.AreEqual(0.25f, ch.Score(2), 1e-6f);
        }

        [Test]
        public void AllNonPositive_NoNaN()
        {
            var ch = new UtilityChooser();
            ch.Evaluate(new[] { -1f, -2f, 0f }, default, default, default, 0f, P.NormEps);
            for (int i = 0; i < 3; i++) Assert.IsFalse(float.IsNaN(ch.Score(i)) || float.IsInfinity(ch.Score(i)));
            ch.Evaluate(new[] { 0f, 0f }, default, default, default, 0f, P.NormEps);
            Assert.AreEqual(0f, ch.Score(0));
            var rng = new SeedRng(3UL);
            Assert.DoesNotThrow(() => ch.Pick(0.2f, rng));
        }

        [Test]
        public void TauNearZero_ArgmaxWithoutRng()
        {
            var ch = new UtilityChooser();
            ch.Evaluate(new[] { 0.3f, 0.9f, 0.5f }, default, default, default, 0f, P.NormEps);
            var rng = new SeedRng(4UL);
            var before = rng.SaveState();
            Assert.AreEqual(1, ch.Pick(1e-7f, rng));
            Assert.AreEqual(before, rng.SaveState(), "τ → 0이면 난수를 쓰지 않는다");
        }

        [Test]
        public void Tie_FirstGenerated()
        {
            var ch = new UtilityChooser();
            ch.Evaluate(new[] { 0.5f, 0.5f }, default, default, default, 0f, P.NormEps);
            Assert.AreEqual(0, ch.Best());
            Assert.AreEqual(0, ch.Pick(1e-7f, new SeedRng(1UL)));
        }

        [Test]
        public void IntentBonus_AddedAfterNormalize_NotMultiplied_AC30()
        {
            var ch = new UtilityChooser();
            ch.Evaluate(new[] { 0.8f, 0.4f }, new[] { 0f, 0.5f }, default, default, 0.3f, P.NormEps);
            Assert.AreEqual(1.0f, ch.Score(0), 1e-6f);
            Assert.AreEqual(0.5f + 0.3f * 0.5f, ch.Score(1), 1e-6f);
        }

        [Test]
        public void Opportunistic_FilterOnlyBelowTheta_AC32()
        {
            var ch = new UtilityChooser();
            var match = new[] { 0f, 1f };
            ch.Evaluate(new[] { 1.0f, 0.6f }, match, default, default, 0.3f, P.NormEps); // 0.9 의도 vs 1.0 밖
            Assert.IsTrue(ch.FilterOpportunistic(match, 0.25f));
            Assert.IsFalse(ch.IsIn(0), "1.0 < 0.9 + 0.25 → 제외");
            ch.Evaluate(new[] { 1.0f, 0.3f }, match, default, default, 0.3f, P.NormEps); // 0.6 의도
            ch.FilterOpportunistic(match, 0.25f);
            Assert.IsTrue(ch.IsIn(0), "1.0 ≥ 0.85 → 남음");
        }

        [Test]
        public void Include_ExcludedCandidateNeverPicked()
        {
            var ch = new UtilityChooser();
            var rng = new SeedRng(9UL);
            var inc = new[] { true, false, true };
            for (int i = 0; i < 500; i++)
            {
                ch.Evaluate(new[] { 0.5f, 5f, 0.4f }, default, inc, default, 0f, P.NormEps);
                Assert.AreNotEqual(1, ch.Pick(0.3f, rng));
            }
            Assert.AreEqual(1f, ch.Score(0), 1e-6f, "정규화는 포함된 후보 최댓값 기준");
        }

        [Test]
        public void Noise_AddedToNormalizedScore()
        {
            var ch = new UtilityChooser();
            ch.Evaluate(new[] { 1f, 0.5f }, default, default, new[] { 0.1f, -0.2f }, 0f, P.NormEps);
            Assert.AreEqual(1.1f, ch.Score(0), 1e-6f);
            Assert.AreEqual(0.3f, ch.Score(1), 1e-6f);
        }
    }
}
