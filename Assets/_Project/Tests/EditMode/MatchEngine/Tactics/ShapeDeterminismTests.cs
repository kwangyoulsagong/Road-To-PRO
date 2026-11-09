using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public sealed class NullDiagnostics : IShapeDiagnostics
    {
        public int Calls;
        public void OnTeam(int team, long tick, TeamShapeEngine engine) => Calls++;
    }

    [Category("Determinism")]
    public class ShapeDeterminismTests
    {
        [Test]
        public void FullMatch_SameHashes()
        {
            var a = ShapeTestKit.Sim().RunToEnd();
            var b = ShapeTestKit.Sim().RunToEnd();
            Assert.That(a.EventLogHash, Is.EqualTo(b.EventLogHash));
            Assert.That(a.FinalStateHash, Is.EqualTo(b.FinalStateHash));
            Assert.That(CheckpointLog.FindFirstDivergence(a.Checkpoints, b.Checkpoints), Is.Null);
        }

        [Test]
        public void DifferentSeed_DifferentResult()
        {
            var a = ShapeTestKit.Sim(setup: ShapeTestKit.Setup(seed: 1UL));
            var b = ShapeTestKit.Sim(setup: ShapeTestKit.Setup(seed: 2UL));
            a.StepTicks(300);
            b.StepTicks(300);
            Assert.That(a.StateHash(), Is.Not.EqualTo(b.StateHash()));
        }

        [Test]
        public void TieFixture_Stable()
        {
            int[] Run()
            {
                var e = ShapeTestKit.Engine("4-4-2");
                var ins = ShapeTestKit.Ins();
                var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
                for (int i = 0; i < 11; i++) f.Own[i] = new Vec2(.5f, .5f); // 모두 같은 자리 → 비용 동점
                ShapeTestKit.Settle(e, f, in ins, 12);
                var r = new int[11];
                for (int i = 1; i < 11; i++) r[i] = e.FormationSlotOf(i);
                return r;
            }
            Assert.That(Run(), Is.EqualTo(Run()));
        }

        [Test]
        public void Snapshot_RoundTrip()
        {
            var o = new MatchSimOptions { Checkpoints = CheckpointMode.All };
            var a = ShapeTestKit.Sim(o);
            a.RunToEnd();
            var snap = a.NearestSnapshot(18_000);
            var fork = a.Fork();
            fork.Restore(snap, a.Log, a.Checkpoints);
            var r = fork.RunToEnd();
            Assert.That(r.EventLogHash, Is.EqualTo(a.Result.EventLogHash));
            Assert.That(r.FinalStateHash, Is.EqualTo(a.Result.FinalStateHash));
        }

        [Test]
        public void TacticsChange_ReproducedByFork()
        {
            var o = new MatchSimOptions { Checkpoints = CheckpointMode.All };
            var a = ShapeTestKit.Sim(o);
            a.StepTicks(600);
            a.RequestInstruction(0, InstructionId.LineHeight, .9f);
            a.RequestFormation(1, ShapeTestKit.Catalog.IndexOf("5-3-2"));
            a.StepTicks(3000);
            var fork = a.Fork();
            fork.RunUntilTick(3600);
            Assert.That(fork.StateHash(), Is.EqualTo(a.StateHash()));
        }

        [TestCase("4-3-3")]
        [TestCase("3-5-2")]
        public void PointSymmetry(string id)
        {
            var ins = ShapeTestKit.Ins();
            var a = ShapeTestKit.Engine(id, team: 0);
            var b = ShapeTestKit.Engine(id, team: 1);
            var fa = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            var fb = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            ShapeTestKit.Settle(a, fa, in ins, 30);
            ShapeTestKit.Settle(b, fb, in ins, 30);
            for (int i = 0; i < 11; i++)
            {
                var wa = PitchCoords.FromPerspective(a.Target(i), TeamSide.Home, 0);
                var wb = PitchCoords.FromPerspective(b.Target(i), TeamSide.Away, 0);
                Assert.That(((1f - wb.X) - wa.X) * 105f, Is.EqualTo(0f).Within(1e-4f), $"x {i}");
                Assert.That(((1f - wb.Y) - wa.Y) * 68f, Is.EqualTo(0f).Within(1e-4f), $"y {i}");
            }
        }

        [Test]
        public void HalfTimeFlip_Symmetric()
        {
            var ins = ShapeTestKit.Ins();
            var e = ShapeTestKit.Engine("4-3-3");
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            ShapeTestKit.Settle(e, f, in ins, 30);
            var before = e.Target(7);
            // 후반: 관점 좌표 입력이 같으면 결과도 같고, 전반 보간 상태가 남지 않는다
            f.Period = 1;
            f.Phase = TeamPhase.BuildUp;
            ShapeTestKit.Settle(e, f, in ins, 1);
            var fresh = ShapeTestKit.Engine("4-3-3");
            var g = ShapeTestKit.Frame(TeamPhase.BuildUp, new Vec2(.5f, .5f));
            g.Period = 1;
            g.Tick = f.Tick - 1;
            ShapeTestKit.Settle(fresh, g, in ins, 1);
            Assert.That(e.SlotTarget(7).X, Is.EqualTo(fresh.SlotTarget(7).X).Within(1e-6f), "하프 전환 때 보간 초기화");
            Assert.That(before, Is.Not.EqualTo(default(Vec2)));
        }

        [Test]
        public void DiagnosticsOnOff_SameHashes()
        {
            var diag = new NullDiagnostics();
            var on = ShapeTestKit.Sim(new MatchSimOptions { ShapeDiagnostics = diag, ShapeTelemetry = new CountingTelemetry() });
            var off = ShapeTestKit.Sim();
            on.StepTicks(3000);
            off.StepTicks(3000);
            Assert.That(diag.Calls, Is.EqualTo(6000));
            Assert.That(on.Log.Hash, Is.EqualTo(off.Log.Hash));
            Assert.That(on.StateHash(), Is.EqualTo(off.StateHash()));
        }
    }
}
