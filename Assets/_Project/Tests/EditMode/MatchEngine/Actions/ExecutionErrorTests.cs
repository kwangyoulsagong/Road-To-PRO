using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-05(패스 오차 σ 표본 ±3%) · FR-66 킥 기술·발.</summary>
    public class ExecutionErrorTests
    {
        static ActionParams P => ActionTestKit.P;

        [Test]
        public void PassSigma_100k_Within3pct()
        {
            var sa = PassExecution.SigmaAngleDeg(P, 0.4f, 0.3f, 0.6f, 1f, 1f);
            var ss = PassExecution.SigmaSpeed(P, 0.4f, 1f, 1f);
            Assert.AreEqual(ActionMath.Lerp(7f, 1.5f, 0.4f) * (1f + 0.8f * 0.3f) * (1f + 0.5f * 0.4f), sa, 1e-4f, "FR-13 식");
            Assert.AreEqual(ActionMath.Lerp(0.15f, 0.04f, 0.4f), ss, 1e-6f);
            var rng = new SeedRng(5005);
            double sumA = 0, sumA2 = 0, sumS = 0, sumS2 = 0;
            const int n = 100_000;
            var v = new Vec3(15f, 0f, 0f);
            for (int i = 0; i < n; i++)
            {
                var o = PassExecution.Apply(v, sa, ss, rng, out _, out _);
                var ang = System.Math.Atan2(o.Y, o.X) * 180.0 / System.Math.PI;
                var sp = o.XY.Length() / 15.0 - 1.0;
                sumA += ang; sumA2 += ang * ang; sumS += sp; sumS2 += sp * sp;
            }
            var stdA = System.Math.Sqrt(sumA2 / n - (sumA / n) * (sumA / n));
            var stdS = System.Math.Sqrt(sumS2 / n - (sumS / n) * (sumS / n));
            Assert.AreEqual(sa, stdA, sa * 0.03, "방향 σ");
            Assert.AreEqual(ss, stdS, ss * 0.03, "속도 σ");
        }

        [Test]
        public void Apply_ConsumesExactlyFour()
        {
            var a = new SeedRng(1);
            var b = new SeedRng(1);
            PassExecution.Apply(new Vec3(10f, 0f, 2f), 3f, 0.1f, a, out _, out _);
            for (int i = 0; i < PassExecution.RngPerPass; i++) b.NextFloat();
            Assert.AreEqual(b.SaveState(), a.SaveState());
        }

        [Test]
        public void KickPlan_FootTechAndAngle()
        {
            var facing = new Vec2(1f, 0f);
            // 정면 짧은 땅볼 → 인사이드, 주발(오른발)
            var a = KickTechnique.Plan(P, facing, new Vec2(1f, 0f), false, 10f, 10f, Foot.Right, 3);
            Assert.AreEqual(KickTech.Inside, a.Tech);
            Assert.AreEqual(BodyPart.RightFoot, a.Foot);
            Assert.AreEqual(1f, a.SigmaMul, 1e-6f);
            // 왼쪽 70° → 아웃사이드가 아니라 각도 > 60° → Outside, 자연 발 왼발(약발 3이면 사용) → 약발 배수
            var b = KickTechnique.Plan(P, facing, new Vec2(0.342f, 0.94f).Normalized(), false, 10f, 10f, Foot.Right, 3);
            Assert.AreEqual(KickTech.Outside, b.Tech);
            Assert.AreEqual(BodyPart.LeftFoot, b.Foot);
            Assert.IsTrue(b.WeakFoot);
            Assert.AreEqual(1.5f * (1f + 0.25f * 2f / 4f), b.SigmaMul, 1e-4f);
            Assert.Greater(b.Windup, a.Windup);
            // 약발 2 이하 → 주발 고집
            Assert.AreEqual(BodyPart.RightFoot, KickTechnique.Plan(P, facing, new Vec2(0.5f, 0.86f).Normalized(), false, 10f, 10f, Foot.Right, 2).Foot);
            // 뜬공: 짧으면 칩, 길면 인스텝 / 빠른 땅볼 인스텝
            Assert.AreEqual(KickTech.Chip, KickTechnique.Plan(P, facing, facing, true, 20f, 14f, Foot.Right, 3).Tech);
            Assert.AreEqual(KickTech.Instep, KickTechnique.Plan(P, facing, facing, true, 40f, 20f, Foot.Right, 3).Tech);
            Assert.AreEqual(KickTech.Instep, KickTechnique.Plan(P, facing, facing, false, 30f, 22f, Foot.Right, 3).Tech);
        }
    }
}
