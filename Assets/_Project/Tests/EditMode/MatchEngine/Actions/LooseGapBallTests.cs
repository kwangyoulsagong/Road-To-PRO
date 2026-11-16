using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/081 T12a: 발에서 떨어진 공(드리블·첫 터치 이격)이 "Controlled"로 남아 GK가 손대지 못한 채 골문으로 들어가던 문제
    /// (슈팅 없는 득점·자책골) — 자기 박스 안 GK가 컨트롤 반경에 들어온 이격 공을 잡는다. GK가 골라인에서 든 공이 발밑 오프셋으로 라인을 넘던 자책골.
    /// </summary>
    public class LooseGapBallTests
    {
        static int Goals(DuelFixture f)
        {
            var n = 0;
            foreach (var e in f.Events) if (e.Kind == SimEventKind.Goal) n++;
            return n;
        }

        [Test]
        public void DribbleIntoBox_KeeperClaimsGapBall_NoGoal()
        {
            var f = new DuelFixture(8121UL);
            var ctx = f.Ctx;
            var dir = ctx.AttackDir(0);
            var goal = ctx.OpponentGoal(0);
            var gk = 11;
            f.Put(gk, new Vec2(goal.X - dir * 1.0f, goal.Y), new Vec2(-dir, 0f));
            f.Put(9, new Vec2(goal.X - dir * 30f, goal.Y), new Vec2(dir, 0f));
            f.Own(9);
            f.SeekAndChase(9, new Vec2(goal.X + dir * 5f, goal.Y)); // 골문 안쪽까지 달린다 (슈팅 없음)
            for (int t = 0; t < 120 && ctx.Flow.InPlay; t++) f.Tick();
            TestContext.WriteLine($"터치 {f.Duels.Touches}, GK 잡기 {f.Duels.KeeperClaims}, 공 {ctx.Ball.Phase} 소유 {ctx.Ball.OwnerId}");
            Assert.AreEqual(0, Goals(f), "슈팅 없이 몰고 들어간 공이 GK 앞을 지나 골이 되지 않는다");
        }

        [Test]
        public void OwnGapBallTowardOwnGoal_TeammateKeeperCollects_NoOwnGoal()
        {
            var f = new DuelFixture(8122UL);
            var ctx = f.Ctx;
            var dir = ctx.AttackDir(0);
            var own = ctx.OwnGoal(0);
            f.Put(0, new Vec2(own.X + dir * 1.0f, own.Y), new Vec2(dir, 0f));           // 팀 0 GK
            f.Put(3, new Vec2(own.X + dir * 25f, own.Y + 2f), new Vec2(-dir, 0f));      // 자기 골문 쪽으로 몰고 가는 수비수
            f.Own(3);
            f.SeekAndChase(3, new Vec2(own.X - dir * 5f, own.Y + 2f));
            for (int t = 0; t < 120 && ctx.Flow.InPlay; t++) f.Tick();
            TestContext.WriteLine($"GK 잡기 {f.Duels.KeeperClaims}, 공 {ctx.Ball.Phase} 소유 {ctx.Ball.OwnerId}");
            Assert.AreEqual(0, Goals(f), "같은 팀 GK가 앞에 있으면 동료의 이격 공이 자기 골문으로 들어가지 않는다");
        }

        [Test]
        public void KeeperHoldingOnGoalLine_FacingGoal_NoOwnGoal()
        {
            var f = new DuelFixture(8123UL);
            var ctx = f.Ctx;
            var dir = ctx.AttackDir(1);
            var own = ctx.OwnGoal(1);
            var gk = 11;
            f.Put(gk, new Vec2(own.X + dir * 0.3f, own.Y), new Vec2(-dir, 0f)); // 골라인 0.3 m 앞에서 골문 쪽을 본다
            f.Own(gk);
            for (int t = 0; t < 10; t++) f.Tick();
            Assert.AreEqual(0, Goals(f), "손에 든 공은 몸 위치 — 라인을 넘지 않는다");
            Assert.AreEqual(BallPhase.Controlled, ctx.Ball.Phase);
            Assert.AreEqual(gk, ctx.Ball.OwnerId);
        }

        [Test]
        public void GapBallReachesKeeper_KeeperClaims()
        {
            // 이격 공이 GK 컨트롤 반경을 지나가게: GK를 공격수 진로 바로 앞(골문 6 m 앞)에 두고 짧은 거리에서 달려 들어간다
            var claims = 0;
            for (int trial = 0; trial < 20; trial++)
            {
                var f = new DuelFixture(8130UL + (ulong)trial);
                var ctx = f.Ctx;
                var dir = ctx.AttackDir(0);
                var goal = ctx.OpponentGoal(0);
                f.Put(11, new Vec2(goal.X - dir * 6f, goal.Y), new Vec2(-dir, 0f));
                f.Put(9, new Vec2(goal.X - dir * 16f, goal.Y), new Vec2(dir, 0f));
                f.Own(9);
                f.SeekAndChase(9, new Vec2(goal.X + dir * 5f, goal.Y));
                for (int t = 0; t < 60 && ctx.Flow.InPlay; t++) f.Tick();
                claims += (int)f.Duels.KeeperClaims;
                Assert.AreEqual(0, Goals(f), $"시행 {trial}: GK를 지나 골이 되지 않는다");
            }
            TestContext.WriteLine($"GK 잡기 {claims}/20");
            Assert.Greater(claims, 0, "이격 공이 GK 컨트롤 반경에 들어오면 GK가 잡는다");
        }
    }
}
