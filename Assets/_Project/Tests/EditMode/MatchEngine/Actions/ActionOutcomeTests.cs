using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/008 AC-23: 고정 상황마다 실행 결과 종류가 FR-70대로 하나씩, 상대 캐리어가 도달 거리 밖인데 태클로 기록된 경우 0 (1,000 시드).
    /// 슈팅 블록은 071(블록 모델) 범위라 여기서는 없다.
    /// </summary>
    public class ActionOutcomeTests
    {
        [Test]
        public void LooseBall_FarOpponent_IsRecovery()
        {
            var f = new ActionFixture();
            f.Put(1, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(60f, 40f), new Vec2(-1f, 0f)); // 상대는 도달 거리 밖
            f.Loose(new Vec2(48f, 34f), new Vec2(1.5f, 0f), 1, BallActionKind.None, 13);
            f.RunReception(200);
            Assert.AreEqual(1, f.Count(SimEventKind.BallRecovered));
            Assert.AreEqual(0, f.Count(SimEventKind.Tackle));
            Assert.AreEqual(0, f.Count(SimEventKind.Interception));
        }

        [Test]
        public void PassThroughDefender_IsInterception()
        {
            var f = new ActionFixture();
            var got = 0;
            for (int attempt = 0; attempt < 50 && got == 0; attempt++)
            {
                f.ClearPitch();
                f.Put(2, new Vec2(50f, 34.2f), new Vec2(-1f, 0f)); // 수비 (홈)
                f.Put(15, new Vec2(65f, 34f), new Vec2(-1f, 0f));  // 원정 수신자
                f.Loose(new Vec2(40f, 34f), new Vec2(15f, 0f), 1, BallActionKind.Pass, 14);
                f.RunReception(150);
                got = f.Count(SimEventKind.Interception) + f.Count(SimEventKind.PassReceived);
            }
            Assert.AreEqual(1, f.Count(SimEventKind.Interception), "레인 중간 수비수가 먼저 잡으면 인터셉트");
            Assert.AreEqual(0, f.Count(SimEventKind.Tackle));
        }

        [Test]
        public void OwnerClearance_IsClearance()
        {
            var f = new ActionFixture();
            f.Put(3, new Vec2(20f, 34f), new Vec2(1f, 0f));
            f.Own(3);
            f.Kick(new BallAction { Kind = BallActionKind.Clearance, KickerSlot = 3, TargetSlot = -1, Velocity = new Vec3(18f, 0f, 8f) }, 100);
            Assert.AreEqual(1, f.Count(SimEventKind.Clearance));
            Assert.AreEqual(0, f.Count(SimEventKind.Tackle));
        }

        [Test]
        public void RealTackle_InReach_IsTackle()
        {
            var f = new ActionFixture();
            f.Put(16, new Vec2(50f, 34f), new Vec2(-1f, 0f)); // 원정 캐리어
            f.Own(16);
            f.Put(4, new Vec2(49.3f, 34f), new Vec2(1f, 0f)); // 홈 태클러 (도달 거리 1.2 m 안)
            f.Kick(new BallAction { Kind = BallActionKind.Clearance, KickerSlot = 4, TargetSlot = -1, Velocity = new Vec3(6f, 0f, 0f) }, 200);
            Assert.AreEqual(1, f.Count(SimEventKind.Tackle));
        }

        /// <summary>회귀 (T18 발견): 놓친 공이 선수 반경 안에서 멈추면 아무도 안 잡아 20분 동안 경기가 멈췄다. 인플레이 느슨한 공이 10초 넘게 방치되면 실패.</summary>
        [Test]
        public void LooseBall_NeverStuck_6Matches()
        {
            foreach (var seed in new ulong[] { 20261103UL, 1, 2, 3, 71, 5 })
            {
                var sim = RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit.Sim(seed);
                var ctx = sim.Context;
                long looseSince = -1;
                while (!sim.IsFinished)
                {
                    sim.StepTick();
                    var loose = ctx.Flow.InPlay && ctx.Ball.Phase != RoadToPro.MatchEngine.Ball.BallPhase.Controlled;
                    if (!loose) { looseSince = -1; continue; }
                    if (looseSince < 0) looseSince = ctx.Tick;
                    Assert.Less(ctx.Tick - looseSince, 100, $"시드 {seed} 틱 {ctx.Tick}: 느슨한 공 10초 넘게 방치 ({ctx.Ball.Position.X:F1}, {ctx.Ball.Position.Y:F1})");
                }
            }
        }

        [Test]
        public void FarCarrier_NeverTackle_1000Seeds()
        {
            var f = new ActionFixture();
            var rng = new SeedRng(2323);
            var reach = f.Ctx.Params.TackleRange;
            for (int i = 0; i < 1000; i++)
            {
                f.ClearPitch();
                var c = new Vec2(rng.Range(20f, 85f), rng.Range(10f, 58f));
                f.Put(17, c, new Vec2(-1f, 0f));
                f.Own(17);
                var ang = rng.Range(0f, 6.283f);
                var d = rng.Range(reach + 0.6f, reach + 6f); // 공(캐리어 앞 0.45 m)까지도 도달 거리 밖
                f.Put(5, c + new Vec2(System.MathF.Cos(ang), System.MathF.Sin(ang)) * d, new Vec2(1f, 0f));
                f.Kick(new BallAction { Kind = BallActionKind.Clearance, KickerSlot = 5, TargetSlot = -1, Velocity = new Vec3(6f, 0f, 0f) }, 150);
            }
            Assert.AreEqual(0, f.Count(SimEventKind.Tackle), "도달 거리 밖 태클 기록 0");
            Assert.AreEqual(0, f.Count(SimEventKind.Clearance), "공을 못 건드렸으니 걷어내기도 없음");
        }
    }
}
