using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 AC-05: 경계 · 예외 상황 (동시 태클·늦은 태클·도달 밖·3 m 경계·이격 0·다운 중·극단 능력치·피치 밖 스냅샷).</summary>
    public class DuelEdgeCaseTests
    {
        const string SureWin = "{\"act.tackle.b0\": 3}";   // 판정 확률 상한(0.999)에 가깝게
        const string SureMiss = "{\"act.tackle.b0\": -3}"; // 하한(0.001)에 가깝게

        static void AssertNoNaN(DuelFixture f)
        {
            foreach (var e in f.Events)
                Assert.IsFalse(float.IsNaN(e.F0) || float.IsNaN(e.F1) || float.IsNaN(e.X) || float.IsNaN(e.Y), e.ToString());
        }

        static int Tackles(DuelFixture f, int bit = -1)
        {
            var n = 0;
            foreach (var e in f.Events) if (e.Kind == SimEventKind.Tackle && (bit < 0 || (e.A & bit) != 0)) n++;
            return n;
        }

        [Test]
        public void SimultaneousTackles_SlotOrder_FirstWinsSecondVoid()
        {
            var f = new DuelFixture(7080UL, SureWin);
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(51f, 34.6f), new Vec2(-1f, 0f));
            f.Put(14, new Vec2(51f, 33.4f), new Vec2(-1f, 0f));
            f.Own(9);
            Assert.IsTrue(f.Duels.ScheduleTackle(f.Ctx, 13, 9, false));
            Assert.IsTrue(f.Duels.ScheduleTackle(f.Ctx, 14, 9, false));
            var before = f.Duels.TacklesResolved;
            f.Ticks(3);
            Assert.AreEqual(before + 2, f.Duels.TacklesResolved, "둘 다 판정(RNG 고정 소비)");
            Assert.AreEqual(2, Tackles(f));
            Assert.AreEqual(1, Tackles(f, EventCodes.TackleWon), "첫 성공 뒤 두 번째는 무효");
            Tackle(f, out var first, out var second);
            Assert.AreEqual(13, f.Ctx.Squad.SlotOf(first.PlayerId), "슬롯 오름차순");
            Assert.AreNotEqual(0, first.A & EventCodes.TackleWon);
            Assert.AreEqual(0, second.A & EventCodes.TackleWon);
            Assert.AreEqual(2, f.Count(SimEventKind.Contact));
            AssertNoNaN(f);
        }

        static void Tackle(DuelFixture f, out SimEvent first, out SimEvent second)
        {
            first = default; second = default;
            var n = 0;
            foreach (var e in f.Events) if (e.Kind == SimEventKind.Tackle) { if (n++ == 0) first = e; else second = e; }
        }

        [Test]
        public void LateTackle_AfterBallLeft_ContactOnlyNoGain()
        {
            var f = new DuelFixture(7081UL, SureWin);
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(51f, 34.5f), new Vec2(-1f, 0f));
            f.Own(9);
            f.Tick();
            Assert.IsTrue(f.Duels.ScheduleTackle(f.Ctx, 13, 9, true));
            // 같은 틱에 공이 떠남 (빠른 패스)
            f.Ctx.Ball = BallState.RollingAt(f.Ctx.Ball.Position.XY, new Vec2(0f, 15f), f.Ctx.BallParams.Radius);
            f.Ctx.LastKickSlot = 9; f.Ctx.LastKickSubstep = f.Ctx.Kinematics.Substep; f.Ctx.LastKickKind = (byte)BallActionKind.Pass; f.Ctx.LastTouchTeam = 0;
            f.Ticks(4);
            Assert.AreEqual(1, Tackles(f, EventCodes.TackleLate), "늦은 태클 1건");
            Assert.AreEqual(0, Tackles(f, EventCodes.TackleWon), "공 획득 없음");
            var c = f.Ctx.Actions.Details.Contacts;
            Assert.AreEqual(1, c.Count);
            Assert.Greater(c[0].Lateness, 0f);
            Assert.IsTrue(f.Ctx.Kinematics.IsImmobile(13), "늦은 슬라이딩도 빗나감 → 다운");
            AssertNoNaN(f);
        }

        [Test]
        public void CarrierOutOfReach_NoTackleNoContact()
        {
            var f = new DuelFixture(7082UL, SureWin);
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(55f, 34f), new Vec2(-1f, 0f));
            f.Own(9);
            Assert.IsTrue(f.Duels.ScheduleTackle(f.Ctx, 13, 9, false));
            f.Ticks(10);
            Assert.AreEqual(0, Tackles(f));
            Assert.AreEqual(0, f.Count(SimEventKind.Contact));
            Assert.IsFalse(f.Duels.TackleOf(13).Active, "만료 취소");
            Assert.IsFalse(f.Duels.TryTackleAttempt(f.Ctx, 13, 9, 1f, new SeedRng(1)), "도달 거리 밖은 시도하지 않음");
        }

        [Test]
        public void FarRecovery_IsInterceptOrRecovery_NotTackle()
        {
            Assert.IsFalse(ActionOutcome.IsTackle(9f, TackleModel.Reach(ActionTestKit.P, false), true), "3 m 떨어진 캐리어 → 태클 아님");
            Assert.IsFalse(ActionOutcome.IsTackle(0.5f, TackleModel.Reach(ActionTestKit.P, false), false), "캐리어가 이미 공을 잃음 → 태클 아님");
        }

        [Test]
        public void TakeOnBoundary_Exactly3m_NotTriggered()
        {
            var f = new DuelFixture();
            var goalX = f.Ctx.OpponentGoal(0).X;
            var dir = goalX > 52f ? 1f : -1f;
            f.Put(9, new Vec2(50f, 34f), new Vec2(dir, 0f));
            f.Put(13, new Vec2(50f + 3f * dir, 34f), new Vec2(-dir, 0f));
            Assert.IsFalse(f.Duels.TakeOnTriggered(f.Ctx, 9, 13));
            f.Put(13, new Vec2(50f + 2.9f * dir, 34f), new Vec2(-dir, 0f));
            Assert.IsTrue(f.Duels.TakeOnTriggered(f.Ctx, 9, 13));
        }

        [Test]
        public void GapZero_NoSteal_ShieldOnly()
        {
            var f = new DuelFixture(7083UL);
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(50.6f, 34.4f), new Vec2(-1f, 0f));
            f.Own(9);
            f.Ticks(20);
            Assert.AreEqual(0, f.Duels.Touches);
            Assert.AreEqual(0, f.Duels.Steals);
            Assert.Greater(f.Duels.ShieldChecks, 0, "정지 볼 보호 → 몸싸움 판정");
            Assert.AreEqual(f.Duels.ShieldChecks, f.Count(SimEventKind.Duel));
            AssertNoNaN(f);
        }

        [Test]
        public void Downed_CannotAttempt_CanBeTarget_CannotClaim()
        {
            var f = new DuelFixture(7084UL, SureWin);
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(51f, 34.5f), new Vec2(-1f, 0f));
            f.Own(9);
            var k = f.Ctx.Kinematics;
            k.SetImmobile(13, k.Substep + 200);
            Assert.IsFalse(f.Duels.ScheduleTackle(f.Ctx, 13, 9, false), "다운 중 시도 불가");
            Assert.IsFalse(f.Duels.TryTackleAttempt(f.Ctx, 13, 9, 1f, new SeedRng(1)));
            k.SetImmobile(9, k.Substep + 200);
            f.Put(14, new Vec2(51f, 33.5f), new Vec2(-1f, 0f));
            Assert.IsTrue(f.Duels.ScheduleTackle(f.Ctx, 14, 9, false), "다운 선수는 대상 가능");

            // 다운 선수는 느슨한 공을 잡지 않는다
            var g = new DuelFixture(7085UL);
            g.Put(13, new Vec2(60f, 34f), new Vec2(1f, 0f));
            g.Ctx.Kinematics.SetImmobile(13, g.Ctx.Kinematics.Substep + 300);
            g.Ctx.Ball = BallState.RollingAt(new Vec2(60.3f, 34f), new Vec2(0.2f, 0f), g.Ctx.BallParams.Radius);
            g.Ctx.LastTouchTeam = 0; g.Ctx.LastKickSlot = -1;
            g.Ticks(10);
            Assert.AreNotEqual(BallPhase.Controlled, g.Ctx.Ball.Phase, "다운 중 공 잡기 없음");
        }

        [Test]
        public void PendingTackle_CancelledWhenTacklerDownedOrOwnsBall()
        {
            var f = new DuelFixture(7086UL, SureWin);
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(51f, 34.5f), new Vec2(-1f, 0f));
            f.Own(9);
            Assert.IsTrue(f.Duels.ScheduleTackle(f.Ctx, 13, 9, false));
            f.Ctx.Kinematics.SetImmobile(13, f.Ctx.Kinematics.Substep + 40); // 판정 전에 다운 (예: 테이크온에 제쳐짐)
            f.Ticks(3);
            Assert.AreEqual(0, Tackles(f), "다운된 태클러의 예약 태클은 취소 (결정론 리뷰 M1)");

            var g = new DuelFixture(7087UL, SureWin);
            g.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            g.Put(13, new Vec2(51f, 34.5f), new Vec2(-1f, 0f));
            g.Own(9);
            Assert.IsTrue(g.Duels.ScheduleTackle(g.Ctx, 13, 9, false));
            g.Own(13); // 판정 전에 태클러가 공을 가짐 (예: 이격 탈취)
            g.Ticks(3);
            Assert.AreEqual(0, Tackles(g), "공을 가진 태클러의 예약 태클은 취소");
            Assert.AreEqual(0, g.Count(SimEventKind.Contact));
        }

        [Test]
        public void ExtremeAttributes_ProbClamped()
        {
            var p = ActionTestKit.P;
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 5, new Vec2(60f, 34f));
            ActionTestKit.Put(w, 16, new Vec2(61f, 34f));
            w.SetDuel(5, TechnicalAttributes.Average.WithDuel(dribbling: 100f, strength: 100f), RoadToPro.MatchEngine.Kinematics.KinematicAttributes.Uniform(20), 100f, 3);
            w.SetDuel(16, TechnicalAttributes.Average.WithDuel(tackling: 0f, positioning: 0f, strength: 0f), RoadToPro.MatchEngine.Kinematics.KinematicAttributes.Uniform(1), 0f, 0);
            foreach (var v in new[]
            {
                TakeOnModel.Probability(p, w, 5, 16, 1f, 0f, 0f), TakeOnModel.Probability(p, w, 16, 5, 0f, 1f, 1f),
                TackleModel.WinProbability(p, w, 16, 5, 0f, 1f, true), TackleModel.WinProbability(p, w, 5, 16, 1f, 0f, false),
                ShieldModel.KeepProbability(p, w, 5, 16), ShieldModel.KeepProbability(p, w, 16, 5),
            })
                Assert.That(v, Is.InRange(p.ProbMin, p.ProbMax));
        }
    }
}
