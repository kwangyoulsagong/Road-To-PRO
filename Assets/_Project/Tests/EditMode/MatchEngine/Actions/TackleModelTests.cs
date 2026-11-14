using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 FR-07·10·11·31: 태클 평가·스타일·결과 표본.</summary>
    public class TackleModelTests
    {
        static ActionParams P => ActionTestKit.P;

        [Test]
        public void Reach_SlidingPlusOne()
        {
            Assert.AreEqual(P.TackleReach + 1f, TackleModel.Reach(P, true), 1e-5f);
            Assert.AreEqual(P.TackleReach, TackleModel.Reach(P, false), 1e-5f);
        }

        [Test]
        public void Logit_FormulaAndSlidingPenalty()
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 16, new Vec2(60f, 34f));
            ActionTestKit.Put(w, 5, new Vec2(61f, 34f));
            var a = w.Tackling[16];
            var expect = -0.1f + 2.0f * (a - a * 0.7f - a * 0.3f) + 1.2f * 0.5f - 0.8f * 0.25f;
            Assert.AreEqual(expect, TackleModel.Logit(P, w, 16, 5, 0.5f, 0.25f, false), 1e-5f);
            Assert.AreEqual(expect - 0.3f, TackleModel.Logit(P, w, 16, 5, 0.5f, 0.25f, true), 1e-5f);
        }

        [Test]
        public void Exposure_And_Behind_Monotonic()
        {
            Assert.Greater(TackleModel.BallExposure(P, 2f, 0.5f, 1.2f), TackleModel.BallExposure(P, 0.3f, 0.5f, 1.2f), "긴 터치 = 공 노출");
            Assert.Greater(TackleModel.BallExposure(P, 1f, 0.2f, 1.2f), TackleModel.BallExposure(P, 1f, 1.5f, 1.2f), "가까운 공");
            var c = new Vec2(50f, 34f);
            var h = new Vec2(1f, 0f);
            Assert.AreEqual(0f, TackleModel.AngleFromBehind(c, h, new Vec2(51f, 34f)), 1e-5f, "정면");
            Assert.AreEqual(1f, TackleModel.AngleFromBehind(c, h, new Vec2(49f, 34f)), 1e-5f, "뒤");
            Assert.AreEqual(0.5f, TackleModel.AngleFromBehind(c, h, new Vec2(50f, 35f)), 1e-5f, "옆");
        }

        [Test]
        public void Jockey_OnlyLongTouchAndTtiAdvantage()
        {
            Assert.IsTrue(TackleModel.CanAttempt(P, false, 0.5f, false));
            Assert.IsFalse(TackleModel.CanAttempt(P, true, 1.0f, true), "짧은 터치");
            Assert.IsFalse(TackleModel.CanAttempt(P, true, 2.0f, false), "TTI 열세");
            Assert.IsTrue(TackleModel.CanAttempt(P, true, 2.0f, true));
        }

        [Test]
        public void SlideProbability_TemperamentAndHabitRaise()
        {
            var b = TackleModel.SlideProbability(P, 0.5f, 0);
            Assert.AreEqual(P.TackleSlideBase, b, 1e-5f);
            Assert.Greater(TackleModel.SlideProbability(P, 0.9f, 0), b);
            Assert.Greater(TackleModel.SlideProbability(P, 0.5f, 2), b);
        }

        [Test]
        public void Lateness_ZeroWhileBallThere_RisesAfter()
        {
            Assert.AreEqual(0f, TackleModel.Lateness(P, 0f));
            Assert.Greater(TackleModel.Lateness(P, 0.1f), 0f);
            Assert.AreEqual(1f, TackleModel.Lateness(P, 5f));
        }

        [Test]
        public void Resolve_FixedTwo_SlidingAlwaysPoke()
        {
            var a = new SeedRng(4);
            var b = new SeedRng(4);
            TackleModel.Resolve(P, 0f, false, a);
            b.NextFloat(); b.NextFloat();
            Assert.AreEqual(b.NextFloat(), a.NextFloat());
            var r = new SeedRng(5);
            for (int i = 0; i < 1000; i++) Assert.AreNotEqual(TackleResult.WinKeep, TackleModel.Resolve(P, 1f, true, r));
        }
    }
}
