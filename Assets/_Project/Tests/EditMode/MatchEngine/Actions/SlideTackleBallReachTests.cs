using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/083 FR-03 ⓐ (Q1 ③, 2026-11-17): 슬라이딩 태클은 판정 순간 태클러–공 수평 거리도 슬라이딩 도달 거리(act.tackle.reach + slideExtra) 안일 때만 이긴다.
    /// 밖이면 빗나감 — 확률 판정(RNG 2)은 그대로 한다. 서서 하는 태클은 이 스펙 밖(기존 판정).
    /// </summary>
    public class SlideTackleBallReachTests
    {
        // 준비 시간을 틱 배수로: 판정이 틱 첫 서브스텝(공 붙이기 전)에 와서, 틱 사이에 놓은 공 위치로 판정된다
        const string SureWin = "{\"act.tackle.b0\": 3, \"act.tackle.windupSlide\": 0.2, \"act.tackle.windupStand\": 0.1}";

        /// <summary>캐리어 9(50, 34) 오른쪽 진행, 태클러 13은 캐리어 뒤 tacklerBack m. 공은 캐리어 앞 ballAhead m (드리블 터치로 앞에 나간 공, Controlled).</summary>
        static DuelFixture Setup(ulong seed, float tacklerBack, float ballAhead, bool sliding, out long resolvedBefore)
        {
            var f = new DuelFixture(seed, SureWin);
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(50f - tacklerBack, 34.2f), new Vec2(1f, 0f));
            f.Own(9);
            f.Ctx.Ball = BallState.Controlled(9, new Vec3(50f + ballAhead, 34f, f.Ctx.BallParams.Radius));
            resolvedBefore = f.Duels.TacklesResolved;
            Assert.IsTrue(f.Duels.ScheduleTackle(f.Ctx, 13, 9, sliding));
            _ahead = ballAhead;
            return f;
        }

        static float _ahead;

        /// <summary>틱마다 공을 캐리어 앞 ballAhead m에 다시 놓는다 (드리블 터치로 나간 공 흉내 — 픽스처에는 결정 단계가 없어 터치가 안 난다).</summary>
        static void Run(DuelFixture f, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                var p = f.Ctx.SlotState(9).Position;
                if (f.Ctx.Ball.OwnerId == 9) f.Ctx.Ball = BallState.Controlled(9, new Vec3(p.X + _ahead, p.Y, f.Ctx.BallParams.Radius));
                f.Tick();
            }
        }

        static int Count(DuelFixture f, int bit)
        {
            int n = 0;
            foreach (var e in f.Events) if (e.Kind == SimEventKind.Tackle && (bit == 0 || (e.A & bit) != 0)) n++;
            return n;
        }

        static float SlideReach(DuelFixture f) => TackleModel.Reach(f.Ctx.Actions.P, true);

        [Test]
        public void FarBall_Miss_RngSame()
        {
            var f = Setup(8301UL, 1.5f, 2.5f, true, out var before); // 캐리어 1.5 m(안), 공 4.0 m(밖)
            Assert.Greater(4.0f, SlideReach(f));
            Run(f, 4);
            Assert.AreEqual(1, Count(f, 0), "태클 판정 1건");
            Assert.AreEqual(0, Count(f, EventCodes.TackleWon), "공이 도달 거리 밖 → 빗나감");
            Assert.AreEqual(before + 1, f.Duels.TacklesResolved, "확률 판정(RNG 2)은 그대로");
            Assert.IsTrue(f.Ctx.Kinematics.IsImmobile(13), "빗나간 슬라이딩 → 다운");
            Assert.AreEqual(9, f.Ctx.Ball.OwnerId, "캐리어가 공을 지킴");
        }

        [Test]
        public void NearBall_Unchanged()
        {
            var f = Setup(8302UL, 1.5f, 0.45f, true, out _); // 공 1.95 m(안)
            Run(f, 4);
            Assert.AreEqual(1, Count(f, EventCodes.TackleWon), "공도 도달 거리 안 → 기존 판정(확실한 이김)");
        }

        [Test]
        public void Standing_Unchanged()
        {
            var f = Setup(8303UL, 1.0f, 2.5f, false, out _); // 서서: 캐리어 1.0 m(안), 공 3.5 m — 이 스펙 밖, 기존 판정
            Run(f, 4);
            Assert.AreEqual(1, Count(f, EventCodes.TackleWon), "서서 하는 태클은 공 거리 조건 없음(083 범위 밖)");
        }

        /// <summary>실경기 고정 시드 묶음(082 + 083) 90분 불변식: 이긴 슬라이딩의 판정 순간 태클러–공 수평 거리 ≤ 슬라이딩 도달 거리 (진단 누계 MaxSlideWinBallDist, 해시 밖).</summary>
        [Test]
        public void WonSlide_BallWithinReach()
        {
            float worst = 0f;
            long wins = 0;
            var seeds = RoadToPro.Tests.EditMode.Presentation.Highlight.LiveContactAuditTests.FixedSeeds();
            RoadToPro.Tests.EditMode.Presentation.Highlight.LiveContactAudit.Prewarm(seeds); // 경기 여러 개 병렬, 진단 테스트와 공유
            foreach (var seed in seeds)
            {
                var sim = RoadToPro.Tests.EditMode.Presentation.Highlight.LiveContactAudit.Played90(seed);
                var d = sim.Context.Actions.Duels;
                worst = System.Math.Max(worst, d.MaxSlideWinBallDist);
                wins += d.SlideWins;
            }
            TestContext.WriteLine($"이긴 슬라이딩 {wins}건, 태클러–공 최대 {worst:0.000} m");
            Assert.Greater(wins, 0L);
            Assert.LessOrEqual(worst, 2.2f + 1e-4f);
        }
    }
}
