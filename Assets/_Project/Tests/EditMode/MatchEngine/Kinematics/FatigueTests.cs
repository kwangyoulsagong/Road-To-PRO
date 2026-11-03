using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-09·10: 급성 W′ · 만성 C (FR-30~36).</summary>
    public class FatigueTests
    {
        /// <summary>30 m 스프린트 4회 (사이 휴식 5 s, 방향 교대). 마지막 스프린트 직후 W와 60 s 저속 후 W.</summary>
        static void FourSprints(int stamina, out float wAfter, out float wAfter60)
        {
            var k = One(Attr(15, stamina), new Vec2(30f, 34f), default, new Vec2(1f, 0f));
            wAfter = wAfter60 = 0f;
            for (int s = 0; s < 4; s++)
            {
                var dir = s % 2 == 0 ? 1f : -1f;
                var start = k.State(0).Position.X;
                k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(dir > 0f ? 105f : 0f, 34f)));
                int guard = 0;
                while ((k.State(0).Position.X - start) * dir < 30f && guard++ < 2000) k.Step();
                k.SetCommandImmediate(1, SteeringCommand.Idle());
                if (s == 3)
                {
                    wAfter = k.State(0).W;
                    Run(k, 6000);
                    wAfter60 = k.State(0).W;
                    return;
                }
                Run(k, 500);
            }
        }

        [Test]
        public void FourSprints_WBetween025And035_RecoversAbove06After60s()
        {
            FourSprints(10, out var w, out var w60);
            Assert.That(w, Is.InRange(0.25f, 0.35f), "연속 스프린트 4회 후 W");
            Assert.That(w60, Is.GreaterThanOrEqualTo(0.6f), "60 s 저속 후 회복");
        }

        [Test]
        public void HigherStamina_DrainsLess()
        {
            FourSprints(20, out var wHi, out _);
            FourSprints(5, out var wLo, out _);
            Assert.That(wHi, Is.GreaterThan(wLo));
        }

        [Test]
        public void StandardProfile90min_C06to09_Stamina20LowerThan5()
        {
            var k10 = StandardProfile90(10);
            var st = k10.Stats(0);
            Assert.That(st.Distance, Is.InRange(10000.0, 11500.0), "표준 프로필 총거리");
            Assert.That(k10.State(0).C, Is.InRange(0.6f, 0.9f), "지구력 10 만성 C");
            var c20 = StandardProfile90(20).State(0).C;
            var c5 = StandardProfile90(5).State(0).C;
            Assert.That(c20, Is.LessThan(c5));
        }

        [Test]
        public void Effects_ReduceSpeedAndAccel_AsSpecified()
        {
            var p = Params;
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(15), p);
            Assert.That(Fatigue.EffectiveMaxSpeed(pr, 1f, 0f, p), Is.EqualTo(pr.MaxSpeed));
            Assert.That(Fatigue.EffectiveMaxSpeed(pr, 0f, 1f, p), Is.EqualTo(pr.MaxSpeed * (1f - 0.06f - 0.05f)).Within(1e-4f));
            Assert.That(Fatigue.EffectiveMaxAccel(pr, 0f, 1f, p), Is.EqualTo(pr.MaxAccel * (1f - 0.04f - 0.02f)).Within(1e-4f));
            Assert.That(Fatigue.SprintCostWeight(0.5f), Is.EqualTo(2f));
            Assert.That(Fatigue.Curve(1f, 0f, p), Is.EqualTo(1f));
            Assert.That(Fatigue.Curve(0f, 1f, p), Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(Fatigue.Curve(0f, 1f, WithValues("{\"version\":1,\"values\":[{\"id\":\"kin.fatigue.curveAcute\",\"value\":0.3},{\"id\":\"kin.fatigue.curveChronic\",\"value\":0.3}]}")),
                Is.EqualTo(0.7f), "하한 f_min");
            Assert.That(Fatigue.ReactionDelay(pr, 0f, p), Is.EqualTo(pr.ReactionDelay), "피로 반응지연 가산 기본 0");
        }

        [Test]
        public void Substitute_StartsFresh_WAndCClamped()
        {
            var k = new KinematicsSystem(Params, new[] { new PlayerSetup(1, Attr(), new Vec2(50f, 34f), w: 3f, c: -2f) });
            Assert.That(k.State(0).W, Is.EqualTo(1f));
            Assert.That(k.State(0).C, Is.EqualTo(0f));
        }
    }
}
