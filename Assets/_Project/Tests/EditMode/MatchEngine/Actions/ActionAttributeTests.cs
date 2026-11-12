using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-22: 능력치(표시 18 vs 8)·버릇(원터치 마스터)만 다른 두 선수의 실행 분포.</summary>
    public class ActionAttributeTests
    {
        static ActionParams P => ActionTestKit.P;
        const int N = 10_000;
        /// <summary>F(9999, 9999) 단측 p = 0.01 임계값 ≈ 1.048.</summary>
        const double FCrit = 1.048;

        static float A(int display) => ActionMath.Norm(KinematicAttributes.InternalFromDisplay(display));

        static double VarAngle(float aPass, float habitMul, ulong seed)
        {
            var rng = new SeedRng(seed);
            var sa = PassExecution.SigmaAngleDeg(P, aPass, 0.2f, 0.5f, 1f, habitMul);
            var ss = PassExecution.SigmaSpeed(P, aPass, 1f, habitMul);
            double s = 0, s2 = 0;
            for (int i = 0; i < N; i++)
            {
                PassExecution.Apply(new Vec3(14f, 0f, 0f), sa, ss, rng, out var e, out _);
                s += e; s2 += (double)e * e;
            }
            return s2 / N - (s / N) * (s / N);
        }

        [Test]
        public void Passing18vs8_FTest()
        {
            var v18 = VarAngle(A(18), 1f, 77);
            var v8 = VarAngle(A(8), 1f, 77);
            Assert.Greater(v8 / v18, FCrit, $"F = {v8 / v18}");
        }

        [Test]
        public void FirstTouch18vs8_FTest()
        {
            double Var(float aFt)
            {
                var rng = new SeedRng(78);
                double s = 0, s2 = 0;
                for (int i = 0; i < N; i++)
                {
                    var r = FirstTouchModel.Sample(P, aFt, 16f, 0.2f, new Vec2(1f, 0f), rng);
                    s += r.Offset; s2 += (double)r.Offset * r.Offset;
                }
                return s2 / N - (s / N) * (s / N);
            }
            var f = Var(A(8)) / Var(A(18));
            Assert.Greater(f, FCrit, $"F = {f}");
        }

        [Test]
        public void OneTouchMaster_SigmaRatio()
        {
            var mul = PassExecution.HabitMul(P, true, 1);
            Assert.AreEqual(0.95f, mul, 1e-6f);
            Assert.AreEqual(1f, PassExecution.HabitMul(P, false, 1), "원터치가 아니면 효과 없음");
            Assert.AreEqual(1f, PassExecution.HabitMul(P, true, 0));
            var with = System.Math.Sqrt(VarAngle(0.5f, mul, 79));
            var without = System.Math.Sqrt(VarAngle(0.5f, 1f, 79));
            Assert.AreEqual(0.95, with / without, 0.95 * 0.03);
        }
    }
}
