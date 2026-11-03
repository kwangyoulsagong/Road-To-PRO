using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-08: 22명 혼잡 해소 (FR-24·27).</summary>
    public class SeparationTests
    {
        static float MinPairDistance(KinematicsSystem k)
        {
            float m = float.MaxValue;
            for (int i = 0; i < k.Count; i++)
            for (int j = i + 1; j < k.Count; j++)
                m = DMath.Min(m, (k.State(i).Position - k.State(j).Position).Length());
            return m;
        }

        [TestCase(42UL, false)]
        [TestCase(7UL, true)]
        public void TwentyTwoInRadius2_Resolve3s_NoDivergence(ulong seed, bool allSeekCenter)
        {
            var rng = new SeedRng(seed);
            var c = new Vec2(52.5f, 34f);
            var ps = new PlayerSetup[22];
            for (int i = 0; i < 22; i++)
            {
                var ang = rng.Range(0f, DMath.TwoPi);
                var rad = 2f * DMath.Sqrt(rng.NextFloat());
                ps[i] = new PlayerSetup(i + 1, KinematicAttributes.Uniform(1 + i % 20), c + new Vec2(DMath.Cos(ang), DMath.Sin(ang)) * rad);
            }
            var k = new KinematicsSystem(Params, ps);
            k.Monitor = new MotionLimitMonitor();
            if (allSeekCenter) for (int i = 0; i < 22; i++) k.SetCommandImmediate(i + 1, SteeringCommand.Arrive(c));
            int resolvedAt = -1;
            for (int t = 0; t < 300; t++)
            {
                k.Step();
                for (int i = 0; i < 22; i++)
                    Assert.That(k.State(i).Velocity.Length(), Is.LessThanOrEqualTo(k.Profile(i).MaxSpeed * 1.001f), "발산");
                if (resolvedAt < 0 && MinPairDistance(k) >= 0.8f - 1e-3f) resolvedAt = t;
            }
            Assert.That(resolvedAt, Is.InRange(0, 299), "3 s 안에 모든 쌍 ≥ 0.8 m");
            Assert.That(MinPairDistance(k), Is.GreaterThanOrEqualTo(0.8f - 1e-3f), "해소 후 유지");
            Assert.That(k.Monitor.TotalViolations, Is.Zero);
        }

        [Test]
        public void SamePosition_SplitsDeterministicallyById()
        {
            var a = KinematicAttributes.Uniform(10);
            var p = new Vec2(50f, 30f);
            var k = new KinematicsSystem(Params, new[] { new PlayerSetup(9, a, p), new PlayerSetup(4, a, p) });
            Run(k, 50);
            Assert.That(k.State(0).Id, Is.EqualTo(4));
            Assert.That(k.State(0).Position.X, Is.LessThan(k.State(1).Position.X), "작은 id는 −x 쪽");
            Assert.That((k.State(0).Position - k.State(1).Position).Length(), Is.GreaterThanOrEqualTo(0.8f - 1e-3f));
        }
    }
}
