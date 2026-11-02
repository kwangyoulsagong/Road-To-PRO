using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-11: 경계 통과는 "공 전체가 넘은 순간"에만 (FR-41).</summary>
    public class BallBoundaryTests
    {
        static List<BallEvent> RunCollect(BallState s, BallParams p, int steps)
        {
            var list = new List<BallEvent>();
            var ev = new BallEventBuffer();
            for (int i = 0; i < steps && s.Phase != BallPhase.Rest; i++)
            {
                ev.Clear();
                var before = s.Position;
                BallStepper.Step(ref s, p, ev, out _);
                for (int k = 0; k < ev.Count; k++)
                {
                    list.Add(ev[k]);
                    if (ev[k].Kind == BallEventKind.BoundaryCrossed && ev[k].Boundary == BallBoundary.GoalLineLow)
                        Assert.AreEqual(-p.Radius, ev[k].Position.X, 1e-3f, "통과 지점은 중심이 −r인 곳");
                }
            }
            return list;
        }

        static int Count(List<BallEvent> l, BallBoundary b) => l.FindAll(e => e.Kind == BallEventKind.BoundaryCrossed && e.Boundary == b).Count;

        [Test]
        public void CrossbarDown_OnLine_NoSignal()
        {
            var p = BallTestKit.Params();
            // 크로스바 하단을 맞고 아래로 떨어지는 공
            var s = BallState.InAir(new Vec3(0.6f, 34f, 2.0f), new Vec3(-6f, 0f, 4f), Vec3.Zero);
            var events = RunCollect(s, p, 600);
            Assert.IsTrue(events.Exists(e => e.Kind == BallEventKind.GoalFrameHit && e.Part == GoalFramePart.Crossbar), "크로스바 접촉 없음");
            Assert.LessOrEqual(Count(events, BallBoundary.GoalLineLow), 1);

            // 선에 걸친 공(중심이 −r..0)은 튀어도 굴러도 신호 없음
            var onLine = BallState.InAir(new Vec3(-0.05f, 34f, 1.0f), new Vec3(-0.02f, 0f, 0f), Vec3.Zero);
            var e2 = RunCollect(onLine, p, 3000);
            Assert.AreEqual(0, Count(e2, BallBoundary.GoalLineLow));
        }

        [TestCase(10f, false)]
        [TestCase(34f, true)]
        public void FullyOver_SignalOnce_InterpolatedTime(float y, bool mouth)
        {
            var p = BallTestKit.Params();
            var events = RunCollect(BallState.RollingAt(new Vec2(1f, y), new Vec2(-5f, 0f), p.Radius), p, 2000);
            var hits = events.FindAll(e => e.Kind == BallEventKind.BoundaryCrossed);
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(BallBoundary.GoalLineLow, hits[0].Boundary);
            Assert.AreEqual(mouth, hits[0].InGoalMouth);
            Assert.That(hits[0].Fraction, Is.InRange(0f, 1f));
        }

        [Test]
        public void Touchline_And_FarGoalLine()
        {
            var p = BallTestKit.Params();
            var e1 = RunCollect(BallState.RollingAt(new Vec2(50f, 1f), new Vec2(0f, -5f), p.Radius), p, 2000);
            Assert.AreEqual(1, Count(e1, BallBoundary.TouchlineLow));
            var e2 = RunCollect(BallState.RollingAt(new Vec2(50f, 67f), new Vec2(0f, 5f), p.Radius), p, 2000);
            Assert.AreEqual(1, Count(e2, BallBoundary.TouchlineHigh));
            var e3 = RunCollect(BallState.RollingAt(new Vec2(104f, 20f), new Vec2(5f, 0f), p.Radius), p, 2000);
            Assert.AreEqual(1, Count(e3, BallBoundary.GoalLineHigh));
        }

        [Test]
        public void IntoGoal_NetStopsBall_BehindLine()
        {
            var p = BallTestKit.Params();
            var s = BallState.RollingAt(new Vec2(3f, 34f), new Vec2(-25f, 0f), p.Radius);
            s = BallTestKit.Run(s, p, 1000);
            Assert.AreEqual(BallPhase.Rest, s.Phase);
            Assert.That(s.Position.X, Is.InRange(-p.NetDepth, -p.Radius));
        }

        [Test]
        public void PostHit_Reflects()
        {
            var p = BallTestKit.Params();
            var postY = 34f - (BallParams.GoalWidth * 0.5f + p.PostRadius);
            var s = BallState.RollingAt(new Vec2(2f, postY), new Vec2(-15f, 0f), p.Radius);
            var events = RunCollect(s, p, 50);
            Assert.IsTrue(events.Exists(e => e.Kind == BallEventKind.GoalFrameHit && e.Part == GoalFramePart.PostLow));
            var hit = events.Find(e => e.Kind == BallEventKind.GoalFrameHit);
            Assert.Greater(hit.VelocityOut.X, 0f, "튕겨 나옴");
            Assert.AreEqual(0.6f * 15f, hit.VelocityOut.X, 0.5f, "포스트 반발 0.6");
        }
    }
}
