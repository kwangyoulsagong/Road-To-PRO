using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public class ShapePropertyTests
    {
        const float L = ShapeTestKit.PitchL, W = ShapeTestKit.PitchW;
        static readonly string[] Ids = { "4-3-3", "4-2-3-1", "4-4-2", "3-5-2", "3-4-3", "5-3-2" };
        static readonly TeamPhase[] Open = { TeamPhase.BuildUp, TeamPhase.Progression, TeamPhase.FinalThird, TeamPhase.AttTransition, TeamPhase.DefTransition, TeamPhase.DefHigh, TeamPhase.DefMid, TeamPhase.DefLow };
        static readonly Vec2[] Edge = { new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1), new Vec2(1, 0), new Vec2(-.02f, .5f), new Vec2(1.02f, .5f), new Vec2(.5f, -.02f), new Vec2(.5f, 1.02f), new Vec2(.33f, .5f), new Vec2(.66f, .5f) };

        static void Randomize(TeamFrame f, SeedRng r, long tick)
        {
            f.Tick = tick;
            f.Ball = tick % 7 == 0 ? Edge[r.Range(0, Edge.Length - 1)] : new Vec2(r.NextFloat(), r.NextFloat());
            f.OppOffsideLine = r.Range(0.02f, 0.5f);
            for (int i = 0; i < TeamFrame.N; i++)
            {
                f.Own[i] = new Vec2(r.NextFloat(), r.NextFloat());
                f.Opp[i] = new Vec2(r.NextFloat(), r.NextFloat());
                f.OwnVel[i] = new Vec2(r.Range(-8f, 8f), r.Range(-8f, 8f));
                f.OppVel[i] = new Vec2(r.Range(-8f, 8f), r.Range(-8f, 8f));
                f.Fatigue[i] = r.NextFloat();
            }
        }

        static TeamInstructions RandomIns(SeedRng r)
        {
            var i = TeamInstructions.Default;
            i.LineHeight = r.NextFloat(); i.WidthIn = r.NextFloat(); i.WidthOut = r.NextFloat(); i.Compactness = r.NextFloat();
            i.PressIntensity = r.NextFloat(); i.OffsideTrap = r.NextFloat();
            return i;
        }

        [Test]
        public void OffsideAndDefLineCaps_10000States()
        {
            var r = new SeedRng(9001UL);
            var eps = 0.5f / L;
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            foreach (var id in Ids)
            {
                var e = ShapeTestKit.Engine(id);
                for (long t = 0; t < 10000 / Ids.Length + 1; t++)
                {
                    Randomize(f, r, t);
                    f.Phase = Open[r.Range(0, Open.Length - 1)];
                    var ins = RandomIns(r);
                    var s = PressSignals.None;
                    e.Step(f, in ins, null, in s, r);
                    if (PhaseMachine.IsPossessing(f.Phase))
                    {
                        for (int i = 1; i < 11; i++)
                            if (e.HasTarget(i)) Assert.That(e.Target(i).X, Is.LessThanOrEqualTo(1f - f.OppOffsideLine - eps + 1e-5f), id + " 오프사이드 상한");
                    }
                    else if (e.Output.Profile > ShapeProfile.FinalThird)
                    {
                        var min = float.MaxValue;
                        for (int i = 1; i < 11; i++) if (e.HasTarget(i) && e.IsLinePlayer(i) && e.Target(i).X < min) min = e.Target(i).X;
                        for (int i = 1; i < 11; i++) if (e.HasTarget(i)) Assert.That(e.Target(i).X, Is.GreaterThanOrEqualTo(min - 1e-5f), id + " 수비 라인 하한");
                    }
                }
            }
        }

        [Test]
        public void TargetsInsidePitch_10000States()
        {
            var r = new SeedRng(9002UL);
            var mx = 0.5f / L; var my = 0.5f / W;
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            var kinds = new[] { RestartKind.FreeKick, RestartKind.Corner, RestartKind.ThrowIn, RestartKind.GoalKick, RestartKind.Penalty, RestartKind.Kickoff };
            var e = ShapeTestKit.Engine("4-3-3");
            for (long t = 0; t < 10000; t++)
            {
                Randomize(f, r, t);
                var setPiece = t % 4 == 0;
                f.Phase = setPiece ? TeamPhase.SetPiece : Open[r.Range(0, Open.Length - 1)];
                f.Restart = kinds[r.Range(0, kinds.Length - 1)];
                f.RestartAttacking = r.Bernoulli(.5f);
                f.RestartSpot = new Vec2(r.NextFloat(), r.NextFloat());
                f.RestartTaker = -1;
                var ins = RandomIns(r);
                var s = PressSignals.None;
                e.Step(f, in ins, null, in s, r);
                for (int i = 0; i < 11; i++)
                {
                    if (!e.HasTarget(i)) continue;
                    Assert.That(e.Target(i).X, Is.InRange(mx - 1e-5f, 1f - mx + 1e-5f), $"x {f.Phase}");
                    Assert.That(e.Target(i).Y, Is.InRange(my - 1e-5f, 1f - my + 1e-5f), $"y {f.Phase}");
                }
            }
        }
    }
}
