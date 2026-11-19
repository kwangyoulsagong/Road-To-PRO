using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;
using RoadToPro.Tests.EditMode.MatchEngine.Ball;
using static RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper.KeeperLeaveKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/073 1v1 FR-20~22 (AC-03).</summary>
    public class OneOnOneModelTests
    {
        static RoadToPro.MatchEngine.Actions.ActionWorld Scene()
        {
            var w = World(new Vec2(3f, 34f));
            ActionTestKit.Put(w, 12, new Vec2(16f, 34f), new Vec2(-5f, 0f)); // 캐리어
            return w;
        }

        [Test]
        public void BigTouch_Smother_SmallTouch_Set()
        {
            var m = new OneOnOneModel(P);
            var k = Profile();
            var big = m.Evaluate(Scene(), 0, in k, Goal, 1f, 12, new Vec2(6f, 34f), true);
            Assert.IsTrue(big.Has);
            TestContext.WriteLine($"큰 터치: TTI 공격 {big.TAtt:F2} / GK {big.TGk:F2}");
            Assert.IsTrue(big.Smother, "큰 터치(공이 GK 쪽) → 스마더");
            var small = m.Evaluate(Scene(), 0, in k, Goal, 1f, 12, new Vec2(16.5f, 34f), false);
            Assert.IsTrue(small.Has);
            Assert.IsFalse(small.Smother, "작은 터치(발에 붙음) → 세트");
            Assert.Greater(small.SetPoint.X, Goal.X, "이등분선 위 전진");
            Assert.LessOrEqual(small.SetPoint.X - Goal.X, P.PosDepthMax + 1e-4f, "011 깊이 상한 안");
        }

        [Test]
        public void DefenderGoalSide_NotOneOnOne()
        {
            var m = new OneOnOneModel(P);
            var w = Scene();
            ActionTestKit.Put(w, 3, new Vec2(9f, 34.8f));
            Assert.IsFalse(m.Evaluate(w, 0, Profile(), Goal, 1f, 12, new Vec2(16.5f, 34f), false).Has, "골 쪽 수비수가 있으면 1v1 아님");
            var far = Scene();
            ActionTestKit.Put(far, 12, new Vec2(40f, 34f));
            Assert.IsFalse(m.Evaluate(far, 0, Profile(), Goal, 1f, 12, new Vec2(40.5f, 34f), false).Has, "range 밖");
        }

        [Test]
        public void SetNotStopped_ReachPenalty()
        {
            var sm = new SaveModel(P, new KeeperPositioner(P, new GoalGrid(P)));
            var bp = BallTestKit.Params();
            sm.Bind(bp);
            var k = Profile();
            var shot = sm.AimedShot(new Vec3(12f, 34f, 0.11f), 2, 22f, Goal, bp);
            Assert.IsTrue(sm.Predict(in shot, bp, Goal, 1f));
            var ctx = new SaveContext { Goal = Goal, DirIn = 1f };
            var still = sm.Evaluate(new KeeperState { Position = new Vec2(3f, 34f) }, in k, in ctx);
            var moving = sm.Evaluate(new KeeperState { Position = new Vec2(3f, 34f), Velocity = new Vec2(3f, 0f) }, in k, in ctx);
            Assert.IsTrue(moving.MovingPenalty);
            Assert.AreEqual(P.StillMovingPenalty, still.MaxMargin - moving.MaxMargin, 1e-4f, "정지 못 하면 reach − 패널티 (011 FR-09 공용)");
        }

        [Test]
        public void StopTimingSigma_OneOnOne1_vs_20()
        {
            Assert.AreEqual(0.15, Sd(Profile(oneOnOne: 0f), 7304UL), 0.15 * 0.03, "OneOnOne 1: σ_t 0.15 s ±3 %");
            Assert.AreEqual(0.05, Sd(Profile(oneOnOne: 100f), 7305UL), 0.05 * 0.03, "OneOnOne 20: σ_t 0.05 s ±3 %");
        }

        static double Sd(KeeperProfile k, ulong seed)
        {
            var rng = new SeedRng(seed);
            const int N = 100_000;
            double sum = 0, sq = 0;
            for (int i = 0; i < N; i++) { double e = OneOnOneModel.StopError(in k, rng); sum += e; sq += e * e; }
            var mean = sum / N;
            return System.Math.Sqrt(sq / N - mean * mean);
        }

        [Test]
        public void Spread_NearShot_R0Up_DiveDown()
        {
            var m = new OneOnOneModel(P);
            var pos = new KeeperPositioner(P, new GoalGrid(P));
            var k = Profile();
            var s = m.Spread(in k);
            Assert.AreEqual(P.OneOnOneSpreadR0, s.R0Bonus, 1e-6f);
            Assert.AreEqual(k.DiveSpeed * (1f - P.OneOnOneSpreadDive), s.DiveSpeed, 1e-5f);
            Assert.Greater(pos.ReachAfterReact(0.05f, 0.5f, in s), pos.ReachAfterReact(0.05f, 0.5f, in k), "근거리(짧은 시간) 유리");
            Assert.Less(pos.ReachAfterReact(0.45f, 0.5f, in s), pos.ReachAfterReact(0.45f, 0.5f, in k), "먼 다이빙은 불리");
            Assert.AreEqual(0f, k.R0Bonus, "매핑값은 스프레드 없음 (011과 같음)");
        }
    }
}
