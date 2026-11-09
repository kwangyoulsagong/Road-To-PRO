using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Tactics;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    /// <summary>위치에 따라 값이 달라지는 대체 피치 컨트롤 (B1 재현용).</summary>
    public sealed class PositionalPitchControl : IPitchControlQuery
    {
        public Vec2 GainDirection(int team, Vec2 p) => new Vec2(p.Y - 0.5f, 0.5f - p.X);
    }

    /// <summary>determinism-reviewer 지적의 회귀 테스트 (T20).</summary>
    public class ShapeReviewRegressionTests
    {
        [Test, Category("Determinism")]
        public void Snapshot_PositionDependentPitchControl_RoundTrip()
        {
            var o = new MatchSimOptions { Checkpoints = CheckpointMode.All, ShapeInputs = new ShapeInputProviders { PitchControl = new PositionalPitchControl() } };
            var a = ShapeTestKit.Sim(o);
            a.StepTicks(9000);
            var snap = a.NearestSnapshot(6000);
            var fork = a.Fork();
            fork.Restore(snap, a.Log, a.Checkpoints);
            fork.RunUntilTick(a.NextTick);
            Assert.That(fork.StateHash(), Is.EqualTo(a.StateHash()));
            Assert.That(fork.Log.Hash, Is.EqualTo(a.Log.Hash));
        }

        [Test]
        public void TacticsInput_NotTreatedAsPlayerInput()
        {
            var sim = ShapeTestKit.Sim(); // 홈 선수 id 1~18: 포메이션 입력 PlayerId 1, 지시 입력 PlayerId 6과 겹친다
            sim.StepTicks(20);
            sim.RequestFormation(1, ShapeTestKit.Catalog.IndexOf("5-3-2"));
            sim.RequestInstruction(0, InstructionId.PressIntensity, .8f);
            sim.StepTicks(2);
            Assert.That(MatchTestKit.Count(sim, SimEventKind.InputApplied), Is.EqualTo(0));
            Assert.That(MatchTestKit.Count(sim, SimEventKind.TacticsChanged), Is.EqualTo(2));
        }

        [Test]
        public void Matcher_NaNCost_ThrowsInsteadOfHanging()
        {
            var c = new float[SlotMatcher.MaxSize * SlotMatcher.MaxSize];
            for (int j = 0; j < 3; j++) c[j] = float.NaN;
            Assert.Throws<ArithmeticException>(() => new SlotMatcher().Solve(3, 3, c, new int[3]));
        }

        [Test]
        public void ShortRematchPeriod_Team1StillRematches()
        {
            var reg = TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning", "shape.json"));
            var bundle = TuningLoader.ParseBundle(reg, MatchTestKit.Values(("rematch.period", 0.1f)));
            var e = new TeamShapeEngine(ShapeParams.Create(bundle, PitchSpec.Default), ShapeTestKit.Catalog, new ShapeInputProviders(), 1, ShapeTestKit.Catalog.IndexOf("4-4-2"));
            var ins = ShapeTestKit.Ins();
            var f = ShapeTestKit.Frame(TeamPhase.Progression, new Vec2(.5f, .5f));
            var s = PressSignals.None;
            f.Tick = 3;
            e.Step(f, in ins, null, in s, new SeedRng(1UL));
            Assert.That(e.SlotError(4), Is.Not.EqualTo(default(Vec2)));
        }

        [TestCase("\"reduced\": 5,", "배열")]
        [TestCase("\"reduced\": [{\"fieldPlayers\": 8, \"remove\": [\"ST\"]}],", "맞지 않다")]
        public void BadReducedVariant_Rejected(string insert, string message)
        {
            var json = ShapeTestKit.FormationsJson;
            const string key = "\"id\": \"4-3-3\",";
            var bad = json.Replace(key, key + insert);
            Assert.That(() => FormationCatalog.Parse(bad), Throws.TypeOf<FormationLoadException>().With.Message.Contains(message));
        }

        [TestCase(true, ShapeProfile.Progression)]
        [TestCase(false, ShapeProfile.DefLow)]
        public void SecondHalfKickoff_ProfileFromRestartTeam(bool attacking, ShapeProfile expected)
        {
            var ins = ShapeTestKit.Ins();
            var e = ShapeTestKit.Engine("4-3-3");
            var f = ShapeTestKit.Frame(TeamPhase.FinalThird, new Vec2(.8f, .5f));
            ShapeTestKit.Settle(e, f, in ins, 10);
            f.Period = 1;
            f.Phase = TeamPhase.SetPiece;
            f.Restart = RestartKind.Kickoff;
            f.RestartAttacking = attacking;
            f.Block = TeamPhase.DefLow;
            ShapeTestKit.Settle(e, f, in ins, 1);
            Assert.That(e.Output.Profile, Is.EqualTo(expected));
        }

        [Test]
        public void SetPiece_WallDoesNotUseCastPlayers()
        {
            var ins = ShapeTestKit.Ins();
            var e = ShapeTestKit.Engine("4-4-2");
            var f = ShapeTestKit.Frame(TeamPhase.SetPiece, new Vec2(18f / 105f, .5f));
            f.Restart = RestartKind.FreeKick;
            f.RestartAttacking = false;
            f.RestartSpot = new Vec2(18f / 105f, .5f);
            for (int i = 1; i < 11; i++) f.Own[i] = new Vec2(.09f + .001f * i, .5f); // 전원 벽 근처
            f.Cast[1] = f.Cast[2] = true;
            ShapeTestKit.Settle(e, f, in ins, 1);
            int wall = 0;
            for (int i = 1; i < 11; i++)
            {
                if (!e.HasTarget(i)) continue;
                var d = Vec2.Distance(new Vec2(e.Target(i).X * 105f, e.Target(i).Y * 68f), new Vec2(18f, 34f));
                if (d < 10.5f && e.Target(i).X * 105f < 10f) wall++;
            }
            Assert.That(wall, Is.EqualTo(4), "배역을 뺀 선수로 벽 인원을 채운다");
        }

        [Test]
        public void ManMarkTargets_Validated()
        {
            var s = ShapeTestKit.Setup();
            s.Home.Tactics.ManMarkTargets = new[] { 1, 2 };
            Assert.Throws<MatchSetupException>(() => s.Validate());
        }

        [Test]
        public void Fork_DoesNotReportTelemetry()
        {
            var t = new CountingTelemetry();
            var a = ShapeTestKit.Sim(new MatchSimOptions { ShapeTelemetry = t });
            a.StepTicks(10);
            var before = t.Calls;
            a.Fork().StepTicks(10);
            Assert.That(t.Calls, Is.EqualTo(before));
        }
    }
}
