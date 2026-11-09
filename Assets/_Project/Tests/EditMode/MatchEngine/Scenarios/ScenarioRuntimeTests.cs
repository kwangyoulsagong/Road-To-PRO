using System.Linq;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Match.Stages.StandIn;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Scenarios;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;
using static RoadToPro.Tests.EditMode.MatchEngine.Scenarios.ScenarioTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Scenarios
{
    public class ScenarioRuntimeTests
    {
        const string Only = ",\"상태\":\"후보\"";
        static ScenarioData D() => DataOf(Lib(Mini("RUN", RunCast, extra: Only)));

        static void StealBall(MatchSim sim, int team)
        {
            var ctx = sim.Context;
            var slot = (1 - team) * Squad.SlotsPerTeam + 5;
            var p = ctx.SlotState(slot).Position;
            BallCommands.TakeControl(ref ctx.Ball, slot, new Vec3(p.X, p.Y, ctx.BallParams.Radius), ctx.BallEvents);
        }

        [Test]
        public void CastReleased_SameTickShapeTarget()
        {
            var sim = Forced(D(), "RUN");
            var rt = Runtime(sim);
            sim.StepTicks(3);
            Assert.That(rt.Run(0).Active, Is.True);
            var slots = rt.Run(0).Slot.Take(3).Where(s => s >= 0).ToArray();
            Assert.That(slots.All(rt.IsCast), Is.True);

            StealBall(sim, 0);
            sim.StepTicks(1);
            Assert.That(rt.Run(0).Active, Is.False);
            var board = sim.Context.Board;
            foreach (var s in slots)
            {
                Assert.That(rt.IsCast(s), Is.False);
                Assert.That(board.HasShapeTarget(s), Is.True, $"슬롯 {s}는 끝난 틱에 형태 목표를 받는다");
            }
        }

        [Test]
        public void NoTeleportOnBreak()
        {
            var sim = Forced(D(), "RUN");
            var ctx = sim.Context;
            sim.StepTicks(3);
            StealBall(sim, 0);
            for (int t = 0; t < 30; t++)
            {
                var before = Enumerable.Range(0, Squad.Slots).Select(s => ctx.IsSlotActive(s) ? ctx.SlotState(s).Position : default).ToArray();
                sim.StepTicks(1);
                for (int s = 0; s < Squad.Slots; s++)
                {
                    if (!ctx.IsSlotActive(s)) continue;
                    var limit = ctx.Kinematics.Profile(s).MaxSpeed * 0.1f + 1e-3f;
                    Assert.That((ctx.SlotState(s).Position - before[s]).Length(), Is.LessThanOrEqualTo(limit), $"슬롯 {s} 틱 {t}");
                }
            }
        }

        [Test]
        public void TackleBreak_EndsAndOpponentActs()
        {
            var sim = Forced(D(), "RUN");
            sim.StepTicks(3);
            StealBall(sim, 0);
            sim.StepTicks(1);
            Assert.That(LastEndReason(sim), Is.EqualTo((int)ScenarioEndReason.Tackle));
            var board = sim.Context.Board;
            Assert.That(board.DecidedCount, Is.EqualTo(sim.Context.DecisionCount), "행동 없는 선수 0");
            for (int i = 0; i < board.DecidedCount; i++)
                Assert.That(board.GetDecision(board.DecidedAt(i)).Kind, Is.Not.EqualTo(DecisionKind.Keep));
        }

        [Test]
        public void LossAndShotSameTick_OneOutcome()
        {
            var sim = Forced(D(), "RUN");
            sim.StepTicks(3);
            StealBall(sim, 0);
            sim.StepTicks(2);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.ScenarioEnd), Is.EqualTo(1));
        }

        [Test]
        public void RequiredCastLost_Ends()
        {
            var probe = Forced(D(), "RUN");
            var at = probe.Context.Tick + 3;
            var id = probe.Context.PlayerAtSlot(Runtime(probe).Run(0).Slot[1]);

            var script = new RoadToPro.MatchEngine.Match.Input.RefereeScript();
            script.Add(new RoadToPro.MatchEngine.Match.Input.RefereeAction(at, RoadToPro.MatchEngine.Match.Input.RefereeActionKind.SendOff, id));
            var sim = Forced(D(), "RUN", options: new MatchSimOptions { Referee = script });
            sim.RunUntilTick(at + 2);
            Assert.That(Runtime(sim).Run(0).Active, Is.False);
            Assert.That(LastEndReason(sim), Is.EqualTo((int)ScenarioEndReason.CastLost));
        }

        [Test]
        public void RequiredOpponentRoleTaken_EndsThatScenario()
        {
            var attack = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\",\"distanceM\":40}]},{\"role\":\"x\",\"opponent\":true,\"required\":true,\"steps\":[{\"do\":\"track_st\"}]}]";
            var press = "[{\"role\":\"p\",\"steps\":[{\"t\":0,\"action\":\"engage\"}]}]";
            var d = DataOf(Lib(Mini("ATK", attack, extra: Only), Mini("DEF-X", press, category: "DEF")));
            var sim = Forced(d, "ATK");
            var rt = Runtime(sim);
            for (int i = 0; i < 40 && !rt.Run(1).Active && rt.Run(0).Active; i++) sim.StepTicks(1);
            Assert.That(rt.Run(0).Active && rt.Run(1).Active, Is.False, "필수 상대 배역을 뺏기면 공격 시나리오는 끝난다");
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (!rt.IsCast(s)) continue;
                Assert.That(rt.Run(Squad.TeamOfSlot(s)).Active, Is.True, $"슬롯 {s}");
            }
        }

        /// <summary>교체는 데드볼에만 실행되므로 재개를 기다리는 세트피스에서 확인한다.</summary>
        [Test]
        public void OptionalCastLost_Continues()
        {
            const string cast = "[{\"role\":\"t\",\"taker\":true,\"positions\":[\"any\"],\"steps\":[{\"t\":0,\"action\":\"deliver\",\"target\":\"near_post_6yd\"}]}," +
                "{\"role\":\"o\",\"required\":false,\"steps\":[{\"t\":5,\"action\":\"attack_zone\",\"zone\":\"far_post_6yd\"}]}]";
            var d = DataOf(Lib(Mini("SP-OPT", cast, category: "SP", extra: Only)));
            var o = new MatchSimOptions { ScenarioForce = new ScenarioForceRequest { Id = "SP-OPT", Team = 0, Allowed = true, FromTick = 200 } };
            var sim = Sim(d, o);
            var ctx = sim.Context;
            var rt = Runtime(sim);
            sim.StepTicks(200);
            ctx.Flow.BeginRestart(RestartKind.Corner, 0, new Vec2(ctx.AttackDir(0) > 0 ? ctx.Pitch.LengthM - 0.5f : 0.5f, 0.5f));
            for (int i = 0; i < 20 && !rt.Run(0).Active; i++) sim.StepTicks(1);
            var run = rt.Run(0);
            Assert.That(run.Active, Is.True);
            var slot = run.Slot[1];
            var id = ctx.PlayerAtSlot(slot);
            sim.RequestSubstitution(0, id, 12);
            sim.StepTicks(3);
            Assert.That(ctx.PlayerAtSlot(slot), Is.Not.EqualTo(id), "교체 실행");
            Assert.That(run.Active, Is.True, "선택 배역이 나가도 시나리오는 계속된다");
            Assert.That(run.Slot[1], Is.EqualTo(-1));
            Assert.That(rt.IsCast(slot), Is.False);
        }

        [Test]
        public void ChainLimit_Fallback()
        {
            const string quick = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"dummy\"}]}]";
            var d = DataOf(Lib(
                Mini("A", quick, branches: "[{\"at\":\"c.1\",\"success\":\"B\"}]", extra: Only),
                Mini("B", quick, branches: "[{\"at\":\"c.1\",\"success\":\"A\"}]", extra: Only)));
            var sim = Forced(d, "A");
            for (int i = 0; i < 30 && Runtime(sim).Run(0).Active; i++) sim.StepTicks(1);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.ScenarioChain), Is.EqualTo(6));
            Assert.That(LastEndReason(sim), Is.EqualTo((int)ScenarioEndReason.ChainLimit));
        }

        [Test]
        public void BranchTargetNotLoaded_Fallback()
        {
            const string quick = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"dummy\"}]}]";
            var s = Sources(withCatalog: true).Add("a.json", Mini("A", quick, branches: "[{\"at\":\"c.1\",\"success\":\"FT02\"}]", extra: Only));
            var sim = Forced(DataOf(ScenarioLibrary.Load(s)), "A");
            for (int i = 0; i < 10 && Runtime(sim).Run(0).Active; i++) sim.StepTicks(1);
            Assert.That(LastEndReason(sim), Is.EqualTo((int)ScenarioEndReason.BranchFallback));
        }

        [Test]
        public void LossTarget_RecordsPossessionLost()
        {
            const string stuck = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"dummy\"}]}]";
            var sim = Forced(DataOf(Lib(Mini("A", stuck, branches: "[{\"at\":\"c.1\",\"success\":\"CT_opp\"}]", extra: Only))), "A");
            for (int i = 0; i < 10 && Runtime(sim).Run(0).Active; i++) sim.StepTicks(1);
            Assert.That(LastEndReason(sim), Is.EqualTo((int)ScenarioEndReason.PossessionLost));
        }

        /// <summary>리뷰 R4: 다른 배역으로 점프해도 끝난 배역이 다시 분기하지 않는다.</summary>
        [Test]
        public void JumpToOtherRole_DoesNotRefire()
        {
            const string cast = "[{\"role\":\"a\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"dummy\"}]},{\"role\":\"b\",\"steps\":[{\"do\":\"hold_width\"}]}]";
            var sim = Forced(DataOf(Lib(Mini("A", cast, branches: "[{\"at\":\"a.1\",\"success\":{\"do\":{\"role\":\"b\",\"do\":\"drop\"}}}]", extra: Only))), "A");
            sim.StepTicks(10);
            Assert.That(MatchTestKit.Events(sim, SimEventKind.ScenarioStepEnd).Count(e => e.A == 0), Is.EqualTo(1));
        }

        [Test]
        public void Timeout_EndsWithReason()
        {
            const string stuck = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"take_on\",\"distanceM\":120}]}]";
            var sim = Forced(DataOf(Lib(Mini("A", stuck, extra: Only))), "A");
            var run = Runtime(sim).Run(0);
            for (int i = 0; i < 80 && run.Active; i++) sim.StepTicks(1);
            Assert.That(run.Active, Is.False);
            Assert.That(LastEndReason(sim), Is.EqualTo((int)ScenarioEndReason.StepTimeout).Or.EqualTo((int)ScenarioEndReason.Tackle).Or.EqualTo((int)ScenarioEndReason.BallDead));
        }

        [Test]
        public void HalfTimeEnds_NoCarryOver()
        {
            var sim = Forced(D(), "RUN");
            var ctx = sim.Context;
            var rt = Runtime(sim);
            for (int i = 0; i < 40 && ctx.Flow.Period == 0; i++) sim.StepTicks(1);
            sim.RunUntilTick(ctx.Flow.PeriodLength(0) > 0 ? ctx.Flow.PeriodLength(0) + 10 : 28000);
            Assert.That(rt.Run(0).Active && rt.Run(0).StartTick < ctx.Flow.PeriodStartTick, Is.False);
        }

        [Test]
        public void Pass_Completes_ToReceiver()
        {
            const string cast = "[{\"role\":\"a\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"pass_short\",\"to\":\"b\"}]}," +
                "{\"role\":\"b\",\"steps\":[{\"do\":\"receive\"}]}]";
            var sim = Forced(DataOf(Lib(Mini("P", cast, extra: Only))), "P");
            var run = Runtime(sim).Run(0);
            for (int i = 0; i < 120 && run.Active; i++) sim.StepTicks(1);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.ScenarioEnd), Is.EqualTo(1));
            var steps = MatchTestKit.Events(sim, SimEventKind.ScenarioStepEnd);
            Assert.That(steps.Count, Is.GreaterThanOrEqualTo(1));
        }
    }

    public class ScenarioMirrorTests
    {
        const string Cast = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\",\"lane\":\"W-L\"}]}," +
            "{\"role\":\"n\",\"steps\":[{\"do\":\"near_post_run\"}]},{\"role\":\"o\",\"steps\":[{\"do\":\"overlap\"}]},{\"role\":\"z\",\"steps\":[{\"do\":\"sprint_to\",\"zone\":\"HS-L\"}]}]";

        static MatchSim Setup(string id, ScenarioData d, bool flip)
        {
            var force = new ScenarioForceRequest { Id = id, Team = 0, Allowed = true, FromTick = long.MaxValue };
            var sim = Sim(d, new MatchSimOptions { ScenarioForce = force });
            var ctx = sim.Context;
            sim.StepTicks(600);
            for (int i = 0; i < 2000 && !ctx.Flow.InPlay; i++) sim.StepTicks(1);
            float Y(float y) => flip ? ctx.Pitch.WidthM - y : y;
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (!ctx.IsSlotActive(s)) continue;
                var x = 20f + (s % 11) * 7f + s * s * 0.07f; // 공까지 거리 동점이 없게
                var y = 6f + (s * 2.7f) % 50f;
                ctx.Kinematics.Place(s, new Vec2(x + (s >= 11 ? 3f : 0f), Y(y)), new Vec2(ctx.AttackDir(Squad.TeamOfSlot(s)), 0f));
            }
            var p = ctx.SlotState(5).Position;
            BallCommands.TakeControl(ref ctx.Ball, 5, new Vec3(p.X, p.Y, ctx.BallParams.Radius), ctx.BallEvents);
            ctx.PossessionStartTick = ctx.Tick;
            force.FromTick = ctx.Tick;
            sim.StepTicks(2);
            Assert.That(Runtime(sim).Run(0).Active, Is.True);
            return sim;
        }

        [Test]
        public void CastTargets_Symmetric()
        {
            var d = DataOf(Lib(Mini("A", Cast, extra: ",\"상태\":\"후보\""), Mini("M", Cast, extra: ",\"mirror\":true,\"상태\":\"후보\"")));
            var a = Setup("A", d, false);
            var m = Setup("M", d, true);
            var ra = Runtime(a).Run(0);
            var rm = Runtime(m).Run(0);
            for (int r = 0; r < 4; r++)
            {
                Assert.That(rm.Slot[r], Is.EqualTo(ra.Slot[r]), $"배역 {r}");
                Assert.That(rm.State[r], Is.EqualTo(ScenarioRun.Running));
                Assert.That((rm.Target[r].X - ra.Target[r].X) * 105f, Is.EqualTo(0f).Within(0.01f), $"x {r}");
                Assert.That(((1f - rm.Target[r].Y) - ra.Target[r].Y) * 68f, Is.EqualTo(0f).Within(0.01f), $"y {r}");
            }
        }
    }

    public class ScenarioSetPieceTests
    {
        [Test]
        public void CornerSequence_LogOrder_NonCastUntouched()
        {
            var d = DataOf(Samples());
            var o = new MatchSimOptions { ScenarioForce = new ScenarioForceRequest { Id = "S-CK-01", Team = 0, Allowed = true, FromTick = 200 } };
            var sim = Sim(d, o);
            var ctx = sim.Context;
            var rt = Runtime(sim);
            sim.StepTicks(200);
            var spot = new Vec2(ctx.AttackDir(0) > 0 ? ctx.Pitch.LengthM - 0.5f : 0.5f, 0.5f);
            ctx.Flow.BeginRestart(RestartKind.Corner, 0, spot);
            for (int i = 0; i < 20 && !rt.Run(0).Active; i++) sim.StepTicks(1);
            Assert.That(rt.Run(0).Active, Is.True);

            sim.StepTicks(1);
            var board = ctx.Board;
            for (int s = 1; s < Squad.SlotsPerTeam; s++)
                if (!rt.IsCast(s) && s != ctx.Flow.RestartTaker)
                    Assert.That(board.HasShapeTarget(s) && board.GetDecision(s).Kind == DecisionKind.HoldShape, Is.True, $"배역 아닌 슬롯 {s}는 형태를 따른다");

            for (int i = 0; i < 400 && rt.Run(0).Active; i++) sim.StepTicks(1);
            int Index(SimEventKind k, int a = int.MinValue)
            {
                for (int i = 0; i < sim.Log.Count; i++)
                    if (sim.Log[i].Kind == k && (a == int.MinValue || sim.Log[i].A == a)) return i;
                return -1;
            }
            var start = Index(SimEventKind.ScenarioStart);
            var resume = Index(SimEventKind.ScenarioResume);
            var flick = Index(SimEventKind.ScenarioStepEnd, 102);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(resume, Is.GreaterThan(start));
            if (flick >= 0) Assert.That(flick, Is.GreaterThan(resume), "니어포스트 플릭은 재개 뒤");
            else Assert.That(LastEndReason(sim), Is.Not.EqualTo((int)ScenarioEndReason.Completed), "플릭 전에 끊겼어야 한다");
        }
    }

    public class ScenarioForceTests
    {
        [Test]
        public void Marked()
        {
            var sim = Forced(DataOf(Lib(Mini("A", CarrierOnly, extra: ",\"상태\":\"후보\""))), "A");
            var e = MatchTestKit.Events(sim, SimEventKind.ScenarioStart);
            Assert.That(e.Single().B & 1, Is.EqualTo(1));
        }

        static MatchSim Run(ScenarioData d, string id, bool allowed)
        {
            var o = new MatchSimOptions { ScenarioForce = new ScenarioForceRequest { Id = id, Team = 0, Allowed = allowed } };
            var sim = Sim(d, o);
            sim.StepTicks(1200);
            return sim;
        }

        [Test]
        public void ReleaseRejected()
        {
            var sim = Run(DataOf(Lib(Mini("A", CarrierOnly, extra: ",\"상태\":\"후보\""))), "A", false);
            var e = MatchTestKit.Events(sim, SimEventKind.ScenarioForceRejected);
            Assert.That(e.Single().B, Is.EqualTo(1));
            Assert.That(MatchTestKit.Count(sim, SimEventKind.ScenarioStart), Is.EqualTo(0));
        }

        [Test]
        public void MissingCast_Reason()
        {
            var cast = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\"}]},{\"role\":\"t\",\"positions\":[\"GK\"],\"opponent\":false,\"steps\":[{\"do\":\"drop\"}]},{\"role\":\"t2\",\"positions\":[\"GK\"],\"steps\":[{\"do\":\"drop\"}]}]";
            var sim = Run(DataOf(Lib(Mini("A", cast, extra: ",\"상태\":\"후보\""))), "A", true);
            var e = MatchTestKit.Events(sim, SimEventKind.ScenarioForceRejected);
            Assert.That(e.Single().B, Is.EqualTo(3));
            Assert.That(MatchTestKit.Count(sim, SimEventKind.ScenarioStart), Is.EqualTo(0));
        }

        [Test]
        public void Candidate_RunnableOnlyByForce()
        {
            var d = DataOf(Lib(Mini("C", CarrierOnly, extra: ",\"상태\":\"후보\"")));
            var normal = Sim(d);
            normal.StepTicks(1200);
            Assert.That(MatchTestKit.Count(normal, SimEventKind.ScenarioStart), Is.EqualTo(0));
            var forced = Forced(d, "C");
            Assert.That(MatchTestKit.Count(forced, SimEventKind.ScenarioStart), Is.EqualTo(1));
        }
    }

    public class FallbackOverrideTests
    {
        sealed class Swallow : IDecisionOverride
        {
            public bool IsCast(int slot) => false;
            public bool TryDecide(MatchContext ctx, int slot, ref bool hasAction, out Decision d)
            {
                hasAction = true;
                d = new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
                return true;
            }
        }

        [Test]
        public void OneBallActionPerTick()
        {
            var o = new MatchSimOptions();
            o.Stages = () =>
            {
                var s = MatchSim.DefaultStages();
                s[(int)StageId.ScenarioOrUtility] = new SimpleDecisionStage(new Swallow());
                return s;
            };
            var sim = MatchTestKit.Sim(options: o);
            for (int i = 0; i < 200; i++)
            {
                sim.StepTicks(1);
                Assert.That(sim.Context.Board.GetBallAction().Kind, Is.EqualTo(BallActionKind.None));
            }
        }

        [Test]
        public void EmptyLibrary_SameEventsAsNoScenario()
        {
            var with = Sim(DataOf(ScenarioLibrary.Load(Sources())));
            var without = ShapeTestKit.Sim(setup: ShapeTestKit.Setup(seed: 20261109UL));
            with.StepTicks(3000);
            without.StepTicks(3000);
            Assert.That(with.Log.Hash, Is.EqualTo(without.Log.Hash));
        }
    }

    public class ScenarioSetupWiringTests
    {
        [Test]
        public void NoScenarioData_UsesStandInStage()
        {
            var sim = ShapeTestKit.Sim();
            Assert.That(sim.Stage(StageId.ScenarioOrUtility), Is.TypeOf<SimpleDecisionStage>());
            Assert.That(sim.Stage(StageId.Shape), Is.TypeOf<ShapeStage>());
        }

        [Test]
        public void ScenarioData_UsesScenarioStages()
        {
            var sim = Sim(DataOf(ScenarioLibrary.Load(Sources())));
            Assert.That(sim.Stage(StageId.ScenarioOrUtility), Is.TypeOf<ScenarioStage>());
            Assert.That(sim.Stage(StageId.Shape), Is.TypeOf<ScenarioShapeStage>());
        }

        [Test]
        public void NoTactics_IgnoresScenarioData()
        {
            var t = MatchTuningOf(DataOf(ScenarioLibrary.Load(Sources())));
            var sim = new MatchSim(MatchTestKit.Setup(), t);
            Assert.That(sim.Stage(StageId.ScenarioOrUtility), Is.TypeOf<SimpleDecisionStage>());
        }
    }

    public class ScenarioMassTests
    {
        const int Matches = 20, Ticks = 3000;
        static readonly string[] Generic =
        {
            Mini("GEN-CARRY", "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\",\"distanceM\":10}]},{\"role\":\"r\",\"steps\":[{\"do\":\"run_in_behind\"}]}]",
                category: "PR"),
            Mini("GEN-PASS", "[{\"role\":\"a\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"pass_short\",\"to\":\"b\"}]},{\"role\":\"b\",\"steps\":[{\"do\":\"receive\"}]},{\"role\":\"o\",\"required\":false,\"steps\":[{\"do\":\"overlap\"}]}]",
                category: "BU"),
            Mini("GEN-PRESS", "[{\"role\":\"p\",\"steps\":[{\"t\":0,\"action\":\"engage\"}]},{\"role\":\"c\",\"steps\":[{\"t\":0,\"action\":\"cover_shadow\"}]}]",
                category: "DEF", branches: "[{\"when\":\"regain\",\"goto\":\"GEN-CARRY\"}]"),
        };

        static ScenarioLibrary MassLib()
        {
            var s = SampleSources();
            for (int i = 0; i < Generic.Length; i++) s.Add($"gen{i}.json", Generic[i]);
            return ScenarioLibrary.Load(s);
        }

        struct Row { public int Starts, Ends, Fallbacks, Completes, ReasonSum; public int LogStarts, LogEnds, LogFallbacks, LogCompletes; public bool Overflow; public int MaxOwn, MaxOther, Double; }

        static Row[] _rows;

        [OneTimeSetUp]
        public void Run()
        {
            var d = DataOf(MassLib());
            Assert.That(d.Library.Report.Errors, Is.Empty, string.Join("\n", d.Library.Report.Errors));
            _rows = new Row[Matches];
            for (int m = 0; m < Matches; m++)
            {
                var sim = Sim(d, null, (ulong)(1000 + m));
                var rt = Runtime(sim);
                var row = new Row();
                var seen = new bool[Squad.Slots];
                for (int t = 0; t < Ticks && !sim.Context.Flow.IsFinished; t++)
                {
                    sim.StepTicks(1);
                    System.Array.Clear(seen, 0, seen.Length);
                    var count = new int[2];
                    for (int team = 0; team < 2; team++)
                    {
                        var run = rt.Run(team);
                        if (!run.Active) continue;
                        for (int r = 0; r < run.Def.Cast.Length; r++)
                        {
                            var s = run.Slot[r];
                            if (s < 0) continue;
                            if (seen[s]) row.Double++;
                            seen[s] = true;
                            count[Squad.TeamOfSlot(s)]++;
                        }
                    }
                    for (int team = 0; team < 2; team++)
                    {
                        // 팀의 상한은 자기 시나리오 입장으로 정해진다: 공격 5, 수비·시나리오 없음 4 (FR-34)
                        var run = rt.Run(team);
                        if (run.Active && run.Def.Side == ScenarioSide.Attack) row.MaxOwn = System.Math.Max(row.MaxOwn, count[team]);
                        else row.MaxOther = System.Math.Max(row.MaxOther, count[team]);
                    }
                }
                var stats = rt.Stats;
                for (int o = 0; o < stats.Size; o++)
                {
                    row.Starts += stats.Starts(o);
                    row.Completes += stats.Completes(o);
                    row.Fallbacks += stats.Fallbacks(o);
                    for (int r = 0; r < ScenarioEndReasons.Count; r++)
                        if (ScenarioEndReasons.IsFallback((ScenarioEndReason)r)) row.ReasonSum += stats.Reason(o, (ScenarioEndReason)r);
                }
                for (int i = 0; i < sim.Log.Count; i++)
                {
                    ref readonly var e = ref sim.Log[i];
                    if (e.Kind == SimEventKind.ScenarioStart) row.LogStarts++;
                    else if (e.Kind == SimEventKind.ScenarioEnd)
                    {
                        row.LogEnds++;
                        if (ScenarioEndReasons.IsFallback((ScenarioEndReason)e.B)) row.LogFallbacks++;
                        else row.LogCompletes++;
                    }
                }
                row.Overflow = sim.Log.Count >= RoadToPro.MatchEngine.Match.Events.SimEventLog.DefaultCapacity;
                row.Ends = row.Completes + row.Fallbacks;
                _rows[m] = row;
            }
        }

        [Test]
        public void Caps_20Matches()
        {
            Assert.That(_rows.Max(r => r.Double), Is.EqualTo(0), "한 선수 두 배역");
            Assert.That(_rows.Max(r => r.MaxOwn), Is.LessThanOrEqualTo(5));
            Assert.That(_rows.Max(r => r.MaxOther), Is.LessThanOrEqualTo(4));
        }

        [Test]
        public void Scenarios_ActuallyRun() => Assert.That(_rows.Sum(r => r.Starts), Is.GreaterThan(20));

        [Test]
        public void Aggregate_MatchesEventLog_20Matches()
        {
            foreach (var r in _rows)
            {
                Assert.That(r.Starts, Is.EqualTo(r.LogStarts));
                Assert.That(r.Fallbacks, Is.EqualTo(r.LogFallbacks));
                Assert.That(r.Completes, Is.EqualTo(r.LogCompletes));
            }
        }

        [Test]
        public void FallbackReasons_SumEqual_20Matches()
        {
            foreach (var r in _rows) Assert.That(r.ReasonSum, Is.EqualTo(r.Fallbacks));
        }

        [Test]
        public void EventLogNotOverflowed() => Assert.That(_rows.Any(r => r.Overflow), Is.False);

        [Test]
        public void EmptyLibrary_20Matches()
        {
            var d = DataOf(ScenarioLibrary.Load(Sources()));
            for (int m = 0; m < Matches; m++)
            {
                var sim = Sim(d, null, (ulong)(2000 + m));
                sim.StepTicks(600);
                Assert.That(MatchTestKit.Count(sim, SimEventKind.ScenarioStart), Is.EqualTo(0));
                Assert.That(sim.Context.Flow.State, Is.Not.EqualTo(FlowState.Finished));
            }
        }

        [Test]
        public void TwoMatches_NoLeak()
        {
            var d = DataOf(MassLib());
            var a = Sim(d, null, 7UL);
            a.StepTicks(1000);
            var a2 = Sim(d, null, 7UL);
            a2.StepTicks(1000);
            Assert.That(a2.Log.Hash, Is.EqualTo(a.Log.Hash));
        }
    }

    [Category("Determinism")]
    public class ScenarioDeterminismTests
    {
        static ScenarioLibrary L(bool reversed)
        {
            var s = SampleSources();
            var gen = Mini("GEN", "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\",\"distanceM\":10}]}]", category: "PR");
            s.Add("gen.json", gen);
            if (reversed) s.Files.Reverse();
            return ScenarioLibrary.Load(s);
        }

        sealed class Diag : IScenarioDiagnostics
        {
            public int Running, Ended;
            public void OnRunning(int team, long tick, string id, bool forced, int roleCount, int[] slots, int[] steps) => Running++;
            public void OnBranch(int team, long tick, string id, int role, int step, StepOutcome outcome) { }
            public void OnEnded(int team, long tick, string id, bool forced, ScenarioEndReason reason) => Ended++;
        }

        [Test]
        public void SameSeed_SameLogHash()
        {
            var d = DataOf(L(false));
            var a = Sim(d);
            var b = Sim(d);
            a.StepTicks(4000);
            b.StepTicks(4000);
            Assert.That(MatchTestKit.Count(a, SimEventKind.ScenarioStart), Is.GreaterThan(0));
            Assert.That(a.Log.Hash, Is.EqualTo(b.Log.Hash));
            Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()));
        }

        [Test]
        public void FileOrderShuffled_SameHash()
        {
            var a = Sim(DataOf(L(false)));
            var b = Sim(DataOf(L(true)));
            a.StepTicks(3000);
            b.StepTicks(3000);
            Assert.That(a.Log.Hash, Is.EqualTo(b.Log.Hash));
        }

        [Test]
        public void Snapshot_RoundTrip()
        {
            var o = new MatchSimOptions { Checkpoints = CheckpointMode.All };
            var a = Sim(DataOf(L(false)), o);
            a.RunToEnd();
            var snap = a.NearestSnapshot(18_000);
            var fork = a.Fork();
            fork.Restore(snap, a.Log, a.Checkpoints);
            var r = fork.RunToEnd();
            Assert.That(r.EventLogHash, Is.EqualTo(a.Result.EventLogHash));
            Assert.That(r.FinalStateHash, Is.EqualTo(a.Result.FinalStateHash));
            Assert.That(a.Result.ScenarioIds, Is.Not.Null);
        }

        /// <summary>리뷰 B1: 배역 중인 선수의 역할 캐시가 복원 뒤에도 같아야 한다.</summary>
        [Test]
        public void Snapshot_RoundTrip_WhileScenarioRunning()
        {
            var s = SampleSources();
            s.Add("g1.json", Mini("GEN-CARRY", "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\",\"distanceM\":12}]},{\"role\":\"r\",\"positions\":[\"CM\",\"AM\",\"W\",\"ST\"],\"steps\":[{\"do\":\"run_in_behind\"}]}]", category: "PR"));
            s.Add("g2.json", Mini("GEN-RECV", "[{\"role\":\"p\",\"steps\":[{\"t\":0,\"action\":\"engage\"}]}]",
                trigger: "{\"event\":\"opp_receive\",\"receiverRole\":[\"CM\",\"AM\",\"W\",\"ST\",\"FB\",\"CB\",\"DM\"]}", category: "DEF"));
            var o = new MatchSimOptions { Checkpoints = CheckpointMode.All };
            var a = Sim(DataOf(ScenarioLibrary.Load(s)), o);
            a.StepTicks(8000);
            Assert.That(MatchTestKit.Count(a, SimEventKind.ScenarioStart), Is.GreaterThan(20));
            for (long tick = 500; tick < 8000; tick += 500)
            {
                var fork = a.Fork();
                fork.Restore(a.NearestSnapshot(tick), a.Log, a.Checkpoints);
                fork.RunUntilTick(8000);
                Assert.That(fork.Log.Hash, Is.EqualTo(a.Log.Hash), $"이벤트 로그, 스냅샷 {tick}");
                Assert.That(fork.StateHash(), Is.EqualTo(a.StateHash()), $"상태, 스냅샷 {tick}");
            }
        }

        [Test]
        public void DiagnosticsOnOff_SameHashes()
        {
            var d = DataOf(L(false));
            var diag = new Diag();
            var on = Sim(d, new MatchSimOptions { ScenarioDiagnostics = diag });
            var off = Sim(d);
            on.StepTicks(3000);
            off.StepTicks(3000);
            Assert.That(diag.Running, Is.GreaterThan(0));
            Assert.That(on.Log.Hash, Is.EqualTo(off.Log.Hash));
            Assert.That(on.StateHash(), Is.EqualTo(off.StateHash()));
        }
    }
}
