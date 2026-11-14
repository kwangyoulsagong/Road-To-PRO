using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 FR-01~03 · AC-06: 드리블 터치 사이클 (공 컨트롤 유지·이격 중에만 탈취·순간이동 없음).</summary>
    public class DribbleCycleTests
    {
        [Test]
        public void BallControlledEveryTick_StealOnlyWhenGapOpen()
        {
            var f = new DuelFixture();
            f.Put(9, new Vec2(25f, 34f), new Vec2(1f, 0f));
            f.Own(9);
            f.SeekAndChase(9, new Vec2(100f, 34f));
            var maxStep = 0f;
            var prev = f.Ctx.Ball.Position;
            for (int t = 0; t < 80; t++)
            {
                f.Tick();
                Assert.AreEqual(BallPhase.Controlled, f.Ctx.Ball.Phase, $"틱 {t}: 드리블 중 공은 컨트롤");
                Assert.AreEqual(9, f.Ctx.Ball.OwnerId);
                maxStep = System.Math.Max(maxStep, (f.Ctx.Ball.Position - prev).Length());
                prev = f.Ctx.Ball.Position;
            }
            TestContext.WriteLine($"터치 {f.Duels.Touches}, 마지막 터치 {f.Duels.LastTouchDist:F2} m, 틱당 최대 공 이동 {maxStep:F2} m");
            Assert.Greater(f.Duels.Touches, 1, "달리면 터치가 나온다");
            Assert.AreEqual(0, f.Duels.Steals);
            Assert.AreEqual(0, f.Duels.GapReleases);
            Assert.Less(maxStep, f.Ctx.BallParams.LimitSpeed * 0.1f, "공 순간이동 없음");

            // 앞을 막는 상대: 탈취가 나면 그 틱 시작에 이격이 열려 있었거나 그 틱에 터치가 있었다
            var g = new DuelFixture(7071UL);
            int steals = 0, bad = 0;
            for (int trial = 0; trial < 40; trial++)
            {
                g.ClearPitch();
                g.Put(9, new Vec2(30f, 34f), new Vec2(1f, 0f));
                g.Put(13, new Vec2(40f, 35.5f), new Vec2(-1f, 0f));
                g.Own(9);
                g.SeekAndChase(9, new Vec2(100f, 34f));
                g.Seek(13, new Vec2(38f, 34f));
                for (int t = 0; t < 40 && g.Ctx.Ball.Phase == BallPhase.Controlled && g.Ctx.Ball.OwnerId == 9; t++)
                {
                    var open = g.Duels.Dribble.Open;
                    var touches = g.Duels.Touches;
                    var s0 = g.Duels.Steals;
                    g.Tick();
                    if (g.Duels.Steals > s0) { steals++; if (!open && g.Duels.Touches == touches) bad++; }
                }
            }
            TestContext.WriteLine($"탈취 {steals}/40");
            Assert.AreEqual(0, bad, "이격이 닫혀 있을 때 탈취 없음");
        }

        /// <summary>터치당 탈취율: 앞을 가로지르는 상대 1명, 1,000회 상황 반복. 분모 = 터치 수.</summary>
        static double StealRate(float touch, ulong seed, out int touches)
        {
            var v = $"{{\"act.drib.touchLo\": {touch}, \"act.drib.touchHi\": {touch}, \"act.drib.skillLo\": 1, \"act.drib.skillHi\": 1, \"act.drib.distSigma\": 0}}";
            var f = new DuelFixture(seed, v);
            int steals = 0;
            double probSum = 0;
            var t0 = f.Duels.Touches;
            for (int i = 0; i < 1000; i++)
            {
                f.ClearPitch();
                f.Put(9, new Vec2(30f, 34f), new Vec2(1f, 0f));
                f.Put(13, new Vec2(36f, 37f), new Vec2(0f, -1f));
                f.Own(9);
                f.SeekAndChase(9, new Vec2(100f, 34f), 7f);
                f.Seek(13, new Vec2(37f, 31f));
                for (int t = 0; t < 30; t++)
                {
                    var s0 = f.Duels.Steals;
                    var c0 = f.Duels.StealChecks;
                    f.Tick();
                    if (f.Duels.StealChecks > c0) probSum += f.Duels.LastStealProb;
                    if (f.Duels.Steals > s0) { steals++; break; }
                    if (f.Ctx.Ball.Phase != BallPhase.Controlled || f.Ctx.Ball.OwnerId != 9) break;
                }
            }
            touches = (int)(f.Duels.Touches - t0);
            TestContext.WriteLine($"터치 {touch} m: 탈취 {steals} / 터치 {touches}, 판정 {f.Duels.StealChecks} (확률 평균 {probSum / System.Math.Max(f.Duels.StealChecks, 1):F3})");
            return touches > 0 ? steals / (double)touches : 0;
        }

        [Test]
        public void Gap4m_StealsMore_Than1m_1000()
        {
            var p4 = StealRate(4f, 7072UL, out var n4);
            var p1 = StealRate(1f, 7072UL, out var n1);
            var pp = (p4 * n4 + p1 * n1) / System.Math.Max(n4 + n1, 1);
            var z = (p4 - p1) / System.Math.Sqrt(System.Math.Max(pp * (1 - pp) * (1.0 / System.Math.Max(n4, 1) + 1.0 / System.Math.Max(n1, 1)), 1e-12));
            Assert.Greater(z, 2.326, $"4 m {p4:P1} vs 1 m {p1:P1}, z = {z}");
        }

        [Test]
        public void SlowOwner_NoTouch_GlueKept()
        {
            var f = new DuelFixture();
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Own(9);
            f.Seek(9, new Vec2(60f, 34f), 1.5f);
            f.Ticks(30);
            Assert.AreEqual(0, f.Duels.Touches, "걷는 속도 아래는 이격 없음");
        }

        [Test]
        public void TouchOverLine_BoundaryCall()
        {
            var f = new DuelFixture();
            f.Put(9, new Vec2(60f, 64f), new Vec2(0f, 1f));
            f.Own(9);
            f.SeekAndChase(9, new Vec2(60f, 90f));
            var crossed = false;
            for (int t = 0; t < 40 && !crossed; t++)
            {
                f.Tick();
                crossed = f.CrossedLastTick();
            }
            Assert.IsTrue(crossed, "이격 공이 터치라인을 넘으면 라인 통과 사건");
        }

        [Test]
        public void OwnerChange_ResetsDribble()
        {
            var f = new DuelFixture();
            f.Put(9, new Vec2(30f, 34f), new Vec2(1f, 0f));
            f.Own(9);
            f.Seek(9, new Vec2(100f, 34f));
            for (int t = 0; t < 40 && !f.Duels.Dribble.Open; t++) f.Tick();
            Assert.IsTrue(f.Duels.Dribble.Open);
            f.Put(5, new Vec2(60f, 20f), new Vec2(1f, 0f));
            f.Own(5);
            f.Tick();
            Assert.AreEqual(5, f.Duels.Dribble.Owner);
            Assert.IsFalse(f.Duels.Dribble.Open);
        }
    }
}
