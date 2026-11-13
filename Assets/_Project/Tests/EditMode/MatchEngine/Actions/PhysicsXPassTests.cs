using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-03: 물리 xPass — 빈 공간 15 m > 0.9, 레인 중간 상대 < 0.5, 2.5 m 위 필드 선수 인터셉트 0.</summary>
    public class PhysicsXPassTests
    {
        static ActionParams P => ActionTestKit.P;

        static BallState GroundPass(ActionWorld w, int from, int to, float extra = 0f)
        {
            var bp = ActionTestKit.Ball;
            var d = w.Pos[to] - w.Pos[from];
            var inv = BallInverse.GroundSpeedFor(bp, d.Length(), 3f + extra);
            var u = d.Normalized();
            return BallState.RollingAt(w.Pos[from], u * inv.Speed, bp.Radius);
        }

        [Test]
        public void OpenSpace15m_Above09()
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(55f, 34f));
            ActionTestKit.Put(w, 12, new Vec2(70f, 10f)); // 멀리 있는 상대
            var r = new PhysicsXPass().Evaluate(P, ActionTestKit.Ball, w, 1, 2, GroundPass(w, 1, 2));
            Assert.Greater(r.Receiver, 0.9f, $"수신자 {r.Receiver} 상대 {r.Opponent}");
            Assert.AreEqual(1f, r.Receiver + r.Teammate + r.Opponent + r.Out, 1e-4f);
        }

        [Test]
        public void DefenderInLane_Below05()
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(55f, 34f));
            ActionTestKit.Put(w, 12, new Vec2(47.5f, 34f));
            var r = new PhysicsXPass().Evaluate(P, ActionTestKit.Ball, w, 1, 2, GroundPass(w, 1, 2));
            Assert.Less(r.Receiver, 0.5f, $"수신자 {r.Receiver} 상대 {r.Opponent}");
            Assert.Greater(r.Opponent, 0.5f);
        }

        [Test]
        public void HighBall_NoFieldIntercept()
        {
            var bp = ActionTestKit.Ball;
            var start = BallState.InAir(new Vec3(30f, 34f, bp.Radius), new Vec3(16f, 0f, 14f), Vec3.Zero);
            // 수비수 근처 통과 높이와 첫 착지 지점 (수신자는 착지 지점에 선다)
            var s = start;
            var ev = new BallEventBuffer(32);
            var minZNear = float.MaxValue;
            var landX = 0f;
            for (int i = 0; i < 400 && landX == 0f; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, bp, ev, out _);
                if (System.Math.Abs(s.Position.X - 50f) < 6f && s.Position.Z < minZNear) minZNear = s.Position.Z;
                for (int k = 0; k < ev.Count; k++) if (ev[k].Kind == BallEventKind.Bounce) landX = s.Position.X;
            }
            Assume.That(minZNear, Is.GreaterThan(2.5f), "고정 상황: 수비수 부근 통과 높이");
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(30f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(landX, 34f));
            ActionTestKit.Put(w, 12, new Vec2(50f, 34f), default, 10, 1); // 정점 아래, 느린 수비수 — 착지 전에 착지점에 못 감
            var r = new PhysicsXPass().Evaluate(P, bp, w, 1, 2, start);
            Assert.AreEqual(0f, r.Opponent, 1e-6f, "2.5 m 위 구간에서 필드 선수 인터셉트 0");
            Assert.AreEqual(0f, BallControl.Prob(P, 0f, 10f, 2.6f, 1f, 0f, true, false));
        }
    }
}
