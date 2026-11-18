using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.PitchControl
{
    /// <summary>specs/007 AC-06: 경계 · 예외 상황 — NaN 0, 인덱스 초과 0, 규칙대로.</summary>
    public class PitchControlEdgeTests
    {
        static PitchControlParams P => PitchControlParams.Defaults;

        static void Finite(PointControl r)
        {
            Assert.IsFalse(float.IsNaN(r.Att) || float.IsNaN(r.Def) || float.IsNaN(r.None));
            Assert.AreEqual(1f, r.Att + r.Def + r.None, 1e-5f);
        }

        static void Finite(TrajectoryControl r)
        {
            Assert.IsFalse(float.IsNaN(r.Receiver) || float.IsNaN(r.Teammate) || float.IsNaN(r.Opponent) || float.IsNaN(r.Out));
            Assert.AreEqual(1f, r.Receiver + r.Teammate + r.Opponent + r.Out, 1e-5f);
        }

        [Test]
        public void EmptyTeam_ZeroControl_NoThrow()
        {
            var pl = new ControlPlayers();
            PitchControlTestKit.Engine(pl, 3, new Vec2(50f, 34f), Vec2.Zero);
            var q = new PitchControlQuery(P);
            var r = q.Point(pl, 0, new Vec2(55f, 34f), 0.5f);
            Finite(r);
            Assert.AreEqual(0f, r.Def);
            Assert.Greater(r.Att, 0.9f);
            var none = q.Point(new ControlPlayers(), 0, new Vec2(55f, 34f), 0.5f);
            Assert.AreEqual(1f, none.None);
        }

        [Test]
        public void NotConverged_ReturnsUncontrolled()
        {
            var pl = new ControlPlayers { PaperArrival = true };
            pl.SetPaper(1, 0, false, new Vec2(0f, 0f), Vec2.Zero, P); // τ = 0.7 + 125/5 ≈ 25.7 s > 10 s
            var r = new PitchControlQuery(P).Point(pl, 0, new Vec2(105f, 68f), 0f);
            Finite(r);
            Assert.Greater(r.None, 0.99f);
        }

        [Test]
        public void ZeroDistance_TauIsReaction()
        {
            var pl = new ControlPlayers { PaperArrival = true };
            pl.SetPaper(1, 0, false, new Vec2(30f, 30f), Vec2.Zero, P);
            pl.SetPaper(12, 1, false, new Vec2(40f, 30f), Vec2.Zero, P);
            var r = new PitchControlQuery(P).Point(pl, 0, new Vec2(30f, 30f), 0f);
            Finite(r);
            Assert.Greater(r.Att, r.Def);
            Assert.AreEqual(1, r.Leader);
        }

        [Test]
        public void OutsidePitch_AllUncontrolled_BoundaryInside()
        {
            var pl = new ControlPlayers();
            PitchControlTestKit.Engine(pl, 1, new Vec2(100f, 60f), Vec2.Zero);
            var q = new PitchControlQuery(P);
            foreach (var p in new[] { new Vec2(-0.01f, 30f), new Vec2(105.01f, 30f), new Vec2(50f, -1f), new Vec2(50f, 68.5f) })
            {
                var r = q.Point(pl, 0, p, 0.5f);
                Assert.AreEqual(1f, r.None, $"{p.X},{p.Y}");
            }
            var edge = q.Point(pl, 0, new Vec2(105f, 68f), 0.5f);
            Finite(edge);
            Assert.Greater(edge.Att, 0f, "경계 위 (105, 68)는 피치 안");
        }

        [Test]
        public void LineOutTrajectory_RemainderIsOut()
        {
            var pl = new ControlPlayers();
            PitchControlTestKit.Engine(pl, 1, new Vec2(50f, 60f), Vec2.Zero);
            PitchControlTestKit.Engine(pl, 2, new Vec2(30f, 20f), Vec2.Zero);
            var tr = new TrajectorySamples();
            tr.Ground(ActionTestKit.Ball, new Vec2(50f, 60f), new Vec2(50f, 75f), 25f, P.Dt, P.Horizon, 105f, 68f);
            Assert.AreEqual(TrajectorySamples.EndOut, tr.End);
            var r = new PitchControlQuery(P).Trajectory(pl, tr, 1, 2);
            Finite(r);
            Assert.Greater(r.Out, 0.9f);
        }

        [Test]
        public void StoppedTrajectory_SplitsByEndPoint()
        {
            var pl = new ControlPlayers();
            PitchControlTestKit.Engine(pl, 1, new Vec2(20f, 34f), Vec2.Zero);
            PitchControlTestKit.Engine(pl, 12, new Vec2(80f, 34f), Vec2.Zero);
            var tr = new TrajectorySamples();
            tr.Ground(ActionTestKit.Ball, new Vec2(20f, 34f), new Vec2(80f, 34f), 6f, P.Dt, 10f, 105f, 68f); // 상한 10 s — 구름 공이 멈출 때까지
            Assert.AreEqual(TrajectorySamples.EndRest, tr.End, "느린 공은 상한 전에 멈춘다");
            var r = new PitchControlQuery(P).Trajectory(pl, tr, 1, -1);
            Finite(r);
            Assert.Greater(r.Opponent, 0.5f, "패서는 빠지고, 멈춘 지점은 남은 상대가 먼저");
        }

        [Test]
        public void PasserExcluded_GetsNothing()
        {
            var pl = new ControlPlayers();
            PitchControlTestKit.Engine(pl, 1, new Vec2(50f, 34f), Vec2.Zero);
            var tr = new TrajectorySamples();
            tr.Ground(ActionTestKit.Ball, new Vec2(50f, 34f), new Vec2(52f, 34f), 3f, P.Dt, P.Horizon, 105f, 68f);
            var r = new PitchControlQuery(P).Trajectory(pl, tr, 1, 2);
            Finite(r);
            Assert.AreEqual(0f, r.Receiver + r.Teammate + r.Opponent, 1e-7f);
        }

        [Test]
        public void SentOff_NotCounted()
        {
            var pl = new ControlPlayers();
            PitchControlTestKit.Engine(pl, 1, new Vec2(50f, 34f), Vec2.Zero);
            PitchControlTestKit.Engine(pl, 12, new Vec2(53f, 34f), Vec2.Zero);
            var q = new PitchControlQuery(P);
            var before = q.Point(pl, 0, new Vec2(54f, 34f), 0.2f);
            pl.Active[12] = false; // 퇴장 직후: 다음 질의부터 빠진다
            var after = q.Point(pl, 0, new Vec2(54f, 34f), 0.2f);
            Finite(after);
            Assert.Greater(before.Def, 0.3f);
            Assert.AreEqual(0f, after.Def);
            Assert.AreEqual(0f, q.PlayerControl(12));
        }
    }
}
