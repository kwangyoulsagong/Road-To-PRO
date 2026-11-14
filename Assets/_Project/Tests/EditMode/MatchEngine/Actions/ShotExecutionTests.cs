using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 FR-14 · AC-04: 슈팅 속도·조준 오차.</summary>
    public class ShotExecutionTests
    {
        static ActionParams P => ActionTestKit.P;

        [Test]
        public void Speed_Lerp18to32()
        {
            Assert.AreEqual(18f, ShotExecution.SpeedFor(P, 0f), 1e-5f);
            Assert.AreEqual(32f, ShotExecution.SpeedFor(P, 1f), 1e-5f);
            Assert.AreEqual(25f, ShotExecution.SpeedFor(P, 0.5f), 1e-5f);
        }

        [Test]
        public void Sigma_Formula()
        {
            var s = ShotExecution.SigmaAimDeg(P, 0.5f, 27f, 0.5f, 0.2f, 1.2f, false);
            var expect = (6f + (1.5f - 6f) * 0.5f) * (1f + 0.5f * 5f / 10f) * (1f + 0.8f * 0.5f * 0.8f) * 1.2f;
            Assert.AreEqual(expect, s, 1e-4f);
            Assert.AreEqual(expect * 1.6f, ShotExecution.SigmaAimDeg(P, 0.5f, 27f, 0.5f, 0.2f, 1.2f, true), 1e-4f, "헤더 1.6");
        }

        [Test]
        public void AimSigma_100k_Within3pct()
        {
            var rng = new SeedRng(7104);
            var sigma = ShotExecution.SigmaAimDeg(P, 0.6f, 25f, 0.3f, 0.5f, 1f, false);
            var yaw = new ShotTestKit.Moments();
            var pitch = new ShotTestKit.Moments();
            var v = new Vec3(20f, 5f, 3f);
            for (int i = 0; i < 100_000; i++)
            {
                var o = ShotExecution.Apply(v, sigma, rng, out var ey, out var ep);
                yaw.Add(ey);
                pitch.Add(ep);
                Assert.AreEqual(v.Length(), o.Length(), 1e-3f);
            }
            Assert.AreEqual(sigma, yaw.Std, sigma * 0.03);
            Assert.AreEqual(sigma, pitch.Std, sigma * 0.03);
        }

        [Test]
        public void Apply_ConsumesExactly4()
        {
            var a = new SeedRng(1);
            var b = new SeedRng(1);
            ShotExecution.Apply(new Vec3(20f, 0f, 2f), 3f, a, out _, out _);
            for (int i = 0; i < ShotExecution.RngPerShot; i++) b.NextFloat();
            Assert.AreEqual(b.NextU64(), a.NextU64());
        }
    }
}
