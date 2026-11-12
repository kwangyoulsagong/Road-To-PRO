using System.Reflection;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-10(경계 · 예외 상황) · AC-11(평가는 RNG를 건드리지 않음).</summary>
    public class ActionEdgeCaseTests
    {
        static ActionParams P => ActionTestKit.P;

        static void Finite(float v, string what) => Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), what);

        [Test]
        public void NoReceiver_NoCandidate()
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            Assert.IsFalse(PassModel.Best(P, ActionTestKit.Ball, w, 1, 1, w.Pos[1], w.Pos[1], 0f).Valid, "수신자 = 패서");
            Assert.IsFalse(PassModel.Best(P, ActionTestKit.Ball, w, 1, -1, w.Pos[1], new Vec2(50f, 34f), 0f).Valid, "수신 후보 0");
        }

        [Test]
        public void UltraClose_LowestSpeed_NoNaN()
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(40f, 34f)); // 0 m
            ActionTestKit.Put(w, 3, new Vec2(40.6f, 34f));
            foreach (var r in new[] { 2, 3 })
            {
                var o = PassModel.Best(P, ActionTestKit.Ball, w, 1, r, w.Pos[1], w.Pos[r], 0f);
                Assert.IsTrue(o.Valid);
                Assert.AreEqual(0, o.Candidate);
                Finite(o.XPass, "xPass");
                Finite(o.ArriveTime, "도착 시간");
            }
            Assert.AreEqual(0f, PassModel.Forwardness(w.Pos[1], w.Pos[1], 1));
            Assert.AreEqual(0f, PassModel.LanePressure(P, w, 0, w.Pos[1], w.Pos[1]));
        }

        [Test]
        public void NegativeDiscriminant_Excluded()
        {
            Assert.IsFalse(PassModel.SolveIntercept(new Vec2(5f, 0f), new Vec2(12f, 0f), 6f, 5f, out _), "판별식 < 0 / 음수 해");
            Assert.IsFalse(PassModel.SolveIntercept(new Vec2(-5f, 0f), new Vec2(-12f, 0f), 6f, 5f, out _), "해가 음수뿐");
        }

        [Test]
        public void HugeLogit_Clamped()
        {
            var hi = PassModel.XPass(P, -1e30f, -1e30f, 0f, 1e30f, 1f, 1f, 1f);
            var lo = PassModel.XPass(P, 1e30f, 1e30f, 1e30f, 0f, -1f, 0f, 0f);
            Assert.AreEqual(0.999f, hi, 1e-6f);
            Assert.AreEqual(0.001f, lo, 1e-6f);
            Finite(ActionMath.Sigmoid(float.PositiveInfinity), "시그모이드 +∞");
        }

        [Test]
        public void ExtremeAttributes_OpenInterval()
        {
            var w = ActionTestKit.World();
            foreach (var d in new[] { 1, 20 })
            foreach (var e in new[] { 1, 20 })
            {
                ActionTestKit.Put(w, 1, new Vec2(40f, 34f), default, d);
                ActionTestKit.Put(w, 2, new Vec2(65f, 30f), default, e);
                ActionTestKit.Put(w, 12, new Vec2(52f, 33f), new Vec2(-3f, 0f), e);
                var o = PassModel.Best(P, ActionTestKit.Ball, w, 1, 2, w.Pos[1], w.Pos[2], 1f);
                Assert.That(o.XPass, Is.InRange(0.001f, 0.999f));
                var c = BallControl.Prob(P, 0.5f, 25f, 0.2f, w.FirstTouch[2], 1f, true, false);
                Assert.That(c, Is.InRange(0.001f, 0.999f));
                Finite(PassExecution.SigmaAngleDeg(P, w.Passing[1], 1f, w.Composure[1], 1.5f, 1f), "σ");
                Finite(FirstTouchModel.Sigma(P, w.FirstTouch[2], 30f, 1f), "첫 터치 σ");
            }
        }

        [Test]
        public void NoDefenders_ZeroPressure()
        {
            Assert.AreEqual(0f, PressureModel.Team(P, new Vec2(50f, 34f), Vec2.Zero, new Vec2[0], new Vec2[0], 0, 68f));
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(60f, 34f));
            Assert.AreEqual(0f, PassModel.LanePressure(P, w, 0, w.Pos[1], w.Pos[2]));
            Assert.AreEqual(1f, w.Control(P, 2, w.Pos[2]), "상대 0명 → 컨트롤 1");
        }

        [Test]
        public void Evaluate_DoesNotTouchRng()
        {
            // 평가 API는 RNG를 인자로 받지 않는다 (타입 보장)
            foreach (var t in new[] { typeof(PassModel), typeof(PressureModel), typeof(BallControl), typeof(PhysicsXPass), typeof(ActionWorld), typeof(KickTechnique) })
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    foreach (var prm in m.GetParameters())
                        Assert.AreNotEqual(typeof(SeedRng), prm.ParameterType, $"{t.Name}.{m.Name}");
            // 호출 전후 같은 RNG 상태 (경기 RNG를 옆에 두고 평가를 부른다)
            var rng = new SeedRng(11);
            var before = rng.SaveState();
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(55f, 34f));
            ActionTestKit.Put(w, 12, new Vec2(48f, 35f), new Vec2(-3f, 0f));
            PassModel.Best(P, ActionTestKit.Ball, w, 1, 2, w.Pos[1], w.Pos[2], 0.3f);
            new PhysicsXPass().Evaluate(P, ActionTestKit.Ball, w, 1, 2, BallState.RollingAt(w.Pos[1], new Vec2(14f, 0f), ActionTestKit.Ball.Radius));
            Assert.AreEqual(before, rng.SaveState());
        }
    }
}
