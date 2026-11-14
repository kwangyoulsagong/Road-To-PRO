using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 FR-04~06 · AC-01: 1v1 테이크온.</summary>
    public class TakeOnTests
    {
        static ActionParams P => ActionTestKit.P;

        static ActionWorld Duel(int attDisplay = 10, int defDisplay = 10)
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 5, new Vec2(60f, 34f), default, attDisplay);
            ActionTestKit.Put(w, 16, new Vec2(62f, 34f), default, defDisplay);
            return w;
        }

        [Test]
        public void Average_37pct_100k()
        {
            var w = Duel();
            var prob = TakeOnModel.Probability(P, w, 5, 16, 0f, 0f, 0f);
            Assert.AreEqual(ActionMath.Sigmoid(-0.55f), prob, 1e-5f);
            var rng = new SeedRng(711);
            const int n = 100_000;
            var beat = 0;
            for (int i = 0; i < n; i++) if (TakeOnModel.Resolve(P, prob, rng) == TakeOnOutcome.Beat) beat++;
            Assert.AreEqual(0.37, beat / (double)n, 0.01);
        }

        [Test]
        public void Branches_100k_Within1pp()
        {
            var rng = new SeedRng(712);
            const int n = 100_000;
            var c = new int[5];
            for (int i = 0; i < n; i++) c[(int)TakeOnModel.Resolve(P, 0f, rng)]++;
            Assert.AreEqual(0, c[0]);
            Assert.AreEqual(0.60, c[1] / (double)n, 0.01, "탈취");
            Assert.AreEqual(0.15, c[2] / (double)n, 0.01, "아웃");
            Assert.AreEqual(0.10, c[3] / (double)n, 0.01, "파울 획득");
            Assert.AreEqual(0.15, c[4] / (double)n, 0.01, "루즈볼");
        }

        [Test]
        public void Triggered_Boundary3m_LaneAndSide()
        {
            var goal = new Vec2(105f, 34f);
            var att = new Vec2(60f, 34f);
            Assert.IsTrue(TakeOnModel.Triggered(P, att, new Vec2(62.99f, 34f), goal));
            Assert.IsFalse(TakeOnModel.Triggered(P, att, new Vec2(63f, 34f), goal), "정확히 3 m는 발동 안 함");
            Assert.IsFalse(TakeOnModel.Triggered(P, att, new Vec2(58f, 34f), goal), "뒤에 있는 수비수");
            Assert.IsFalse(TakeOnModel.Triggered(P, att, new Vec2(61f, 36.6f), goal), "골문 선에서 옆으로 벗어남 (laneHalf 2 m)");
        }

        [Test]
        public void Factors_Direction()
        {
            var w = Duel();
            var b = TakeOnModel.Probability(P, w, 5, 16, 0f, 0f, 0f);
            Assert.Greater(TakeOnModel.Probability(P, w, 5, 16, 1f, 0f, 0f), b, "각도 우위");
            Assert.Less(TakeOnModel.Probability(P, w, 5, 16, 0f, 1f, 0f), b, "커버");
            Assert.Less(TakeOnModel.Probability(P, w, 5, 16, 0f, 0f, 1f), b, "정면 자세 수비");
            Assert.AreEqual(1f, TakeOnModel.DefSetFactor(P, new Vec2(1f, 0f)));
            Assert.AreEqual(0f, TakeOnModel.DefSetFactor(P, new Vec2(3f, 0f)));
        }

        [Test]
        public void Cover_SecondDefenderBehind_Raises()
        {
            var w = Duel();
            var goal = new Vec2(105f, 34f);
            Assert.AreEqual(0f, TakeOnModel.Cover(P, w, 5, 16, goal), "두 번째 수비수 없음");
            ActionTestKit.Put(w, 17, new Vec2(65f, 34f));
            var near = TakeOnModel.Cover(P, w, 5, 16, goal);
            ActionTestKit.Put(w, 17, new Vec2(90f, 10f));
            var far = TakeOnModel.Cover(P, w, 5, 16, goal);
            Assert.Greater(near, far);
        }

        [Test]
        public void Resolve_ConsumesExactlyTwo_EvenOnSuccess()
        {
            var a = new SeedRng(3);
            var b = new SeedRng(3);
            TakeOnModel.Resolve(P, 1f, a);
            b.NextFloat(); b.NextFloat();
            Assert.AreEqual(b.NextFloat(), a.NextFloat());
        }
    }
}
