using NUnit.Framework;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 FR-21·23·26·86 평가 노이즈·압박·집중 저하·리더십 (AC-06).</summary>
    public class EvalNoiseTests
    {
        static DecisionParams P => DecisionParams.Defaults;

        [Test]
        public void Pressure_LowComposureNoisier_EqualWithoutPressure_AC06()
        {
            var sigma = EvalNoise.Sigma(P, 0.5f, 0.5f);
            var low = EvalNoise.UnderPressure(sigma, 1f, 0f);   // Composure 1
            var high = EvalNoise.UnderPressure(sigma, 1f, 1f);  // Composure 20
            Assert.Greater(low, high);
            Assert.AreEqual(EvalNoise.UnderPressure(sigma, 0f, 0f), EvalNoise.UnderPressure(sigma, 0f, 1f));
        }

        [Test]
        public void Sigma_LowerWithBetterDecisionsVision()
        {
            Assert.Greater(EvalNoise.Sigma(P, 0f, 0f), EvalNoise.Sigma(P, 1f, 1f));
            Assert.AreEqual(P.NoiseBase, EvalNoise.Sigma(P, 1f, 1f), 1e-6f);
        }

        [Test]
        public void Pressure_FromNearestOpponentDistance()
        {
            Assert.AreEqual(1f, EvalNoise.Pressure(P, 0f), 1e-6f);
            Assert.AreEqual(0f, EvalNoise.Pressure(P, P.PressureRadius + 1f), 1e-6f);
            Assert.AreEqual(0.5f, EvalNoise.Pressure(P, P.PressureRadius * 0.5f), 1e-6f);
        }

        [Test]
        public void LapseHazard_ConcentrationFatigueLeadership()
        {
            var baseH = EvalNoise.LapseHazard(P, 0.5f, 0f, 0f);
            Assert.Greater(EvalNoise.LapseHazard(P, 0f, 0f, 0f), baseH, "집중력 낮을수록");
            Assert.Greater(EvalNoise.LapseHazard(P, 0.5f, 1f, 0f), baseH, "만성 피로 C 높을수록");
            Assert.Less(EvalNoise.LapseHazard(P, 0.5f, 0f, EvalNoise.LeaderRelief(P, 1f)), baseH, "리더 근처면 완화");
            Assert.AreEqual(0f, EvalNoise.LapseHazard(P, 1f, 1f, 0f), "집중력 20이면 0");
        }

        [Test]
        public void Temperature_FamiliarityRaises()
        {
            var natural = UtilityChooser.Temperature(P.TauMax, P.TauMin, 0.5f, 0f);
            var unfamiliar = UtilityChooser.Temperature(P.TauMax, P.TauMin, 0.5f, EvalNoise.FamiliarityTau(P, 0f));
            Assert.AreEqual(natural + P.FamiliarityTauAdd, unfamiliar, 1e-6f);
            Assert.AreEqual(0f, EvalNoise.FamiliarityTau(P, 1f));
        }
    }
}
