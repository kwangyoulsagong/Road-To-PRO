using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-12: 경계 · 예외 상황 (잘못된 표면 파라미터는 BallParamsTests).</summary>
    public class BallEdgeCaseTests
    {
        static void AssertFinite(BallState s)
        {
            foreach (var v in new[] { s.Position, s.Velocity, s.Spin })
                Assert.IsTrue(FloatGuard.IsFinite(v.X) && FloatGuard.IsFinite(v.Y) && FloatGuard.IsFinite(v.Z), $"NaN/Inf: {v}");
        }

        [Test]
        public void ZeroVelocity_NoNaN()
        {
            var p = BallTestKit.Params();
            var air = BallTestKit.Run(BallState.InAir(new Vec3(50f, 34f, 3f), Vec3.Zero, Vec3.Zero), p, 3000);
            AssertFinite(air);
            Assert.AreEqual(BallPhase.Rest, air.Phase);
            var roll = BallTestKit.Run(BallState.RollingAt(new Vec2(50f, 34f), Vec2.Zero, p.Radius), p, 10);
            AssertFinite(roll);
            Assert.AreEqual(BallPhase.Rest, roll.Phase);
        }

        [Test]
        public void SpinOnly_NoMagnus()
        {
            var p = BallTestKit.Params();
            var a = BallAero.AirAcceleration(new Vec3(1e-5f, 0f, 0f), new Vec3(0f, 0f, 150f), p);
            Assert.AreEqual(new Vec3(0f, 0f, -p.Gravity), a);
            var s = BallState.InAir(new Vec3(50f, 34f, 2f), Vec3.Zero, new Vec3(0f, 0f, 100f));
            var ev = new BallEventBuffer();
            BallStepper.Step(ref s, p, ev, out _);
            Assert.AreEqual(50f, s.Position.X);
            Assert.AreEqual(34f, s.Position.Y);
            Assert.Less(s.Spin.Z, 100f, "스핀은 감쇠만");
        }

        [Test]
        public void ExtremeInput_ClampedWithWarning()
        {
            var p = BallTestKit.Params();
            var s = BallState.Controlled(1, new Vec3(50f, 34f, p.Radius));
            var ev = new BallEventBuffer();
            BallCommands.Kick(ref s, new Vec3(60f, 0f, 10f), new Vec3(0f, 0f, 300f), p, ev);
            Assert.AreEqual(BallEventKind.InputClamped, ev[0].Kind);
            Assert.LessOrEqual(s.Velocity.Length(), 40f + 1e-3f);
            Assert.LessOrEqual(s.Spin.Length(), 180f + 1e-3f);
            AssertFinite(BallTestKit.Run(s, p, 3000));
        }

        [Test]
        public void NonFiniteInput_Rejected()
        {
            var p = BallTestKit.Params();
            var s = BallState.Controlled(1, new Vec3(50f, 34f, p.Radius));
            Assert.Throws<NumericException>(() => BallCommands.Kick(ref s, new Vec3(float.NaN, 0f, 0f), Vec3.Zero, p, new BallEventBuffer()));
            Assert.AreEqual(BallPhase.Controlled, s.Phase);
        }

        [Test]
        public void BelowGround_CorrectedSameStep()
        {
            var p = BallTestKit.Params();
            var s = BallState.InAir(new Vec3(50f, 34f, -0.5f), new Vec3(3f, 0f, -2f), Vec3.Zero);
            var ev = new BallEventBuffer();
            BallStepper.Step(ref s, p, ev, out _);
            Assert.GreaterOrEqual(s.Position.Z, p.Radius);
            var bounced = false;
            for (int k = 0; k < ev.Count; k++) bounced |= ev[k].Kind == BallEventKind.Bounce;
            Assert.IsTrue(bounced);

            var c = BallState.Controlled(1, new Vec3(50f, 34f, -1f));
            ev.Clear();
            BallCommands.Kick(ref c, new Vec3(10f, 0f, 0f), Vec3.Zero, p, ev);
            Assert.AreEqual(p.Radius, c.Position.Z);
        }

        [Test]
        public void PostAndCrossbar_SameSubstep_Ordered()
        {
            var p = BallTestKit.Params();
            var postY = 34f - (BallParams.GoalWidth * 0.5f + p.PostRadius);
            var barZ = BallParams.GoalHeight + p.PostRadius;
            BallState Shot() => BallState.InAir(new Vec3(0.4f, postY + 0.05f, barZ - 0.05f), new Vec3(-25f, 0f, 0f), Vec3.Zero);
            var ev = new BallEventBuffer();
            var hits = 0;
            var s = Shot();
            for (int i = 0; i < 5; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                var last = 0f;
                int stepHits = 0;
                for (int k = 0; k < ev.Count; k++)
                {
                    Assert.GreaterOrEqual(ev[k].Fraction, last, "시각 순");
                    last = ev[k].Fraction;
                    if (ev[k].Kind == BallEventKind.GoalFrameHit) stepHits++;
                }
                Assert.LessOrEqual(stepHits, GoalFrame.MaxContactsPerSubstep);
                hits += stepHits;
                AssertFinite(s);
            }
            Assert.GreaterOrEqual(hits, 1);
            Assert.Greater(s.Velocity.X, 0f, "튕겨 나옴");

            // 같은 입력 → 같은 결과
            var a = Shot();
            var b = Shot();
            BallTestKit.Run(a, p, 5);
            Assert.AreEqual(BallTestKit.Run(a, p, 5).Position, BallTestKit.Run(b, p, 5).Position);
        }

        [Test]
        public void EventBuffer_Overflow_Throws()
        {
            var buf = new BallEventBuffer(1);
            buf.Add(BallEvent.Transition(0f, Vec3.Zero, BallPhase.Controlled, BallPhase.Air, BallTransitionReason.LoftedKick));
            Assert.Throws<InvalidOperationException>(() =>
                buf.Add(BallEvent.Transition(0f, Vec3.Zero, BallPhase.Controlled, BallPhase.Air, BallTransitionReason.LoftedKick)));
        }

        [Test]
        public void Kick_NotControlled_Throws()
        {
            var p = BallTestKit.Params();
            var s = BallState.AtRest(new Vec2(50f, 34f), p.Radius);
            Assert.Throws<InvalidOperationException>(() => BallCommands.Kick(ref s, new Vec3(5f, 0f, 0f), Vec3.Zero, p, new BallEventBuffer()));
        }
    }
}
