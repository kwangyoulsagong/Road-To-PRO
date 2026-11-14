using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 AC-05: 크로스·피니싱만 다른 두 선수(표시 18 vs 8), 버릇 "컷인 장인"만 다른 두 선수.</summary>
    public class ShotAttributeTests
    {
        static ActionParams P => ActionTestKit.P;
        const int N = 10_000;

        static float A(int display) => ActionMath.Norm(KinematicAttributes.InternalFromDisplay(display));

        static double AimVar(float sigma)
        {
            var rng = new SeedRng(7151);
            var m = new ShotTestKit.Moments();
            for (int i = 0; i < N; i++)
            {
                ShotExecution.Apply(new Vec3(22f, 0f, 2f), sigma, rng, out var ey, out _);
                m.Add(ey);
            }
            return m.Var;
        }

        static double LandVar(float sigma)
        {
            var rng = new SeedRng(7152);
            var m = new ShotTestKit.Moments();
            for (int i = 0; i < N; i++)
            {
                CrossModel.ApplyLanding(default, sigma, rng, out var e);
                m.Add(e.X);
            }
            return m.Var;
        }

        [Test]
        public void Finishing18vs8_Crossing18vs8_FTest()
        {
            var f = AimVar(ShotExecution.SigmaAimDeg(P, A(8), 25f, 0.3f, 0.5f, 1f, false))
                    / AimVar(ShotExecution.SigmaAimDeg(P, A(18), 25f, 0.3f, 0.5f, 1f, false));
            Assert.Greater(f, ShotTestKit.FCrit10k, $"피니싱 F = {f}");
            var g = LandVar(CrossModel.SigmaLanding(P, A(8), 0.3f)) / LandVar(CrossModel.SigmaLanding(P, A(18), 0.3f));
            Assert.Greater(g, ShotTestKit.FCrit10k, $"크로스 F = {g}");
        }

        [Test]
        public void CutInHabit_Sigma0p9_Within3pct()
        {
            var baseS = ShotExecution.SigmaAimDeg(P, 0.6f, 24f, 0.2f, 0.5f, 1f, false, ShotExecution.CutInMul(P, true, 0));
            var habit = ShotExecution.SigmaAimDeg(P, 0.6f, 24f, 0.2f, 0.5f, 1f, false, ShotExecution.CutInMul(P, true, 2));
            var ratio = System.Math.Sqrt(AimVar(habit) / AimVar(baseS));
            Assert.AreEqual(0.9, ratio, 0.9 * 0.03);
            Assert.AreEqual(1f, ShotExecution.CutInMul(P, false, 2), "감아차기 아니면 효과 없음");
        }
    }
}
