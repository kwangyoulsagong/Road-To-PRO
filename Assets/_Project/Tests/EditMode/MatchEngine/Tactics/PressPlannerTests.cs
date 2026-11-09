using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public class PressPlannerTests
    {
        static PressContext Ctx(Vec2 ball, bool defending = true, bool counter = false, bool[] candidate = null)
        {
            var own = new Vec2[11]; var opp = new Vec2[11];
            var ownA = new bool[11]; var oppA = new bool[11]; var cand = candidate ?? new bool[11];
            for (int i = 0; i < 11; i++)
            {
                ownA[i] = oppA[i] = true;
                if (candidate == null) cand[i] = i > 0;
                own[i] = new Vec2(ball.X - 0.02f * i, ball.Y + 0.01f * i);
                opp[i] = new Vec2(ball.X + 0.03f * i, ball.Y - 0.015f * i);
            }
            return new PressContext { Tick = 100, Defending = defending, CounterPressWindow = counter, Ball = ball, Own = own, Opp = opp, OwnCandidate = cand, OwnActive = ownA, OppActive = oppA };
        }

        static PressPlanner New() => new PressPlanner(ShapeTestKit.Params());

        static TeamInstructions Ins(float pi, PressLineLevel line = PressLineLevel.High)
        {
            var i = TeamInstructions.Default; i.PressIntensity = pi; i.PressLine = line; return i;
        }

        static PressSignals Back()
        {
            var s = PressSignals.None; s.BackPassReceived = true; return s;
        }

        [Test]
        public void Trigger_Thresholds()
        {
            var p = New();
            var c = Ctx(new Vec2(.5f, .5f));
            var i1 = Ins(1f); var s = Back();
            p.Step(in s, in c, in i1);
            Assert.That(p.Active, Is.True, "백패스 + 강도 1 + High");

            var all = new PressSignals { BackPassReceived = true, TouchQuality = 0f, ReceiverFacingOwnGoal = true, LoftedOrSlow = true, GkShort = true, PassReceiver = -1 };
            var q = New();
            var i0 = Ins(0f);
            q.Step(in all, in c, in i0);
            Assert.That(q.Active, Is.False, "강도 0은 어떤 조합도 개시하지 않는다");
        }

        [TestCase(0.3f, 1)]
        [TestCase(0.5f, 2)]
        [TestCase(0.9f, 3)]
        public void Count_ByIntensity(float intensity, int expected)
        {
            var p = New();
            var all = new PressSignals { BackPassReceived = true, TouchQuality = 0f, ReceiverFacingOwnGoal = true, LoftedOrSlow = true, GkShort = true, PassReceiver = -1 };
            var c = Ctx(new Vec2(.5f, .5f));
            var i = Ins(intensity);
            p.Step(in all, in c, in i);
            Assert.That(p.Count, Is.EqualTo(expected));
        }

        [Test]
        public void CounterPress_AddsOne()
        {
            var p = New();
            var s = new PressSignals { BackPassReceived = true, TouchQuality = 0f, ReceiverFacingOwnGoal = true, PassReceiver = -1 };
            var c = Ctx(new Vec2(.5f, .5f), counter: true);
            var i = Ins(0.3f);
            p.Step(in s, in c, in i);
            Assert.That(p.Count, Is.EqualTo(2));
        }

        [Test]
        public void CastExcluded()
        {
            var cand = new bool[11];
            for (int i = 2; i < 11; i++) cand[i] = true; // 1번(가장 가까운 선수)은 배역 중
            var p = New();
            var c = Ctx(new Vec2(.5f, .5f), candidate: cand);
            var ins = Ins(1f); var s = Back();
            p.Step(in s, in c, in ins);
            for (int o = 0; o < p.Count; o++) Assert.That(p.Presser(o), Is.Not.EqualTo(1));
        }

        [Test]
        public void NotDefending_NoPress()
        {
            var p = New();
            var c = Ctx(new Vec2(.5f, .5f), defending: false);
            var i = Ins(1f); var s = Back();
            p.Step(in s, in c, in i);
            Assert.That(p.Active, Is.False);
        }

        [Test]
        public void PressLineLow_NoPressFarUp()
        {
            var p = New();
            var c = Ctx(new Vec2(.9f, .5f));
            var i = Ins(1f, PressLineLevel.Low); var s = Back();
            p.Step(in s, in c, in i);
            Assert.That(p.Active, Is.False, "낮은 압박 라인은 상대 진영 깊이 나가지 않는다");
        }

        [Test]
        public void FirstResponder_PressPriority()
        {
            var e = ShapeTestKit.Engine("4-4-2");
            var ins = ShapeTestKit.Ins();
            ins.PressIntensity = 1f; ins.PressLine = PressLineLevel.High;
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            var rng = new SeedRng(2UL);
            var s = Back();
            for (int t = 0; t < 12; t++) { f.Tick = t; e.Step(f, in ins, null, in s, rng); }
            Assert.That(e.Output.PressActive, Is.True);
            Assert.That(e.Output.FirstResponder, Is.Not.EqualTo(e.Output.Press1));
        }

        [Test]
        public void PressureRules_FifaDefinition()
        {
            var p = ShapeTestKit.Params();
            var ball = new Vec2(50, 30);
            Assert.That(PressureRules.IsPressing(p, new Vec2(50, 31), default, ball, false), Is.True, "1.5 m 이내");
            Assert.That(PressureRules.IsPressing(p, new Vec2(40, 30), new Vec2(5, 0), ball, true), Is.True, "동료보다 빠름");
            Assert.That(PressureRules.IsPressing(p, new Vec2(40, 30), new Vec2(5, 0), ball, false), Is.False);
            Assert.That(PressureRules.IsPressing(p, new Vec2(28, 30), new Vec2(6, 0), ball, false), Is.True, "20 km/h 초과 25 m");
        }
    }
}
