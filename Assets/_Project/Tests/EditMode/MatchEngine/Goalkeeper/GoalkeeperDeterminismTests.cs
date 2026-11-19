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

        /// <summary>specs/073 T04: 골문 이탈 계획(모든 필드 채움) 쓰기 → 읽기 → 해시·필드 동일, GK 시스템 섹션 왕복 해시 동일.</summary>
        [Test]
        public void StateRoundTrip_WithPlans()
        {
            var p = new KeeperPlan
            {
                Flight = 7, Cross = CrossMode.Punch, CrossReadyTick = 1234, Landing = new Vec2(5.5f, 30.25f), ClaimFailed = true,
                Rush = true, RushPoint = new Vec2(19f, 41f), RushOwned = true,
                OneOnOne = OneOnOneMode.Set, Carrier = 14, SetPoint = new Vec2(4f, 33f), StopTick = 99,
            };
            var w = new RoadToPro.Core.Serialization.StateWriter();
            p.Write(w);
            var q = KeeperPlan.Read(new RoadToPro.Core.Serialization.StateReader(w.ToArray()));
            var ha = StableHasher.Create(); p.AddTo(ref ha);
            var hb = StableHasher.Create(); q.AddTo(ref hb);
            Assert.AreEqual(ha.Value, hb.Value);
            Assert.AreEqual(p.Cross, q.Cross);
            Assert.AreEqual(p.Carrier, q.Carrier);
            Assert.AreEqual(p.StopTick, q.StopTick);
            Assert.AreEqual(KeeperPlan.Empty.Flight, -1);

            var bp = BallTestKit.Params();
            var a = new GoalkeeperSystem(GoalkeeperParams.Defaults, bp, 73UL);
            a.NotifyFlight(); a.NotifyFlight();
            var sw = new RoadToPro.Core.Serialization.StateWriter();
            a.Write(sw);
            var b = new GoalkeeperSystem(GoalkeeperParams.Defaults, bp, 1UL);
            b.Read(new RoadToPro.Core.Serialization.StateReader(sw.ToArray()));
            var h1 = StableHasher.Create(); a.AddTo(ref h1);
            var h2 = StableHasher.Create(); b.AddTo(ref h2);
            Assert.AreEqual(h1.Value, h2.Value);
            Assert.AreEqual(2, b.FlightSeq);
        }

        /// <summary>specs/074 AC-04: PK 전략·손 보유 시계 Write→Read→해시 동일, 시스템 상태 왕복.</summary>
        [Test]
        public void StateRoundTrip_WithPenaltyAndHold()
        {
            var pk = new PenaltyPlan { Active = true, KickDue = 4321, Dir = -1, Lean = -0.73f, Lead = 0.21f, DiveAbs = 4300, Started = true, StartPos = new Vec2(0.4f, 34.1f), OffLine = true, Kicked = true };
            var w = new RoadToPro.Core.Serialization.StateWriter();
            pk.Write(w);
            var pk2 = PenaltyPlan.Read(new RoadToPro.Core.Serialization.StateReader(w.ToArray()));
            var ha = StableHasher.Create(); pk.AddTo(ref ha);
            var hb = StableHasher.Create(); pk2.AddTo(ref hb);
            Assert.AreEqual(ha.Value, hb.Value);
            Assert.AreEqual(pk.DiveAbs, pk2.DiveAbs);
            Assert.AreEqual(pk.Dir, pk2.Dir);

            var hc = new HoldClock { Active = true, StartAbs = 800, DeadlineAbs = 1400, Quick = true };
            var w2 = new RoadToPro.Core.Serialization.StateWriter();
            hc.Write(w2);
            var hc2 = HoldClock.Read(new RoadToPro.Core.Serialization.StateReader(w2.ToArray()));
            var hd = StableHasher.Create(); hc.AddTo(ref hd);
            var he = StableHasher.Create(); hc2.AddTo(ref he);
            Assert.AreEqual(hd.Value, he.Value);
            Assert.AreEqual(hc.DeadlineAbs, hc2.DeadlineAbs);

            var bp = BallTestKit.Params();
            var a = new GoalkeeperSystem(GoalkeeperParams.Defaults, bp, 74UL);
            var sw = new RoadToPro.Core.Serialization.StateWriter();
            a.Write(sw);
            var b = new GoalkeeperSystem(GoalkeeperParams.Defaults, bp, 2UL);
            b.Read(new RoadToPro.Core.Serialization.StateReader(sw.ToArray()));
            var h1 = StableHasher.Create(); a.AddTo(ref h1);
            var h2 = StableHasher.Create(); b.AddTo(ref h2);
            Assert.AreEqual(h1.Value, h2.Value);
        }

        /// <summary>specs/073 AC-05: 스루패스·크로스·1v1 고정 상황 각 100개를 2회 처리 — 결정(선택 RNG 포함)·지연·정지 오차 해시 동일.</summary>
        [Test]
        public void LeaveGoal_Fixtures100x3_TwiceSameHash() => Assert.AreEqual(LeaveGoalHash(), LeaveGoalHash());

        static ulong LeaveGoalHash()
        {
            var p = GoalkeeperParams.Defaults;
            var sweeper = new SweeperModel(p);
            var cross = new CrossClaimModel(p);
            var one = new OneOnOneModel(p);
            var chooser = new KeeperChooser(p);
            var cands = new KeeperCandidate[4];
            var fx = new SeedRng(7350UL);
            var rng = new SeedRng(7351UL);
            var times = new float[30];
            var pos = new Vec3[30];
            var goal = KeeperLeaveKit.Goal;
            var h = StableHasher.Create();
            for (int i = 0; i < 100; i++)
            {
                var k = KeeperLeaveKit.Profile(fx.Range(0f, 100f), fx.Range(0f, 100f), fx.Range(0f, 100f), fx.Range(0f, 100f));
                // 스루패스
                var w = KeeperLeaveKit.World(new Vec2(fx.Range(3f, 20f), fx.Range(28f, 40f)));
                RoadToPro.Tests.EditMode.MatchEngine.Actions.ActionTestKit.Put(w, 12, new Vec2(fx.Range(20f, 40f), fx.Range(20f, 48f)), new Vec2(fx.Range(-8f, 0f), fx.Range(-2f, 2f)));
                var n = KeeperLeaveKit.StraightPath(new Vec2(fx.Range(40f, 55f), fx.Range(25f, 43f)), new Vec2(fx.Range(-16f, -8f), fx.Range(-3f, 3f)), times, pos);
                var g = sweeper.Gate(w, 0, in k, (KeeperRole)(i % 3), goal, 1f, times, pos, n, 0f, 2.5f);
                h.Add(g.Has); h.Add(g.Pass); h.Add(g.Point.X); h.Add(g.Point.Y); h.Add(g.Delta); h.Add(g.Margin);
                cands[0] = new KeeperCandidate { Kind = KeeperActionKind.Position, Utility = p.ChoicePositionUtility };
                var c = 1;
                if (g.Pass) cands[c++] = new KeeperCandidate { Kind = KeeperActionKind.Rush, Point = g.Point, Utility = sweeper.Utility(fx.NextFloat(), 0.05f, sweeper.EmptyXg(g.Point, goal)) };
                h.Add(chooser.Choose(cands, c, default, k.DecisionsN, rng));
                // 크로스
                var cw = KeeperLeaveKit.World(new Vec2(fx.Range(1f, 5f), fx.Range(30f, 38f)));
                for (int s = 12; s < 12 + (int)(fx.NextFloat() * 6f); s++)
                    RoadToPro.Tests.EditMode.MatchEngine.Actions.ActionTestKit.Put(cw, s, new Vec2(fx.Range(3f, 16f), fx.Range(24f, 44f)));
                var landing = new Vec2(fx.Range(2f, 15f), fx.Range(26f, 42f));
                var ce = cross.Evaluate(cw, 0, in k, 1.88f, goal, 1f, landing, fx.Range(0.8f, 2.2f), 2.5f, 0.3f);
                h.Add(ce.Has); h.Add(ce.PClaim); h.Add(ce.PPunch); h.Add(ce.Crowd);
                if (ce.Has)
                {
                    cands[1] = new KeeperCandidate { Kind = KeeperActionKind.Claim, Point = landing, Utility = ce.UClaim };
                    cands[2] = new KeeperCandidate { Kind = KeeperActionKind.Punch, Point = landing, Utility = ce.UPunch };
                    h.Add(chooser.Choose(cands, 3, default, k.DecisionsN, rng));
                }
                h.Add(cross.Delay(in k, rng));
                // 1v1
                var ow = KeeperLeaveKit.World(new Vec2(fx.Range(1f, 5f), fx.Range(31f, 37f)));
                var cpos = new Vec2(fx.Range(8f, 22f), fx.Range(24f, 44f));
                RoadToPro.Tests.EditMode.MatchEngine.Actions.ActionTestKit.Put(ow, 12, cpos, new Vec2(fx.Range(-7f, -2f), 0f));
                var ball = cpos + new Vec2(-fx.Range(0f, 8f), 0f);
                var oe = one.Evaluate(ow, 0, in k, goal, 1f, 12, ball, fx.NextFloat() < 0.5f);
                h.Add(oe.Has); h.Add(oe.Smother); h.Add(oe.SetPoint.X); h.Add(oe.SetPoint.Y); h.Add(oe.TimeToShot);
                h.Add(OneOnOneModel.StopError(in k, rng));
            }
            return h.Value;
        }

        /// <summary>specs/073 AC-05: 010 실경기(팀 0 스위퍼·팀 1 전통) 같은 시드 2회 → 로그·상태 해시 동일, 경기 중 스냅샷 복원 뒤 이어 돌려도 동일 (계획 포함).</summary>
        [Test]
        public void LeaveGoal_LiveMatch_TwiceSameHash_SnapshotRestore()
        {
            MatchSim Make()
            {
                var sim = new MatchSim(RoadToPro.Tests.EditMode.MatchEngine.Tactics.ShapeTestKit.Setup(seed: 7301UL), RoadToPro.Tests.EditMode.MatchEngine.Tactics.ShapeTestKit.TuningWithShape());
                sim.Context.Keepers.Roles[0] = KeeperRole.Sweeper;
                sim.Context.Keepers.Roles[1] = KeeperRole.Traditional;
                return sim;
            }
            var a = Make();
            a.RunUntilTick(30_000);
            var b = Make();
            b.RunUntilTick(30_000);
            Assert.AreEqual(a.Log.Hash, b.Log.Hash);
            Assert.AreEqual(a.StateHash(), b.StateHash());
            var snap = a.Capture();
            var fork = a.Fork();
            fork.Restore(snap, a.Log, a.Checkpoints);
            Assert.AreEqual(a.StateHash(), fork.StateHash(), "복원 직후");
            a.StepTicks(6_000);
            fork.StepTicks(6_000);
            Assert.AreEqual(a.StateHash(), fork.StateHash(), "복원 뒤 6,000틱");
            Assert.AreEqual(a.Log.Hash, fork.Log.Hash);
        }

        /// <summary>specs/074 AC-04 ①: 같은 시드 승부차기 10라운드(킥 20) 2회 — GK 방향·조기 시각·기울기·결과·기억(방향 합·다이빙 수) 해시 동일.</summary>
        [Test]
        public void Shootout10Rounds_TwiceSameHash() => Assert.AreEqual(ShootoutHash(), ShootoutHash());

        static ulong ShootoutHash()
        {
            var sim = PenaltyShootoutSimTests.Ready(10.5f);
            var ctx = sim.Context;
            var judge = new RoadToPro.MatchEngine.Match.Flow.SimPenaltyJudge();
            var h = StableHasher.Create();
            for (int r = 0; r < 10; r++)
                for (int t = 0; t < 2; t++)
                {
                    var kicker = ctx.Setup.Team(t).Starters[1 + r];
                    var o = judge.Kick(ctx, t, kicker, ctx.Setup.Team(1 - t).Starters[0]);
                    h.Add(o.Scored); h.Add(o.KeeperDir); h.Add(o.Lead); h.Add(o.Lean);
                    var m = judge.Memory(1 - t);
                    h.Add(m.Balance); h.Add(m.Dives);
                }
            return h.Value;
        }

        /// <summary>specs/074 AC-04 ②: 같은 시드 배급 픽스처 1,000회(평가·선택·롱 조준 오차) 2회 해시 동일.</summary>
        [Test]
        public void Distribution_Fixture1000_TwiceSameHash() => Assert.AreEqual(DistributionHash(), DistributionHash());

        static ulong DistributionHash()
        {
            var p = GoalkeeperParams.Defaults;
            var ap = RoadToPro.Tests.EditMode.MatchEngine.Actions.ActionTestKit.P;
            var bp = RoadToPro.Tests.EditMode.MatchEngine.Actions.ActionTestKit.Ball;
            var m = new DistributionModel(p);
            var layout = new SeedRng(740UL);
            var rng = new SeedRng(741UL);
            var gk = new Vec2(6f, 34f);
            var h = StableHasher.Create();
            for (int i = 0; i < 1000; i++)
            {
                var w = RoadToPro.Tests.EditMode.MatchEngine.Actions.ActionTestKit.World();
                w.PrepareRoll(ap, bp);
                RoadToPro.Tests.EditMode.MatchEngine.Actions.ActionTestKit.Put(w, 0, gk);
                for (int sl = 1; sl < 22; sl++) RoadToPro.Tests.EditMode.MatchEngine.Actions.ActionTestKit.Put(w, sl, new Vec2(layout.Range(8f, 100f), layout.Range(3f, 65f)));
                var c = new DistributionContext { Source = (DistributionSource)(i % 3), Quick = i % 4 == 0, HighPress = i % 5 == 0, Directness = 0.5f, DecisionsN = 0.5f };
                var n = m.Evaluate(w, ap, bp, 0, gk, 0.3f, in c);
                var pick = n == 0 ? m.Forced(w, bp, 0, gk, c.Source) : m.Candidate(m.Choose(in c, rng));
                var aim = DistributionExecution.PerturbAim(gk, pick.Aim, DistributionExecution.SigmaAngleDeg(p, m, pick.Kind, 0.5f), DistributionExecution.SigmaDist(p, 0.5f), rng, out _, out _);
                h.Add(n); h.Add((byte)pick.Kind); h.Add(pick.Target); h.Add(pick.Eu); h.Add(aim.X); h.Add(aim.Y);
            }
            return h.Value;
        }

        /// <summary>specs/074 AC-04 ③: PK·배급·손 보유가 들어간 010 실경기 같은 시드 2회 + PK 직후 스냅샷 복원 뒤 6,000틱 해시 동일.</summary>
        [Test]
        public void PenaltyDistribution_LiveMatch_TwiceSameHash_SnapshotRestore()
        {
            MatchSim Make()
            {
                var script = new RoadToPro.MatchEngine.Match.Input.RefereeScript();
                script.Add(new RoadToPro.MatchEngine.Match.Input.RefereeAction(3_000, RoadToPro.MatchEngine.Match.Input.RefereeActionKind.AwardPenalty, 0));
                script.Add(new RoadToPro.MatchEngine.Match.Input.RefereeAction(12_000, RoadToPro.MatchEngine.Match.Input.RefereeActionKind.AwardPenalty, 1));
                return new MatchSim(RoadToPro.Tests.EditMode.MatchEngine.Tactics.ShapeTestKit.Setup(seed: 7401UL), RoadToPro.Tests.EditMode.MatchEngine.Tactics.ShapeTestKit.TuningWithShape(), new MatchSimOptions { Referee = script });
            }
            var a = Make();
            a.RunUntilTick(12_030); // 두 번째 PK 준비 중
            var b = Make();
            b.RunUntilTick(12_030);
            Assert.AreEqual(a.Log.Hash, b.Log.Hash);
            Assert.AreEqual(a.StateHash(), b.StateHash());
            var snap = a.Capture();
            var fork = a.Fork();
            fork.Restore(snap, a.Log, a.Checkpoints);
            Assert.AreEqual(a.StateHash(), fork.StateHash(), "복원 직후");
            a.StepTicks(6_000);
            fork.StepTicks(6_000);
            Assert.AreEqual(a.StateHash(), fork.StateHash(), "복원 뒤 6,000틱");
            Assert.AreEqual(a.Log.Hash, fork.Log.Hash);
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
