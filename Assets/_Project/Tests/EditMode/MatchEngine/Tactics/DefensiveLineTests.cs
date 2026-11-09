using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public sealed class FixedTraits : IPlayerShapeTraits
    {
        public float P = 0.5f, T = 0.5f, C = 0.5f, F = 1f;
        public float Positioning(int slot) => P;
        public float Teamwork(int slot) => T;
        public float Concentration(int slot) => C;
        public float Familiarity(int slot) => F;
    }

    public class DefensiveLineTests
    {
        static float Run(PressSignals s, int ticks, float trap = 1f)
        {
            var p = ShapeTestKit.Params();
            var line = new DefensiveLine(p);
            var rng = new SeedRng(1UL);
            line.Step(0.4f, float.MaxValue, true, PressSignals.None, trap, rng, 0.1f);
            for (int i = 0; i < ticks; i++) line.Step(0.4f, float.MaxValue, true, in s, trap, rng, 0.1f);
            return line.X;
        }

        [Test]
        public void Drop_StepUp()
        {
            var free = PressSignals.None; free.PasserFreeFacingForward = true;
            Assert.That(Run(free, 20), Is.LessThan(0.4f));
            var back = PressSignals.None; back.BackPassReceived = true;
            Assert.That(Run(back, 20), Is.GreaterThan(0.4f));
        }

        [Test]
        public void SpeedLimit()
        {
            var p = ShapeTestKit.Params();
            var line = new DefensiveLine(p);
            var rng = new SeedRng(1UL);
            var s = PressSignals.None;
            line.Step(0.2f, float.MaxValue, true, in s, .5f, rng, .1f);
            var maxStep = p.LineVMax * .1f / p.PitchL;
            var prev = line.X;
            for (int i = 0; i < 60; i++)
            {
                line.Step(0.8f, float.MaxValue, true, in s, .5f, rng, .1f);
                Assert.That(Math.Abs(line.X - prev), Is.LessThanOrEqualTo(maxStep + 1e-6f));
                prev = line.X;
            }
        }

        [Test]
        public void Threat_MoreConservative()
        {
            var p = ShapeTestKit.Params();
            var line = new DefensiveLine(p);
            var rng = new SeedRng(1UL);
            var s = PressSignals.None;
            line.Step(0.4f, float.MaxValue, true, in s, .5f, rng, .1f);
            for (int i = 0; i < 40; i++) line.Step(0.4f, 0.3f, true, in s, .5f, rng, .1f);
            Assert.That(line.X, Is.EqualTo(0.3f).Within(0.01f));
        }

        static double Std(double[] v)
        {
            double m = 0, s = 0;
            foreach (var x in v) m += x;
            m /= v.Length;
            foreach (var x in v) s += (x - m) * (x - m);
            return Math.Sqrt(s / v.Length);
        }

        static TeamShapeEngine WithTraits(FixedTraits t) => ShapeTestKit.Engine("4-4-2", inputs: new ShapeInputProviders { Traits = t });

        [Test]
        public void Cohesion_Sigma()
        {
            var good = WithTraits(new FixedTraits { T = 1f });
            var ins = ShapeTestKit.Ins();
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            ShapeTestKit.Settle(good, f, in ins, 50, errors: true);
            for (int i = 1; i < 11; i++) Assert.That(Math.Abs(good.LineError(i)), Is.LessThanOrEqualTo(1.5f));

            var bad = WithTraits(new FixedTraits { T = 0f });
            var g = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(.5f, .5f));
            for (int i = 0; i < 11; i++) g.Fatigue[i] = 1f;
            var rng = new SeedRng(5UL);
            var s = PressSignals.None;
            var samples = new double[2000];
            int n = 0;
            for (long t = 0; n < samples.Length; t += 5)
            {
                g.Tick = t;
                bad.Step(g, in ins, null, in s, rng);
                for (int i = 1; i < 11 && n < samples.Length; i++) samples[n++] = bad.LineError(i);
            }
            Assert.That(Std(samples), Is.EqualTo(1.0).Within(0.1), "σ_line = 0.6·1 + 0.4·1");
        }

        [TestCase(0f, 4.0)]
        [TestCase(1f, 0.5)]
        public void SlotError_SigmaAndHold(float positioning, double expected)
        {
            var e = WithTraits(new FixedTraits { P = positioning });
            var ins = ShapeTestKit.Ins();
            var f = ShapeTestKit.Frame(TeamPhase.Progression, new Vec2(.5f, .5f));
            var rng = new SeedRng(6UL);
            var s = PressSignals.None;
            var samples = new double[10000];
            int n = 0;
            for (long t = 0; n < samples.Length; t += 5)
            {
                f.Tick = t;
                e.Step(f, in ins, null, in s, rng);
                for (int i = 1; i < 11 && n < samples.Length; i++)
                {
                    samples[n++] = e.SlotError(i).X;
                    if (n < samples.Length) samples[n++] = e.SlotError(i).Y;
                }
            }
            Assert.That(Std(samples), Is.EqualTo(expected).Within(expected * 0.05));

            var held = e.SlotError(3);
            for (int k = 1; k <= 4; k++)
            {
                f.Tick++;
                e.Step(f, in ins, null, in s, rng);
                Assert.That(e.SlotError(3), Is.EqualTo(held), "재매칭 주기 사이에는 고정");
            }
        }

        [Test]
        public void OffsideLine_SweeperKeeper()
        {
            var pos = new Vec2[11];
            var active = new bool[11];
            for (int i = 0; i < 11; i++) { active[i] = true; pos[i] = new Vec2(.45f, .5f); }
            pos[0] = new Vec2(.30f, .5f);  // 스위퍼 GK
            pos[1] = new Vec2(.35f, .5f);
            Assert.That(DefensiveLine.OffsideLine(pos, active, .9f), Is.EqualTo(.35f).Within(1e-6f), "GK 포함 두 번째");
            Assert.That(DefensiveLine.OffsideLine(pos, active, .2f), Is.EqualTo(.2f).Within(1e-6f), "공이 더 뒤");
            for (int i = 0; i < 11; i++) pos[i] = new Vec2(.7f, .5f);
            Assert.That(DefensiveLine.OffsideLine(pos, active, .9f), Is.EqualTo(.5f).Within(1e-6f), "하프라인 상한");
        }
    }
}
