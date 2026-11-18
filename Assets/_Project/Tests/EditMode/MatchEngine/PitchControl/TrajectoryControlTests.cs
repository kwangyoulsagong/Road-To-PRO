using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.PitchControl
{
    /// <summary>specs/007 AC-05 (궤적 질의) · AC-03 (궤적 합 = 1).</summary>
    public class TrajectoryControlTests
    {
        static PitchControlParams P => PitchControlParams.Defaults;

        static ControlPlayers Layout(params (int slot, Vec2 pos)[] players)
        {
            var pl = new ControlPlayers();
            foreach (var (slot, pos) in players) PitchControlTestKit.Engine(pl, slot, pos, Vec2.Zero);
            return pl;
        }

        static TrajectoryControl GroundPass(ControlPlayers pl, int passer, int receiver, Vec2 from, Vec2 aim, float v0)
        {
            var tr = new TrajectorySamples();
            tr.Ground(ActionTestKit.Ball, from, aim, v0, P.Dt, P.Horizon, 105f, 68f);
            return new PitchControlQuery(P).Trajectory(pl, tr, passer, receiver);
        }

        [Test]
        public void BlockedGroundPass_OpponentOver50()
        {
            var pl = Layout((1, new Vec2(40f, 34f)), (2, new Vec2(60f, 34f)), (12, new Vec2(50f, 34.5f)));
            var r = GroundPass(pl, 1, 2, new Vec2(40f, 34f), new Vec2(60f, 34f), 14f);
            TestContext.WriteLine($"수신 {r.Receiver:F3} 동료 {r.Teammate:F3} 상대 {r.Opponent:F3} 아웃 {r.Out:F3}");
            Assert.Greater(r.Opponent, 0.5f);
        }

        [Test]
        public void OpenSpace_ReceiverOver90()
        {
            var pl = Layout((1, new Vec2(40f, 34f)), (2, new Vec2(55f, 34f)), (12, new Vec2(20f, 8f)), (13, new Vec2(90f, 62f)));
            var r = GroundPass(pl, 1, 2, new Vec2(40f, 34f), new Vec2(55f, 34f), 12f);
            TestContext.WriteLine($"수신 {r.Receiver:F3} 동료 {r.Teammate:F3} 상대 {r.Opponent:F3} 아웃 {r.Out:F3}");
            Assert.Greater(r.Receiver, 0.9f);
        }

        [Test]
        public void AboveAirZ_NoFieldGain()
        {
            // 공이 3.5 m 높이로 선수들 머리 위를 지나 라인 밖으로 — 필드 선수·GK 모두 0, 전부 아웃
            var pl = Layout((1, new Vec2(50f, 20f)), (2, new Vec2(50f, 40f)), (12, new Vec2(50f, 50f)), (11, new Vec2(50f, 60f)));
            var tr = new TrajectorySamples();
            tr.Line(new Vec2(50f, 10f), new Vec2(50f, 90f), 20f, 3.5f, P.Dt, P.Horizon);
            Assert.AreEqual(TrajectorySamples.EndOut, tr.End);
            var r = new PitchControlQuery(P).Trajectory(pl, tr, 1, 2);
            Assert.AreEqual(0f, r.Receiver + r.Teammate + r.Opponent, 1e-7f);
            Assert.AreEqual(1f, r.Out, 1e-6f);

            // 2.8 m: 필드 선수는 0, GK(슬롯 11, 손 높이 3.0 m)만 컨트롤을 얻는다
            tr.Line(new Vec2(50f, 10f), new Vec2(50f, 90f), 20f, 2.8f, P.Dt, P.Horizon);
            var q = new PitchControlQuery(P);
            var r2 = q.Trajectory(pl, tr, 1, 2);
            Assert.AreEqual(0f, r2.Receiver + r2.Teammate, 1e-7f);
            Assert.Greater(r2.Opponent, 0f, "GK만 손으로");
        }

        [Test]
        public void Trajectory_SumsToOne_1000()
        {
            var rng = new SeedRng(7009);
            var pl = new ControlPlayers();
            var q = new PitchControlQuery(P);
            var tr = new TrajectorySamples();
            for (int i = 0; i < 1000; i++)
            {
                PitchControlTestKit.Random(pl, rng, i % 2 == 0);
                var passer = -1;
                for (int s = 0; s < 22; s++) if (pl.Active[s]) { passer = s; break; }
                var from = pl.Pos[passer];
                var aim = PitchControlTestKit.RandomPoint(rng);
                if (i % 3 == 0) tr.Line(from, aim, 8f + 20f * rng.NextFloat(), 4f * rng.NextFloat(), P.Dt, P.Horizon);
                else tr.Ground(ActionTestKit.Ball, from, aim, 8f + 20f * rng.NextFloat(), P.Dt, P.Horizon, 105f, 68f);
                var receiver = (int)(rng.NextFloat() * 22f) % 22;
                var r = q.Trajectory(pl, tr, passer, receiver);
                foreach (var v in new[] { r.Receiver, r.Teammate, r.Opponent, r.Out })
                {
                    Assert.IsFalse(float.IsNaN(v), $"#{i} NaN");
                    Assert.That(v, Is.InRange(0f, 1f), $"#{i}");
                }
                Assert.AreEqual(1f, r.Receiver + r.Teammate + r.Opponent + r.Out, 1e-5f, $"#{i}");
            }
        }
    }
}
