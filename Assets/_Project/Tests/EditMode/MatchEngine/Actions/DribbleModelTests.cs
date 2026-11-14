using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 FR-01·02 · AC-03: 드리블 속도·터치 거리·방향 오차.</summary>
    public class DribbleModelTests
    {
        static ActionParams P => ActionTestKit.P;

        [Test]
        public void VmaxBall_Between78And92Percent()
        {
            Assert.AreEqual(9f * 0.78f, DribbleModel.VmaxBall(P, 9f, 0f), 1e-4f);
            Assert.AreEqual(9f * 0.92f, DribbleModel.VmaxBall(P, 9f, 1f), 1e-4f);
            Assert.AreEqual(9f * 0.85f, DribbleModel.VmaxBall(P, 9f, 0.5f), 1e-4f);
        }

        [Test]
        public void TouchDist_FormulaAndCap()
        {
            // 정지·평균 기술: 0.8 × lerp(1.25, 0.8, 0.5) = 0.8 × 1.025
            Assert.AreEqual(0.8f * 1.025f, DribbleModel.TouchDist(P, 0f, 0.5f, 0.5f), 1e-4f);
            // 최고 속도·최저 기술: 4.0 × 1.25 = 5.0 (상한)
            Assert.AreEqual(5.0f, DribbleModel.TouchDist(P, 20f, 0f, 0f), 1e-4f);
            Assert.Less(DribbleModel.TouchDist(P, 6f, 1f, 1f), DribbleModel.TouchDist(P, 6f, 0f, 0f), "기술이 높을수록 짧은 터치");
            Assert.Less(DribbleModel.TouchDist(P, 2f, 0.5f, 0.5f), DribbleModel.TouchDist(P, 8f, 0.5f, 0.5f), "빠를수록 긴 터치");
        }

        [Test]
        public void SigmaDeg_FormulaAndPressure()
        {
            Assert.AreEqual(12f, DribbleModel.SigmaDeg(P, 0f, 0f), 1e-4f);
            Assert.AreEqual(3f, DribbleModel.SigmaDeg(P, 1f, 0f), 1e-4f);
            Assert.AreEqual(7.5f * 1.5f, DribbleModel.SigmaDeg(P, 0.5f, 0.5f), 1e-4f);
        }

        [Test]
        public void TouchDistAndSigma_100k_Within3pct_MaxBelow5()
        {
            var rng = new SeedRng(701);
            const int n = 100_000;
            var mean = DribbleModel.TouchDist(P, 6f, 0.4f, 0.5f);
            var sigma = DribbleModel.SigmaDeg(P, 0.4f, 0.3f);
            double s = 0, s2 = 0, d = 0, d2 = 0;
            var max = 0f;
            for (int i = 0; i < n; i++)
            {
                var t = DribbleModel.SampleTouch(P, mean, sigma, new Vec2(1f, 0f), rng);
                s += t.ErrorDeg; s2 += (double)t.ErrorDeg * t.ErrorDeg;
                d += t.Distance; d2 += (double)t.Distance * t.Distance;
                if (t.Distance > max) max = t.Distance;
                Assert.AreEqual(t.ErrorDeg, Angle(t.Direction), 1e-2f);
            }
            var sdA = System.Math.Sqrt(s2 / n - (s / n) * (s / n));
            var mD = d / n;
            var sdD = System.Math.Sqrt(d2 / n - mD * mD);
            Assert.AreEqual(sigma, sdA, sigma * 0.03, "방향 오차 σ");
            Assert.AreEqual(mean, mD, mean * 0.03, "터치 거리 평균");
            Assert.AreEqual(mean * P.DribDistSigma, sdD, mean * P.DribDistSigma * 0.03, "터치 거리 σ");
            // 상한: 가장 긴 평균(5.0)에서도 5.0 초과 없음
            var rng2 = new SeedRng(702);
            for (int i = 0; i < n; i++)
                max = System.Math.Max(max, DribbleModel.SampleTouch(P, DribbleModel.TouchDist(P, 20f, 0f, 0f), 12f, new Vec2(0f, 1f), rng2).Distance);
            Assert.LessOrEqual(max, 5.0f);
        }

        static float Angle(Vec2 v) => DMath.Atan2(v.Y, v.X) / ActionMath.DegToRad;

        [Test]
        public void SampleTouch_ConsumesExactlyFour()
        {
            var a = new SeedRng(9);
            var b = new SeedRng(9);
            DribbleModel.SampleTouch(P, 2f, 6f, new Vec2(1f, 0f), a);
            for (int i = 0; i < DribbleModel.RngPerTouch; i++) b.NextFloat();
            Assert.AreEqual(b.NextFloat(), a.NextFloat());
        }

        [Test]
        public void StealProb_OpponentFirst_Higher()
        {
            Assert.Greater(DribbleModel.StealProb(P, 1.0f, 0.4f), 0.8f);
            Assert.Less(DribbleModel.StealProb(P, 0.4f, 1.0f), 0.1f);
        }
    }
}
