using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-05(첫 터치 10만) · AC-20(1만, 불안한 터치 플래그).</summary>
    public class FirstTouchTests
    {
        static ActionParams P => ActionTestKit.P;

        static double SampleStd(int n, float aFt, float v, float press, ulong seed, out bool flagsOk)
        {
            var rng = new SeedRng(seed);
            double s = 0, s2 = 0;
            flagsOk = true;
            for (int i = 0; i < n; i++)
            {
                var r = FirstTouchModel.Sample(P, aFt, v, press, new Vec2(1f, 0.3f), rng);
                s += r.Offset; s2 += (double)r.Offset * r.Offset;
                if (r.Heavy != (r.Distance > 1.5f)) flagsOk = false;
                if (System.Math.Abs(r.Direction.Length() - 1f) > 1e-4f) flagsOk = false;
            }
            return System.Math.Sqrt(s2 / n - (s / n) * (s / n));
        }

        [Test]
        public void SeparationSigma_100k_Within3pct()
        {
            var sigma = FirstTouchModel.Sigma(P, 0.3f, 20f, 0.4f);
            var raw = (1.8f - 1.4f * 0.3f) / PassModel.RecvDecay(P, 20f, 0.3f) * (1f + 0.5f * 0.4f);
            Assert.AreEqual(raw, sigma, 1e-5f, "FR-67 식");
            var std = SampleStd(100_000, 0.3f, 20f, 0.4f, 91, out var ok);
            Assert.AreEqual(sigma, std, sigma * 0.03);
            Assert.IsTrue(ok);
        }

        [Test]
        public void Sigma10k_And_HeavyFlagOnlyAbove15()
        {
            var sigma = FirstTouchModel.Sigma(P, 0.8f, 12f, 0f);
            var std = SampleStd(10_000, 0.8f, 12f, 0f, 92, out var ok);
            Assert.AreEqual(sigma, std, sigma * 0.03);
            Assert.IsTrue(ok, "불안한 터치 플래그는 이격 > 1.5 m일 때만");
        }

        [Test]
        public void Sample_ConsumesExactlyThree()
        {
            var a = new SeedRng(3);
            var b = new SeedRng(3);
            FirstTouchModel.Sample(P, 0.5f, 10f, 0f, new Vec2(1f, 0f), a);
            for (int i = 0; i < FirstTouchModel.RngPerTouch; i++) b.NextFloat();
            Assert.AreEqual(b.SaveState(), a.SaveState());
        }
    }
}
