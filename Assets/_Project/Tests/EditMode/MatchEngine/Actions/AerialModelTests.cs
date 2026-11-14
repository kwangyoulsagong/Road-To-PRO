using NUnit.Framework;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 FR-03·04 · AC-01: 공중볼 경합 — 키 단독 결정 아님.</summary>
    public class AerialModelTests
    {
        static ActionParams P => ActionTestKit.P;
        const int N = 10_000;

        static float S(float heightCm, float aJump, float tArrive, float tLand, float approach = 3f)
        {
            var jump = AerialModel.JumpHeight(P, aJump, approach, 0.5f);
            var reach = AerialModel.Reach(P, heightCm, jump, false);
            return AerialModel.Score(P, reach, AerialModel.Timing(P, tArrive, tLand), 0.5f, 0.5f, 0.5f);
        }

        static int WinsOfFirst(float s0, float s1, int seed)
        {
            var sc = new[] { s0, s1 };
            var pr = new float[2];
            AerialModel.Softmax(sc, 2, pr);
            var rng = new SeedRng((ulong)seed);
            var w = 0;
            for (int i = 0; i < N; i++) if (AerialModel.Pick(pr, 2, rng.NextFloat()) == 0) w++;
            return w;
        }

        [Test]
        public void TallerLate04s_LosesAtLeast30pct_10k()
        {
            var tall = S(190f, 0.5f, 0.6f, 0.2f); // 0.4 s 늦음
            var small = S(170f, 0.5f, 0.2f, 0.2f);
            var tallWins = WinsOfFirst(tall, small, 7111);
            Assert.GreaterOrEqual((N - tallWins) / (double)N, 0.30, $"키 큰 선수 승 {tallWins}");
        }

        [Test]
        public void SameTiming_TallerJumper_WinsMore_Binomial()
        {
            var tall = S(190f, 0.75f, 0.2f, 0.2f);
            var small = S(175f, 0.4f, 0.2f, 0.2f);
            var w = WinsOfFirst(tall, small, 7112);
            Assert.Greater(ShotTestKit.OnePropZ(w, N, 0.5), ShotTestKit.ZCrit, $"승 {w}");
        }

        [Test]
        public void Formula_Components()
        {
            Assert.AreEqual(180f * 1.33f / 100f + 0.5f, AerialModel.Reach(P, 180f, 0.5f, false), 1e-5f);
            Assert.AreEqual(180f * 1.33f / 100f + 0.5f + 0.6f, AerialModel.Reach(P, 180f, 0.5f, true), 1e-5f, "GK +0.6");
            Assert.AreEqual(0.35f, AerialModel.JumpHeight(P, 0f, 0f, 1f), 1e-5f);
            Assert.AreEqual(0.70f * 1.15f, AerialModel.JumpHeight(P, 1f, 100f, 1f), 1e-5f, "runUp 상한 1.15");
            Assert.AreEqual(1f, AerialModel.Timing(P, 0.3f, 0.3f));
            Assert.AreEqual(0f, AerialModel.Timing(P, 1.0f, 0.3f));
            Assert.AreEqual(0.6f, AerialModel.Timing(P, 0.5f, 0.3f), 1e-5f);
            var s = AerialModel.Score(P, 2.8f, 0.5f, 0.6f, 0.7f, 0.4f);
            Assert.AreEqual(4f * 2.8f + 1.5f * 0.5f + 0.8f * 0.6f + 0.3f * 0.7f * 0.3f + 0.6f * 0.4f, s, 1e-4f);
        }

        [Test]
        public void Softmax_Large_NoOverflow()
        {
            var sc = new[] { 1e6f, 1e6f - 1f, -1e6f };
            var pr = new float[3];
            AerialModel.Softmax(sc, 3, pr);
            Assert.AreEqual(1f, pr[0] + pr[1] + pr[2], 1e-5f);
            Assert.Greater(pr[0], pr[1]);
            Assert.IsFalse(float.IsNaN(pr[2]));
            Assert.AreEqual(-1, AerialModel.Pick(pr, 0, 0.5f));
        }
    }
}
