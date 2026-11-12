using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 FR-70~72 이동 강도 (AC-21 로직 부분).</summary>
    public class IntensityTests
    {
        static DecisionParams P => DecisionParams.Defaults;

        [Test]
        public void HoldShapeNear_JogOrLess()
        {
            Assert.LessOrEqual(IntensityPicker.Pick(P, Consideration.HoldShape, P.JogDist - 0.5f, 0f, 0.5f, false, Intensity.Jog, false), Intensity.Jog);
            Assert.AreEqual(Intensity.Walk, IntensityPicker.Pick(P, Consideration.HoldShape, P.WalkDist * 0.5f, 0f, 0.5f, false, Intensity.Walk, false));
            Assert.AreEqual(Intensity.Run, IntensityPicker.Pick(P, Consideration.HoldShape, P.JogDist + 5f, 0f, 0.5f, false, Intensity.Run, false), "멀면 달리기");
        }

        [Test]
        public void OneStepPerTick_TwoOnTrigger()
        {
            Assert.AreEqual(Intensity.Run, IntensityPicker.Pick(P, Consideration.HoldShape, 0.1f, 0f, 0.5f, false, Intensity.Sprint, false), "질주 → 걷기 바로 안 감");
            Assert.AreEqual(Intensity.Jog, IntensityPicker.Pick(P, Consideration.HoldShape, 0.1f, 0f, 0.5f, false, Intensity.Sprint, true), "트리거면 두 단계");
            Assert.AreEqual(Intensity.Jog, IntensityPicker.Pick(P, Consideration.RunInBehind, 20f, 0f, 0.5f, false, Intensity.Walk, false), "올릴 때도 한 단계");
        }

        [Test]
        public void Sprint_DisallowedWhenFatigued()
        {
            Assert.IsTrue(IntensityPicker.SprintAllowed(P, 0f, 0.5f));
            Assert.IsFalse(IntensityPicker.SprintAllowed(P, 1f, 1f));
            Assert.AreEqual(Intensity.Run, IntensityPicker.Pick(P, Consideration.RunInBehind, 20f, 1f, 0.5f, false, Intensity.Run, false));
            Assert.AreEqual(Intensity.Sprint, IntensityPicker.Pick(P, Consideration.RunInBehind, 20f, 0f, 0.5f, false, Intensity.Run, false));
        }

        [Test]
        public void SpeedRatio_NotZero_FR70()
        {
            foreach (Intensity i in System.Enum.GetValues(typeof(Intensity))) Assert.Greater(IntensityPicker.SpeedRatio(P, i), 0f);
        }

        [Test]
        public void Decider_FatiguedPlayer_SprintsLess_AC21()
        {
            int Sprints(float c)
            {
                var f = new DecisionFixture();
                f.W.C[9] = c;
                f.Refresh();
                var rng = new SeedRng(40UL);
                int n = 0;
                for (int i = 0; i < 1000; i++)
                {
                    f.ResetState(9);
                    f.State[9].Intensity = Intensity.Run;
                    if (f.Decide(9, rng, true).Intensity == Intensity.Sprint) n++;
                }
                return n;
            }
            var fresh = Sprints(0f);
            var tired = Sprints(1f);
            Assert.Greater(fresh, tired, $"C=0 {fresh} vs C=1 {tired}");
        }

        [Test]
        public void Decider_MaxSpeedFromIntensity_NeverDefaultZero()
        {
            var f = new DecisionFixture();
            var r = f.Decide(3, new SeedRng(41UL), true);
            Assert.Greater(r.MaxSpeed, 0f, "속도 0(= 최고 속도) 기본값에 기대지 않는다");
            Assert.LessOrEqual(r.MaxSpeed, f.W.Profile[3].MaxSpeed + 1e-4f);
        }

        [Test]
        public void Settle_Hysteresis_FR43a()
        {
            var f = new DecisionFixture();
            const int s = 3; // 홈 수비 25·42, 앵커 = 현재 위치
            var rng = new SeedRng(42UL);
            OffBallResult r = default;
            var found = false;
            for (int k = 0; k < 50 && !found; k++)
            {
                f.ResetState(s);
                r = f.Decide(s, rng, true);
                found = r.Kind == Consideration.HoldShape || r.Kind == Consideration.LineHold;
            }
            Assert.IsTrue(found, "형태 유지 표본");
            Assert.IsTrue(r.Settled, "목표 안 → 선다");
            var stand = f.State[s].Point;
            var kind = r.Kind;
            f.W.Anchor[s] = f.W.Anchor[s] + new Vec2(4f, 0f); // settle_in(3) < 4 < settle_out(5)
            f.W.DefLineX[0] += 4f;
            f.W.Tick += 10;
            r = f.Decide(s, rng);
            if (r.Kind == kind)
            {
                Assert.IsTrue(r.Settled, "사이 거리 → 계속 서 있음");
                Assert.AreEqual(stand, r.Point);
            }
            f.W.Anchor[s] = f.W.Anchor[s] + new Vec2(3f, 0f); // 7 m > settle_out
            f.W.DefLineX[0] += 3f;
            f.W.Tick += 10;
            r = f.Decide(s, rng);
            if (r.Kind == kind) Assert.IsFalse(r.Settled, "멀어지면 다시 따른다");
        }
    }
}
