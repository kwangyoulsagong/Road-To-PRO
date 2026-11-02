using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>T16 결정론 리뷰 지적 회귀 테스트.</summary>
    public class BallReviewRegressionTests
    {
        /// <summary>B-1: BallParams에 공유 가변 버퍼가 없다 (병렬 경기에서 공유해도 안전). 버퍼가 달라도 결과가 같다.</summary>
        [Test]
        public void Params_HasNoSharedMutableBuffer_LoftedSameWithDifferentScratch()
        {
            foreach (var f in typeof(BallParams).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                Assert.AreNotEqual(typeof(BallEventBuffer), f.FieldType, f.Name);
            var p = BallTestKit.Params();
            var a = BallInverse.LoftedSpeedFor(p, 30f, 0.5f, Vec3.Zero, 100f, new BallEventBuffer());
            var b = BallInverse.LoftedSpeedFor(p, 30f, 0.5f, Vec3.Zero, 100f, new BallEventBuffer(4));
            Assert.AreEqual(a.Speed, b.Speed);
        }

        /// <summary>R-1: 소유자 교체가 이벤트로 남는다. 같은 소유자면 이벤트 없음.</summary>
        [Test]
        public void TakeControl_OwnerChange_EmitsEvent()
        {
            var s = BallState.Controlled(7, new Vec3(50f, 34f, 0.109f));
            var ev = new BallEventBuffer();
            BallCommands.TakeControl(ref s, 7, s.Position, ev);
            Assert.AreEqual(0, ev.Count);
            BallCommands.TakeControl(ref s, 12, s.Position, ev);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(BallEventKind.OwnerChanged, ev[0].Kind);
            Assert.AreEqual(7, ev[0].OwnerFrom);
            Assert.AreEqual(12, ev[0].OwnerTo);
            Assert.AreEqual(12, s.OwnerId);
        }

        /// <summary>
        /// R-3: 골대 근처 무작위 슛에서 같은 서브스텝 이벤트가 시각 순이고, 경계 통과 지점이 실제 경로 위(포스트 안쪽이 아님)이며
        /// 공 중심이 경계에서 반지름만큼 바깥이다.
        /// </summary>
        [Test]
        public void GoalMouthShots_EventsTimeOrdered_BoundaryPointsOnRealPath()
        {
            var p = BallTestKit.Params();
            var rng = new SeedRng(77UL);
            var ev = new BallEventBuffer();
            var R = p.Radius + p.PostRadius;
            var postLow = 34f - (BallParams.GoalWidth * 0.5f + p.PostRadius);
            var postHigh = 34f + (BallParams.GoalWidth * 0.5f + p.PostRadius);
            int frameHits = 0, crossings = 0;
            for (int n = 0; n < 2000; n++)
            {
                var target = new Vec3(0f, rng.Range(29f, 39f), rng.Range(0.1f, 3f));
                var from = new Vec3(rng.Range(3f, 12f), rng.Range(20f, 48f), p.Radius);
                var dir = (target - from).Normalized();
                var s = BallState.InAir(from, dir * rng.Range(12f, 32f) + new Vec3(0f, 0f, rng.Range(0f, 4f)), new Vec3(0f, 0f, rng.Range(-60f, 60f)));
                for (int i = 0; i < 400 && s.Phase != BallPhase.Rest; i++)
                {
                    ev.Clear();
                    BallStepper.Step(ref s, p, ev, out _);
                    var last = 0f;
                    for (int k = 0; k < ev.Count; k++)
                    {
                        Assert.GreaterOrEqual(ev[k].Fraction, last, "시각 순");
                        last = ev[k].Fraction;
                        if (ev[k].Kind == BallEventKind.GoalFrameHit) frameHits++;
                        if (ev[k].Kind != BallEventKind.BoundaryCrossed) continue;
                        crossings++;
                        var c = ev[k].Position;
                        if (ev[k].Boundary == BallBoundary.GoalLineLow) Assert.AreEqual(-p.Radius, c.X, 2e-3f);
                        if (c.Z <= BallParams.GoalHeight + p.PostRadius)
                        {
                            Assert.GreaterOrEqual(new Vec2(c.X, c.Y - postLow).Length(), R - 2e-3f, "포스트 안쪽 지점");
                            Assert.GreaterOrEqual(new Vec2(c.X, c.Y - postHigh).Length(), R - 2e-3f, "포스트 안쪽 지점");
                        }
                    }
                }
            }
            Assert.Greater(frameHits, 50, "골대 접촉이 충분히 나와야 의미가 있다");
            Assert.Greater(crossings, 500);
        }

        /// <summary>재리뷰 NB-1: 같은 서브스텝에서 지면 접촉이 포스트 접촉보다 먼저면 포스트 반발은 없고, 바운스 입사 속도는 반사 전 속도다.</summary>
        [Test]
        public void GroundBeforePost_SameSubstep_NoGhostFrameHit()
        {
            var p = BallTestKit.Params();
            var postY = 34f - (BallParams.GoalWidth * 0.5f + p.PostRadius);
            var s = BallState.InAir(new Vec3(0.35f, postY, p.Radius + 0.02f), new Vec3(-30f, 0f, -5f), Vec3.Zero);
            var ev = new BallEventBuffer();
            BallStepper.Step(ref s, p, ev, out _);
            var bounced = false;
            for (int k = 0; k < ev.Count; k++)
            {
                Assert.AreNotEqual(BallEventKind.GoalFrameHit, ev[k].Kind, "지면이 먼저인데 골대 접촉이 기록됨");
                if (ev[k].Kind != BallEventKind.Bounce) continue;
                bounced = true;
                Assert.Less(ev[k].VelocityIn.X, -29f, "입사 속도는 반사 전");
                Assert.Less(ev[k].VelocityOut.X, 0f);
            }
            Assert.IsTrue(bounced);
        }

        /// <summary>R-6: 경계 이벤트에 실제 상태가 기록된다.</summary>
        [Test]
        public void BoundaryEvent_RecordsActualPhase()
        {
            var p = BallTestKit.Params();
            var s = BallState.RollingAt(new Vec2(50f, 0.5f), new Vec2(0f, -8f), p.Radius);
            var ev = new BallEventBuffer();
            for (int i = 0; i < 100; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                for (int k = 0; k < ev.Count; k++)
                    if (ev[k].Kind == BallEventKind.BoundaryCrossed)
                    {
                        Assert.AreEqual(BallPhase.Rolling, ev[k].From);
                        return;
                    }
            }
            Assert.Fail("경계 통과 없음");
        }

        /// <summary>R-5: 디버그(Strict)에서 NaN 상태는 즉시 예외 (조용히 넘어가지 않음).</summary>
        [Test]
        public void NaNState_Strict_Throws()
        {
            Assume.That(FloatGuard.DefaultMode, Is.EqualTo(GuardMode.Strict));
            var p = BallTestKit.Params();
            var s = BallState.InAir(new Vec3(50f, 34f, 2f), new Vec3(float.NaN, 0f, 0f), Vec3.Zero);
            Assert.Throws<NumericException>(() => BallStepper.Step(ref s, p, new BallEventBuffer(), out _));
        }

        /// <summary>N-1: 정지까지 표를 다 못 채우는 구름 감속은 로딩 시 거부.</summary>
        [Test]
        public void RollTable_Truncated_RejectedAtLoad()
        {
            Assert.Throws<TuningLoadException>(() => BallTestKit.WithValues(
                @"{""version"":1,""values"":[{""id"":""ball.roll.a"",""value"":0},{""id"":""ball.roll.b"",""value"":0}]}"));
        }

        /// <summary>N-4: 지면 아래에서 위로 향하는 Air 입력은 위쪽 속도를 잃지 않는다.</summary>
        [Test]
        public void BelowGround_Upward_KeepsFlying()
        {
            var p = BallTestKit.Params();
            var s = BallState.InAir(new Vec3(50f, 34f, 0f), new Vec3(5f, 0f, 6f), Vec3.Zero);
            var ev = new BallEventBuffer();
            BallStepper.Step(ref s, p, ev, out _);
            Assert.AreEqual(BallPhase.Air, s.Phase);
            Assert.Greater(s.Velocity.Z, 5f);
            Assert.Greater(s.Position.Z, p.Radius);
        }
    }
}
