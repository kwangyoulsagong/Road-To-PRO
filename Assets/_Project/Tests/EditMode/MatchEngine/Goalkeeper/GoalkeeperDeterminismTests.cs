using System;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Ball;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/011 AC-16·20 결정론 · 스냅샷 (T13).</summary>
    [Category("Determinism")]
    public class GoalkeeperDeterminismTests
    {
        static ulong FixtureHash()
        {
            var p = GoalkeeperParams.Defaults;
            var pos = new KeeperPositioner(p, new GoalGrid(p));
            var m = new SaveModel(p, pos);
            var bp = BallTestKit.Params();
            m.Bind(bp);
            var k = KeeperProfile.From(p, KeeperAttributes.Uniform(10.5f));
            var goal = new Vec2(0f, 34f);
            var sctx = new SaveContext { Goal = goal, DirIn = 1f };
            var fx = new SeedRng(404UL);
            var rng = new SeedRng(405UL);
            var ev = new BallEventBuffer(16);
            var h = StableHasher.Create();
            for (int i = 0; i < 100; i++)
            {
                var origin = new Vec3(fx.Range(8f, 30f), 34f + fx.Range(-15f, 15f), 0.11f);
                var target = pos.Target(new PositionInput { Goal = goal, DirIn = 1f, Ball = new Vec2(origin.X, origin.Y), LineHeight = 30f }, k);
                h.Add(target.X); h.Add(target.Y);
                var s = m.AimedShot(origin, (int)(fx.NextFloat() * GoalGrid.Cells), fx.Range(16f, 32f), goal, bp);
                if (!m.Predict(in s, bp, goal, 1f)) continue;
                var j = m.Evaluate(new KeeperState { Position = target }, in k, in sctx);
                var o = m.Resolve(in j, in k, in sctx, rng);
                h.Add(j.PReach); h.Add(j.PCatch); h.Add((byte)o.Kind); h.Add(o.ReboundVelocity.X); h.Add(o.ReboundVelocity.Y); h.Add(o.ReboundVelocity.Z);
                var b = BallState.InAir(j.ContactPoint, o.ReboundVelocity, Vec3.Zero);
                for (int step = 0; step < 100 && b.Phase == BallPhase.Air; step++) { ev.Clear(); BallStepper.Step(ref b, bp, ev, out _); }
                h.Add(b.Position.X); h.Add(b.Position.Y); h.Add(b.Position.Z);
            }
            return h.Value;
        }

        [Test]
        public void ShotFixtures100_TwoRuns_SameHash() => Assert.AreEqual(FixtureHash(), FixtureHash());

        static ulong KeeperEventHash(MatchSim sim)
        {
            var h = StableHasher.Create();
            for (int i = 0; i < sim.Log.Count; i++)
            {
                ref readonly var e = ref sim.Log[i];
                var k = (int)e.Kind;
                if ((k >= 300 && k < 400) || e.Kind == SimEventKind.Goal) e.AddTo(ref h);
            }
            return h.Value;
        }

        [Test]
        public void Match90_TwoRuns_SameKeeperEventHash()
        {
            var a = Sim(9011UL);
            a.RunUntilTick(60_000);
            var b = Sim(9011UL);
            b.RunUntilTick(60_000);
            Assert.AreEqual(KeeperEventHash(a), KeeperEventHash(b));
            Assert.AreEqual(a.Log.Hash, b.Log.Hash);
            Assert.AreEqual(a.StateHash(), b.StateHash());
        }

        [Test]
        public void Snapshot_RoundTrip_MidDive()
        {
            var opts = new MatchSimOptions();
            var sim = Scripted(ctx =>
            {
                if (ctx.Tick == 0)
                {
                    ctx.Flow.OnRestartTaken();
                    for (int s = 1; s < Squad.Slots; s++) if (ctx.IsSlotActive(s)) ctx.Kinematics.Place(s, new Vec2(95f, 4f + 2.8f * s), new Vec2(-1f, 0f));
                    ctx.Kinematics.Place(0, new Vec2(2f, 34f), new Vec2(1f, 0f));
                }
                if (ctx.Tick == 5)
                {
                    ctx.Ball = BallState.InAir(new Vec3(16f, 28f, 0.11f), new Vec3(-21.5f, 11.5f, 2.2f), Vec3.Zero);
                    ctx.LastTouchTeam = 1; ctx.LastTouchSlot = 11; ctx.LastKickSlot = 11;
                    ctx.Keepers.NotifyFlight();
                }
            }, options: opts);
            for (int i = 0; i < 30 && !sim.Context.Keepers.IsBusy(0); i++) sim.StepTicks(1);
            Assume.That(sim.Context.Keepers.IsBusy(0), "다이빙 중 스냅샷");
            var snap = sim.Capture();
            var fork = sim.Fork();
            fork.Restore(snap, sim.Log, sim.Checkpoints);
            Assert.AreEqual(sim.StateHash(), fork.StateHash(), "복원 직후");
            sim.StepTicks(40);
            fork.StepTicks(40);
            Assert.AreEqual(sim.StateHash(), fork.StateHash(), "다이빙·일어나기 뒤");
            Assert.AreEqual(sim.Log.Hash, fork.Log.Hash);
        }

        sealed class Sink : IKeeperDiagnostics
        {
            public int Candidates, Targets;
            public void OnRiskCandidate(int team, Vec2 point, float risk, float objective) => Candidates++;
            public void OnTarget(int team, Vec2 target, Vec2 bisectorFrom, Vec2 bisectorTo) => Targets++;
        }

        [Test]
        public void Diagnostics_OnOff_SameHashes_ZeroAllocWhenOff()
        {
            var off = Sim(9012UL);
            var on = Sim(9012UL);
            var sink = new Sink();
            on.Context.Keepers.Diagnostics = sink;
            off.RunUntilTick(9_000);
            on.RunUntilTick(9_000);
            Assert.Greater(sink.Targets, 0);
            Assert.Greater(sink.Candidates, 0);
            Assert.AreEqual(off.Log.Hash, on.Log.Hash, "이벤트 로그");
            Assert.AreEqual(off.StateHash(), on.StateHash(), "상태");

            // 꺼진 경로 할당 0 (위치 탐색 반복)
            var p = GoalkeeperParams.Defaults;
            var pos = new KeeperPositioner(p, new GoalGrid(p));
            var k = KeeperProfile.From(p, KeeperAttributes.Uniform(10.5f));
            var inp = new PositionInput { Goal = new Vec2(0f, 34f), DirIn = 1f, Ball = new Vec2(18f, 30f), LineHeight = 30f };
            pos.Target(in inp, in k);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { inp.Ball = new Vec2(10f + i * 0.02f, 30f); pos.Target(in inp, in k, 0, null); }
            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
