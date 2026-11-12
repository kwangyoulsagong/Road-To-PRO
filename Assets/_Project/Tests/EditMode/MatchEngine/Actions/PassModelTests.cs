using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-02(로지스틱 단조성) · AC-04(스루볼 조준) · FR-12 속도 후보.</summary>
    public class PassModelTests
    {
        static ActionParams P => ActionTestKit.P;

        [Test]
        public void Logistic_Monotonic_1000Cases()
        {
            var rng = new SeedRng(2008);
            for (int i = 0; i < 1000; i++)
            {
                var dist = rng.Range(1f, 50f);
                var press = rng.Range(0f, 1f);
                var lane = rng.Range(0f, 1f);
                var pc = rng.Range(0f, 1f);
                var fwd = rng.Range(-1f, 1f);
                var pass = rng.Range(0f, 1f);
                var vis = rng.Range(0f, 1f);
                var d = rng.Range(0.01f, 10f);
                var basis = PassModel.Logit(P, dist, press, lane, pc, fwd, pass, vis);
                Assert.Less(PassModel.Logit(P, dist + d, press, lane, pc, fwd, pass, vis), basis, "거리 증가 → 감소");
                Assert.Less(PassModel.Logit(P, dist, press + d * 0.1f, lane, pc, fwd, pass, vis), basis, "패서 압박 증가 → 감소");
                Assert.Less(PassModel.Logit(P, dist, press, lane + d * 0.1f, pc, fwd, pass, vis), basis, "레인 압박 증가 → 감소");
                Assert.Greater(PassModel.Logit(P, dist, press, lane, pc, fwd, pass + d * 0.1f, vis), basis, "패스 증가 → 증가");
                Assert.Greater(PassModel.Logit(P, dist, press, lane, pc, fwd, pass, vis + d * 0.1f), basis, "시야 증가 → 증가");
                var x0 = PassModel.XPass(P, dist, press, lane, pc, fwd, pass, vis);
                Assert.LessOrEqual(PassModel.XPass(P, dist + d, press, lane, pc, fwd, pass, vis), x0);
                Assert.GreaterOrEqual(PassModel.XPass(P, dist, press, lane, pc, fwd, pass + d * 0.1f, vis), x0);
                Assert.That(x0, Is.InRange(0.001f, 0.999f));
            }
        }

        [Test]
        public void ThroughBall_ZeroError_RunnerArrives()
        {
            var bp = ActionTestKit.Ball;
            var ev = new BallEventBuffer(32);
            var rng = new SeedRng(44);
            var tested = 0;
            for (int n = 0; n < 200; n++)
            {
                var from = new Vec2(rng.Range(30f, 50f), rng.Range(20f, 48f));
                var r = from + new Vec2(rng.Range(10f, 25f), rng.Range(-12f, 12f));
                var vr = new Vec2(rng.Range(3f, 8f), rng.Range(-3f, 3f));
                var v0 = PassModel.GroundSpeed(P, rng.Range(1, 4));
                if (!PassModel.ThroughAim(P, bp, from, r, vr, 1, 0f, v0, out var aim, out var tau)) continue;
                tested++;
                // 오차 0 실행: 실제 004 적분으로 공을 굴려 조준점에 가장 가까워지는 시각
                var u = (aim - from).Normalized();
                var s = BallState.RollingAt(from, u * v0, bp.Radius);
                var bestD = float.MaxValue;
                var tBall = 0f;
                for (int i = 1; i < 1000; i++)
                {
                    ev.Clear();
                    BallStepper.Step(ref s, bp, ev, out _);
                    var d = (s.Position.XY - aim).Length();
                    if (d < bestD) { bestD = d; tBall = i * bp.Dt; }
                }
                Assert.Less(bestD, 0.5f, "공이 조준점을 지난다");
                Assert.AreEqual(tau, tBall, 0.15f, "공 도착 시각 ≈ τ");
                var runner = r + vr * tBall;
                Assert.Less((runner - aim).Length(), 1f, "러너가 도착 지점 1 m 안");
            }
            Assert.Greater(tested, 100, "해가 있는 표본이 충분해야 한다");
        }

        [Test]
        public void ThroughBall_NoRoot_Excluded()
        {
            var bp = ActionTestKit.Ball;
            // 러너가 공보다 빠르게 멀어진다 → 판별식 < 0 또는 해가 음수뿐
            Assert.IsFalse(PassModel.ThroughAim(P, bp, new Vec2(10f, 34f), new Vec2(40f, 34f), new Vec2(30f, 0f), 1, 0f, 10f, out _, out _));
            Assert.IsFalse(PassModel.SolveIntercept(new Vec2(10f, 0f), new Vec2(20f, 0f), 5f, 5f, out _));
        }

        [Test]
        public void Best_OpenSpace_PicksValid_CloseRangeLowestSpeed()
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(55f, 34f));
            var o = PassModel.Best(P, ActionTestKit.Ball, w, 1, 2, w.Pos[1], w.Pos[2], 0f);
            Assert.IsTrue(o.Valid);
            Assert.Greater(o.XPass, 0.8f);
            ActionTestKit.Put(w, 3, new Vec2(40.8f, 34f));
            var c = PassModel.Best(P, ActionTestKit.Ball, w, 1, 3, w.Pos[1], w.Pos[3], 0f);
            Assert.IsTrue(c.Valid);
            Assert.AreEqual(0, c.Candidate, "1 m 이하 초근접은 최저 속도");
            Assert.IsFalse(PassModel.Best(P, ActionTestKit.Ball, w, 1, 1, w.Pos[1], w.Pos[1], 0f).Valid, "자기 자신 → 후보 없음");
        }

        [Test]
        public void FastBall_HarderToReceive_TouchRelieves()
        {
            Assert.AreEqual(1f, PassModel.RecvDecay(P, 10f, 0f), 1e-6f);
            Assert.AreEqual(0.6f, PassModel.RecvDecay(P, 24f, 0f), 1e-6f);
            Assert.Greater(PassModel.RecvDecay(P, 24f, 1f), PassModel.RecvDecay(P, 24f, 0f));
        }
    }
}
