using NUnit.Framework;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public class PhaseMachineTests
    {
        static PhaseInput In(long tick, float x, int possession = 0, bool confirmed = true, long change = 0, long loose = 0) => new PhaseInput
        {
            Tick = tick, Team = 0, BallX = x, PossessionTeam = possession, Confirmed = confirmed, PossessionChangeTick = change,
            LooseTicks = loose, OpponentsBehindBall = 9, LineHeight = 0.5f,
        };

        static PhaseMachine M() => new PhaseMachine(ShapeTestKit.Params());

        [TestCase(0.20f, TeamPhase.BuildUp)]
        [TestCase(0.50f, TeamPhase.Progression)]
        [TestCase(0.80f, TeamPhase.FinalThird)]
        public void ThirdsByBallX(float x, TeamPhase expected)
        {
            var m = M();
            var i = In(0, x);
            m.Step(in i);
            Assert.That(m.Current, Is.EqualTo(expected));
        }

        [Test]
        public void BoundaryOscillation_NoFlicker()
        {
            var m = M();
            var first = In(0, 0.2f);
            m.Step(in first);
            long lastChange = -1000;
            for (long t = 1; t < 200; t++)
            {
                var i = In(t, t % 2 == 0 ? 0.329f : 0.331f);
                if (m.Step(in i))
                {
                    Assert.That(t - lastChange, Is.GreaterThanOrEqualTo(5), "0.5 s 안에 두 번 바뀌면 안 된다");
                    lastChange = t;
                }
            }
        }

        [Test]
        public void DefTransition_Window()
        {
            var m = M();
            var a = In(0, 0.6f, possession: 0);
            m.Step(in a);
            for (long t = 1; t <= 151; t++)
            {
                var i = In(t, 0.5f, possession: 1, change: 1);
                m.Step(in i);
                if (t == 50) Assert.That(m.Current, Is.EqualTo(TeamPhase.DefTransition), "4.9 s");
                if (t == 52) Assert.That(PhaseMachine.IsDefBlock(m.Current), Is.True, "5.1 s");
            }
        }

        [Test]
        public void Regroup_ImmediateBlockBlend()
        {
            var e = ShapeTestKit.Engine("4-4-2");
            var ins = ShapeTestKit.Ins();
            ins.OnLoss = OnLossMode.Regroup;
            var f = ShapeTestKit.Frame(TeamPhase.Progression, new RoadToPro.Core.Math.Vec2(0.5f, 0.5f));
            ShapeTestKit.Settle(e, f, in ins);
            f.Phase = TeamPhase.DefTransition;
            ShapeTestKit.Settle(e, f, in ins, 1);
            Assert.That(e.Output.Profile, Is.EqualTo(ShapeProfile.DefMid));
            var c = ShapeTestKit.Ins();
            var e2 = ShapeTestKit.Engine("4-4-2");
            var f2 = ShapeTestKit.Frame(TeamPhase.DefTransition, new RoadToPro.Core.Math.Vec2(0.5f, 0.5f));
            ShapeTestKit.Settle(e2, f2, in c, 1);
            Assert.That(e2.Output.Profile, Is.EqualTo(ShapeProfile.DefHigh), "CounterPress");
        }

        [Test]
        public void DoubleChange_RestartsTimer()
        {
            var m = M();
            var a = In(0, 0.6f, possession: 0);
            m.Step(in a);
            var b = In(10, 0.6f, possession: 1, change: 10);
            m.Step(in b);
            var c = In(11, 0.6f, possession: 0, change: 11);
            m.Step(in c);
            Assert.That(m.Current, Is.EqualTo(TeamPhase.AttTransition));
            Assert.That(m.TransitionTicks(11), Is.EqualTo(0));
        }

        [Test]
        public void Loose_KeepsThenTransition()
        {
            var m = M();
            var a = In(0, 0.6f);
            m.Step(in a);
            var keep = In(5, 0.6f, confirmed: false, loose: 10);
            m.Step(in keep);
            Assert.That(m.Current, Is.EqualTo(TeamPhase.Progression));
            var over = In(20, 0.6f, confirmed: false, loose: 16);
            m.Step(in over);
            Assert.That(m.Current, Is.EqualTo(TeamPhase.DefTransition));
        }

        [Test]
        public void AttTransition_ExitsBetweenSixAndEight()
        {
            var m = M();
            var a = In(0, 0.6f, possession: 1);
            m.Step(in a);
            for (long t = 1; t <= 90; t++)
            {
                var i = In(t, 0.6f, possession: 0, change: 1);
                i.OpponentsBehindBall = 2; // 상대 미정렬
                m.Step(in i);
                if (t == 70) Assert.That(m.Current, Is.EqualTo(TeamPhase.AttTransition), "미정렬이면 8 s까지");
                if (t == 85) Assert.That(m.Current, Is.EqualTo(TeamPhase.Progression), "시간 초과");
            }
        }

        [Test]
        public void DeadBall_SetPieceThenReturns()
        {
            var m = M();
            var a = In(0, 0.6f);
            m.Step(in a);
            var d = In(1, 0.6f); d.DeadBall = true;
            m.Step(in d);
            Assert.That(m.Current, Is.EqualTo(TeamPhase.SetPiece));
            var b = In(2, 0.6f);
            m.Step(in b);
            Assert.That(m.Current, Is.EqualTo(TeamPhase.Progression));
        }
    }
}
