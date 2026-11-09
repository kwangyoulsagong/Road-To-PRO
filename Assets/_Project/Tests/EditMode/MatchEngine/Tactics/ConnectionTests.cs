using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Match.Stages.StandIn;
using RoadToPro.MatchEngine.Tactics;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public sealed class SetCast : IScenarioCastSource
    {
        public readonly HashSet<int> Slots = new HashSet<int>();
        public bool IsCast(int slot) => Slots.Contains(slot);
        public bool IsLineSyncAdvance(int team) => false;
    }

    public class ScenarioBoundaryTests
    {
        [Test]
        public void CastSkipped_ThenReturnsToSlot()
        {
            var cast = new SetCast();
            cast.Slots.UnionWith(new[] { 6, 7, 9 });
            var inputs = new ShapeInputProviders { Cast = cast, Traits = new FixedTraits { P = 1f } };
            var e = ShapeTestKit.Engine("4-4-2", inputs: inputs);
            var ins = ShapeTestKit.Ins();
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            foreach (var s in cast.Slots) f.Cast[s] = true;
            ShapeTestKit.Settle(e, f, in ins, 20, errors: true);
            foreach (var s in cast.Slots) Assert.That(e.HasTarget(s), Is.False, $"배역 {s}");
            for (int i = 1; i < 11; i++) if (!cast.Slots.Contains(i)) Assert.That(e.HasTarget(i), Is.True, $"비배역 {i}");

            // 5 m 이상 이탈한 배역을 끝낸다: 다음 틱에 슬롯 목표를 받고, 005 이동으로 4 s 안에 3 m 이내로 돌아온다
            var kin = KinematicsParams.Create(MatchTestKit.Tuning().Kinematics, PitchSpec.Default);
            var players = new PlayerSetup[11];
            var attr = new KinematicAttributes(70, 70, 70, 70, 70, 70, 70);
            for (int i = 0; i < 11; i++)
            {
                var p = new Vec2(f.Own[i].X * 105f, f.Own[i].Y * 68f);
                if (cast.Slots.Contains(i) && e.FormationSlotOf(i) < 0)
                {
                    p = new Vec2(e.SlotTarget(i).X * 105f, e.SlotTarget(i).Y * 68f) + new Vec2(14f, 10f); // ≤ 20 m (plan 해석 8)
                    p = new Vec2(Interp(p.X, 105f), Interp(p.Y, 68f));
                }
                players[i] = new PlayerSetup(i, attr, p, default, new Vec2(1f, 0f));
            }
            var sys = new KinematicsSystem(kin, players);
            foreach (var s in cast.Slots) f.Cast[s] = false;
            var rng = new SeedRng(4UL);
            var sig = PressSignals.None;
            for (int tick = 0; tick < 40; tick++)
            {
                f.Tick++;
                for (int i = 0; i < 11; i++)
                {
                    var st = sys.State(i);
                    f.Own[i] = new Vec2(st.Position.X / 105f, st.Position.Y / 68f);
                }
                e.Step(f, in ins, null, in sig, rng);
                if (tick == 0) foreach (var s in cast.Slots) Assert.That(e.HasTarget(s), Is.True, $"복귀 {s}");
                for (int i = 0; i < 11; i++)
                {
                    if (!e.HasTarget(i)) continue;
                    var c = SteeringCommand.Formation(new Vec2(e.Target(i).X * 105f, e.Target(i).Y * 68f));
                    sys.SetCommand(i, in c);
                }
                for (int sub = 0; sub < 10; sub++) sys.Step();
            }
            foreach (var s in cast.Slots)
            {
                var st = sys.State(s).Position;
                var d = Vec2.Distance(st, new Vec2(e.Target(s).X * 105f, e.Target(s).Y * 68f));
                Assert.That(d, Is.LessThanOrEqualTo(3f), $"슬롯 오차 {s}");
            }
        }

        static float Interp(float v, float max) => v < 1f ? 1f : (v > max - 1f ? max - 1f : v);

        [Test]
        public void VacantSlot_CoveredByRematch()
        {
            var e = ShapeTestKit.Engine("4-4-2");
            var ins = ShapeTestKit.Ins();
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            f.Cast[2] = true; // 라인 슬롯 선수가 배역 중
            ShapeTestKit.Settle(e, f, in ins, 12);
            var line = 0;
            for (int i = 1; i < 11; i++) if (e.HasTarget(i) && e.IsLinePlayer(i)) line++;
            Assert.That(line, Is.EqualTo(4), "라인 슬롯 공석을 다른 선수가 메운다");
        }

        [Test]
        public void TooManyCast_Warns()
        {
            var cast = new SetCast();
            cast.Slots.UnionWith(new[] { 1, 2, 3, 4, 5 });
            var o = new MatchSimOptions { ShapeInputs = new ShapeInputProviders { Cast = cast } };
            var sim = ShapeTestKit.Sim(o);
            sim.StepTicks(80);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.ShapeCastOverflow), Is.EqualTo(1));
            Assert.That(sim.IsFinished, Is.False);
        }
    }

    public class RosterChangeTests
    {
        [Test]
        public void RedCard_TenManShape()
        {
            var e = ShapeTestKit.Engine("4-4-2");
            var ins = ShapeTestKit.Ins();
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            ShapeTestKit.Settle(e, f, in ins, 10);
            f.OwnActive[5] = false;
            ShapeTestKit.Settle(e, f, in ins, 1);
            int slots = 0;
            for (int s = 1; s < 11; s++) if (e.SlotActive(s)) slots++;
            Assert.That(slots, Is.EqualTo(9));
            Assert.That(e.HasTarget(5), Is.False);
            AssertUnique(e, f);

            var m = new PhaseMachine(ShapeTestKit.Params());
            var a = new PhaseInput { Tick = 0, Team = 0, BallX = .5f, PossessionTeam = 1, Confirmed = true, OpponentsBehindBall = 9, LineHeight = .5f };
            m.Step(in a);
            Assert.That(m.Block, Is.EqualTo(TeamPhase.DefMid));
            a.Tick = 50; a.BlockDrop = 1;
            m.Step(in a);
            Assert.That(m.Block, Is.EqualTo(TeamPhase.DefLow), "블록 한 단계 내림");
        }

        static void AssertUnique(TeamShapeEngine e, TeamFrame f)
        {
            var seen = new HashSet<int>();
            for (int i = 1; i < 11; i++)
            {
                if (!f.OwnActive[i]) continue;
                var s = e.FormationSlotOf(i);
                Assert.That(s, Is.GreaterThan(0), $"선수 {i} 슬롯 없음");
                Assert.That(seen.Add(s), Is.True, $"슬롯 {s} 중복");
                Assert.That(e.SlotActive(s), Is.True, $"제거된 슬롯 {s}");
            }
        }

        [Test]
        public void NoDuplicateOrEmptyAssignment_ThreeOff()
        {
            var e = ShapeTestKit.Engine("3-5-2");
            var ins = ShapeTestKit.Ins();
            var f = ShapeTestKit.Frame(TeamPhase.DefLow, new Vec2(.3f, .4f));
            f.OwnActive[9] = f.OwnActive[10] = f.OwnActive[4] = false;
            ShapeTestKit.Settle(e, f, in ins, 12);
            AssertUnique(e, f);
        }

        [Test]
        public void SentOff_InMatch_ReducesShape()
        {
            var script = new RefereeScript();
            script.Add(new RefereeAction(60, RefereeActionKind.SendOff, 5));
            var sim = ShapeTestKit.Sim(new MatchSimOptions { Referee = script });
            sim.StepTicks(80);
            Assert.That(sim.IsFinished, Is.False);
            var e = ((ShapeStage)sim.Stage(StageId.Shape)).Engine(0);
            int slots = 0;
            for (int s = 1; s < 11; s++) if (e.SlotActive(s)) slots++;
            Assert.That(slots, Is.EqualTo(9));
        }
    }

    public class SetupWiringTests
    {
        [Test]
        public void NoTactics_UsesStandIn()
        {
            var sim = MatchTestKit.Sim();
            Assert.That(sim.Stage(StageId.Shape), Is.TypeOf<HoldShapeStage>());
        }

        [Test]
        public void Tactics_UsesShapeStages()
        {
            var sim = ShapeTestKit.Sim();
            Assert.That(sim.Stage(StageId.TeamTactics), Is.TypeOf<TacticsStage>());
            Assert.That(sim.Stage(StageId.Phase), Is.TypeOf<PhaseStage>());
            Assert.That(sim.Stage(StageId.Shape), Is.TypeOf<ShapeStage>());
            sim.StepTicks(600);
            Assert.That(sim.IsFinished, Is.False);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.PhaseChange), Is.GreaterThan(0));
        }

        [Test]
        public void OneSidedTactics_Rejected()
        {
            var s = ShapeTestKit.Setup();
            s.Away.Tactics = null;
            Assert.Throws<MatchSetupException>(() => new MatchSim(s, ShapeTestKit.TuningWithShape()));
        }

        [Test]
        public void UnknownFormation_Rejected()
        {
            var s = ShapeTestKit.Setup("9-9-9");
            Assert.Throws<MatchSetupException>(() => new MatchSim(s, ShapeTestKit.TuningWithShape()));
        }

        [Test]
        public void MissingShapeData_Rejected()
        {
            Assert.Throws<MatchSetupException>(() => new MatchSim(ShapeTestKit.Setup(), MatchTestKit.Tuning()));
        }
    }

    public sealed class CountingTelemetry : ITeamShapeTelemetry
    {
        public int Calls;
        public TeamShapeMetrics Last;
        public void OnTeamShape(int team, long tick, TeamPhase phase, in TeamShapeMetrics m) { Calls++; Last = m; }
    }

    public class ShapeTelemetryTests
    {
        [Test]
        public void WritesEveryDecisionTick()
        {
            var t = new CountingTelemetry();
            var sim = ShapeTestKit.Sim(new MatchSimOptions { ShapeTelemetry = t });
            sim.StepTicks(100);
            Assert.That(t.Calls, Is.EqualTo(200));
            Assert.That(t.Last.Length, Is.GreaterThan(0f));
            Assert.That(t.Last.Width, Is.GreaterThan(0f));
            Assert.That(t.Last.Area, Is.EqualTo(t.Last.Length * t.Last.Width).Within(1e-2f));
        }
    }

    public class TacticsChangeTests
    {
        [Test]
        public void Instruction_AppliedNextTickAndLogged()
        {
            var sim = ShapeTestKit.Sim();
            sim.StepTicks(50);
            sim.RequestInstruction(0, InstructionId.LineHeight, 0.9f);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.TacticsChanged), Is.EqualTo(0));
            sim.StepTicks(1);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.TacticsChanged), Is.EqualTo(1));
        }

        [Test]
        public void Formation_ChangesAtNextTick()
        {
            var sim = ShapeTestKit.Sim();
            sim.StepTicks(50);
            var idx = ShapeTestKit.Catalog.IndexOf("5-3-2");
            sim.RequestFormation(0, idx);
            sim.StepTicks(1);
            Assert.That(((ShapeStage)sim.Stage(StageId.Shape)).Engine(0).Formation.Id, Is.EqualTo("5-3-2"));
            Assert.That(MatchTestKit.Count(sim, SimEventKind.TacticsChanged), Is.EqualTo(1));
        }

        [Test]
        public void BadFormationIndex_WarnsAndIgnored()
        {
            var sim = ShapeTestKit.Sim();
            sim.StepTicks(5);
            sim.RequestFormation(1, 99);
            sim.StepTicks(1);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.TacticsInputClamped), Is.EqualTo(1));
            Assert.That(((ShapeStage)sim.Stage(StageId.Shape)).Engine(1).Formation.Id, Is.EqualTo("4-4-2"));
        }
    }
}
