using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-07·08: 바운스.</summary>
    public class BallBounceTests
    {
        /// <summary>바닥에서 높이 h(공 아래면 기준)로 떨어뜨린 뒤 첫 반발의 최고 높이(공 아래면 기준).</summary>
        static float FirstReboundHeight(BallParams p, float h)
        {
            var s = BallState.InAir(new Vec3(50f, 34f, h + p.Radius), Vec3.Zero, Vec3.Zero);
            var ev = new BallEventBuffer();
            var bounced = false;
            var apex = 0f;
            for (int i = 0; i < 2000; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                for (int k = 0; k < ev.Count; k++)
                    if (ev[k].Kind == BallEventKind.Bounce)
                    {
                        if (bounced) return apex - p.Radius;
                        bounced = true;
                    }
                if (bounced) apex = DMath.Max(apex, s.Position.Z);
                if (s.Phase != BallPhase.Air) break;
            }
            return apex - p.Radius;
        }

        /// <summary>
        /// √ 관계 h = e²·H는 공기저항 없는 반발 법칙이라 항력 0으로 확인한다 (항력 포함이면 낙하·상승 손실로 약 −10%, 항력은 AC-03이 검증).
        /// 항력 포함 상태에서는 바운스 순간 속도비가 e와 같은지 확인한다.
        /// </summary>
        [Test]
        public void Drop2m_Dry_FirstRebound077()
        {
            var noDrag = BallTestKit.WithValues(@"{""version"":1,""values"":[{""id"":""ball.drag.panel32.cdLow"",""value"":0},{""id"":""ball.drag.panel32.cdHigh"",""value"":0}]}");
            var h = FirstReboundHeight(noDrag, 2f);
            Assert.AreEqual(0.62f * 0.62f * 2f, h, 0.7688f * 0.05f, $"반발 높이 {h}");

            var p = BallTestKit.Params(BallType.Panel32, SurfaceCondition.Dry);
            var s = BallState.InAir(new Vec3(50f, 34f, 2f + p.Radius), Vec3.Zero, Vec3.Zero);
            var ev = new BallEventBuffer();
            for (int i = 0; i < 1000; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                for (int k = 0; k < ev.Count; k++)
                    if (ev[k].Kind == BallEventKind.Bounce)
                    {
                        Assert.AreEqual(0.62f, ev[k].VelocityOut.Z / -ev[k].VelocityIn.Z, 1e-5f);
                        return;
                    }
            }
            Assert.Fail("바운스 없음");
        }

        [Test]
        public void Drop2m_Wet_HigherThanDry()
        {
            var dry = FirstReboundHeight(BallTestKit.Params(BallType.Panel32, SurfaceCondition.Dry), 2f);
            var wet = FirstReboundHeight(BallTestKit.Params(BallType.Panel32, SurfaceCondition.Wet), 2f);
            Assert.Greater(wet, dry);
        }

        [TestCase(0f)]
        [TestCase(6f)]
        public void RepeatedBounce_RollsWithin20_ThenRest(float horizontal)
        {
            var p = BallTestKit.Params(BallType.Panel32, SurfaceCondition.Wet);
            var s = BallState.InAir(new Vec3(30f, 34f, 10f), new Vec3(horizontal, 0f, 0f), Vec3.Zero);
            var ev = new BallEventBuffer();
            int bounces = 0;
            var rolled = false;
            for (int i = 0; i < 20000 && s.Phase != BallPhase.Rest; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                for (int k = 0; k < ev.Count; k++)
                    if (ev[k].Kind == BallEventKind.Bounce) bounces++;
                if (s.Phase == BallPhase.Rolling) rolled = true;
                Assert.GreaterOrEqual(s.Position.Z, p.Radius, "지면 아래");
            }
            Assert.That(bounces, Is.InRange(1, 20));
            Assert.IsTrue(rolled);
            Assert.AreEqual(BallPhase.Rest, s.Phase);
        }

        [Test]
        public void Bounce_SlidingBall_GainsTopspin_LosesSpeed()
        {
            var p = BallTestKit.Params();
            var s = BallState.InAir(new Vec3(30f, 34f, p.Radius + 0.01f), new Vec3(10f, 0f, -3f), Vec3.Zero);
            var ev = new BallEventBuffer();
            BallStepper.Step(ref s, p, ev, out _);
            Assert.Less(s.Velocity.X, 10f);
            Assert.Greater(s.Spin.Y, 0f, "앞으로 구르는 스핀(ωy > 0)");
            Assert.AreEqual(0f, s.Velocity.Y);
        }
    }
}
