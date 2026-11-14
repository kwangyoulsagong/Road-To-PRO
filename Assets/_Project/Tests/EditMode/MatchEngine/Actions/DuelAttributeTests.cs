using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 AC-04 · FR-30: Dribbling만 다른 두 선수(표시 18 vs 8)의 터치 방향 오차·테이크온 성공률.</summary>
    public class DuelAttributeTests
    {
        static ActionParams P => ActionTestKit.P;
        const int N = 10_000;
        const double FCrit = 1.048; // F(9999, 9999) 단측 p = 0.01

        static float Drib(int display) => KinematicAttributes.InternalFromDisplay(display);

        [Test]
        public void Dribbling18vs8_FTest_And_BinomialTakeOn()
        {
            double Var(int display)
            {
                var rng = new SeedRng(721);
                var a = ActionMath.Norm(Drib(display));
                var sigma = DribbleModel.SigmaDeg(P, a, 0.3f);
                double s = 0, s2 = 0;
                for (int i = 0; i < N; i++)
                {
                    var t = DribbleModel.SampleTouch(P, 2f, sigma, new Vec2(1f, 0f), rng);
                    s += t.ErrorDeg; s2 += (double)t.ErrorDeg * t.ErrorDeg;
                }
                return s2 / N - (s / N) * (s / N);
            }
            var f = Var(8) / Var(18);
            Assert.Greater(f, FCrit, $"F = {f}");

            int Wins(int display)
            {
                var w = ActionTestKit.World();
                ActionTestKit.Put(w, 5, new Vec2(60f, 34f));
                ActionTestKit.Put(w, 16, new Vec2(62f, 34f));
                w.SetDuel(5, TechnicalAttributes.Uniform(10).WithDuel(dribbling: Drib(display)), KinematicAttributes.Uniform(10), 50f, 0);
                var prob = TakeOnModel.Probability(P, w, 5, 16, 0f, 0f, 0f);
                var rng = new SeedRng(722);
                var c = 0;
                for (int i = 0; i < N; i++) if (TakeOnModel.Resolve(P, prob, rng) == TakeOnOutcome.Beat) c++;
                return c;
            }
            var w18 = Wins(18);
            var w8 = Wins(8);
            // 두 비율 차 z 검정 (단측 p < 0.01 → z > 2.326)
            var p1 = w18 / (double)N; var p2 = w8 / (double)N; var pp = (w18 + w8) / (2.0 * N);
            var z = (p1 - p2) / System.Math.Sqrt(pp * (1 - pp) * 2.0 / N);
            Assert.Greater(z, 2.326, $"18: {p1:P1}, 8: {p2:P1}, z = {z}");
        }
    }
}
