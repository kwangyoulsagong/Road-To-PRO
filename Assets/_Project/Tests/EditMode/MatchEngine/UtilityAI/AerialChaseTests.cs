using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/081 AC-11: 뜬 공 착지점 추격 (FR-24·25).</summary>
    public class AerialChaseTests
    {
        static readonly Vec2 Land = new Vec2(90f, 34f);

        /// <summary>모든 선수를 빼고 지정한 선수만 둔다. 팀 0이 +x로 공격, 킥은 팀 0.</summary>
        static DecisionFixture Scene()
        {
            var f = new DecisionFixture();
            for (int s = 0; s < DecisionWorld.N; s++) f.W.Active[s] = false;
            void P(int s, float x, float y) { f.W.Active[s] = true; f.W.Pos[s] = new Vec2(x, y); f.W.Vel[s] = Vec2.Zero; }
            P(9, 86f, 34f); P(10, 80f, 34f); P(7, 70f, 34f);          // 팀 0: TTI 9 < 10 < 7
            P(14, 93f, 35f); P(12, 100f, 30f); P(13, 101f, 38f);     // 팀 1: 13이 최후방(골문 105에 가장 가까움)
            f.Refresh();
            return f;
        }

        static int[] Sel(AerialChase c, int team)
        {
            var r = new int[c.CountOf(team)];
            for (int k = 0; k < r.Length; k++) r[k] = c.At(team, k);
            return r;
        }

        [Test]
        public void AerialChase_EachTeamMinTti_MovesToLanding()
        {
            var f = Scene();
            var c = new AerialChase();
            c.Select(f.P, f.W, Land, 0, null);
            CollectionAssert.AreEqual(new[] { 9, 10 }, Sel(c, 0), "팀 0 = TTI 순 2명");
            CollectionAssert.AreEqual(new[] { 14, 12 }, Sel(c, 1), "팀 1 = 최후방 수비수(13) 제외 TTI 순 2명");
            Assert.IsTrue(c.IsChaser(9));
            Assert.IsFalse(c.IsChaser(7));
            Assert.IsFalse(c.IsChaser(13));
            Assert.AreEqual(Land.X, c.Landing.X, 1e-5f);
        }

        [Test]
        public void AerialChase_LandingOutOfPitch_None()
        {
            var f = Scene();
            var c = new AerialChase();
            c.Select(f.P, f.W, new Vec2(f.W.PitchL + 2f, 34f), 0, null);
            Assert.AreEqual(0, c.CountOf(0));
            Assert.AreEqual(0, c.CountOf(1));
            c.Select(f.P, f.W, new Vec2(60f, -1f), 0, null);
            Assert.AreEqual(0, c.CountOf(0) + c.CountOf(1));
        }

        [Test]
        public void AerialChase_ReceiverOrWinner_NoDuplicate()
        {
            // 받는 선수(008 T14a)·이동 불가 등 단계가 넘긴 제외 슬롯은 고르지 않고, 인원을 다음 선수로 채운다
            var f = Scene();
            var ex = new bool[DecisionWorld.N];
            ex[9] = true;
            var c = new AerialChase();
            c.Select(f.P, f.W, Land, 0, ex);
            CollectionAssert.AreEqual(new[] { 10, 7 }, Sel(c, 0));
            Assert.IsFalse(c.IsChaser(9));
        }

        [Test]
        public void AerialChase_KeeperOnlyInOwnBox()
        {
            var f = Scene();
            f.W.Active[11] = true; f.W.Pos[11] = new Vec2(103f, 34f); // 팀 1 GK
            f.Refresh();
            var c = new AerialChase();
            c.Select(f.P, f.W, new Vec2(100f, 34f), 0, null);    // 팀 1 박스 안
            Assert.IsTrue(c.IsChaser(11), "자기 박스 안 착지점 → GK 후보");
            f.W.Pos[11] = new Vec2(85f, 34f);                     // 팀 1 박스(골라인 105에서 16.5 m) 밖 착지점 바로 옆
            f.Refresh();
            c.Select(f.P, f.W, new Vec2(84f, 34f), 0, null);
            Assert.IsFalse(c.IsChaser(11), "박스 밖 착지점이면 GK 제외");
        }

        [Test]
        public void AerialChase_FarPlayersAndZeroChasers()
        {
            var f = Scene();
            var c = new AerialChase();
            c.Select(f.P, f.W, new Vec2(20f, 34f), 0, null); // 모두 25 m 넘게 먼 착지점
            Assert.AreEqual(0, c.CountOf(0) + c.CountOf(1));
        }

        // ── 경기 파이프라인 (010 결정 단계 + 071 경합) ──

        static MatchSim InPlaySim()
        {
            var sim = ShapeTestKit.Sim(setup: ShapeTestKit.Setup(seed: 8111UL));
            while (!sim.Context.Flow.InPlay) sim.StepTick();
            sim.StepTicks(20);
            var ctx = sim.Context;
            for (int s = 0; s < Squad.Slots; s++)
                if (ctx.IsSlotActive(s))
                {
                    ctx.Kinematics.Place(s, new Vec2(2f + (s % 11) * 1.5f, s < 11 ? 1f : 67f), new Vec2(1f, 0f));
                    ctx.Kinematics.SetCommandImmediate(s, SteeringCommand.Idle());
                }
            return sim;
        }

        static UtilityDecisionStage StageOf(MatchSim sim)
        {
            var st = sim.Stage(StageId.ScenarioOrUtility);
            if (st is ScenarioStage sc) return (UtilityDecisionStage)sc.DecisionStage;
            return (UtilityDecisionStage)st;
        }

        /// <summary>
        /// 팀 0 공격 방향으로 날아가 약 2.6 s 뒤 착지하는 높은 크로스를 예약하고, 공격수 9·수비수 14를 (항력 포함) 예측 착지점 5 m 앞뒤에,
        /// 팀 1 최후방 수비수 13을 그 뒤에 둔다 (AC-11: 양 팀 선수가 착지점 6 m 안).
        /// </summary>
        static void LaunchCross(MatchSim sim, out Vec2 land)
        {
            var ctx = sim.Context;
            var dir = ctx.AttackDir(0);
            var goal = ctx.OpponentGoal(0);
            var aim = new Vec2(goal.X - dir * 14f, goal.Y);
            ctx.Kinematics.Place(7, new Vec2(aim.X - dir * 22f, 8f), new Vec2(dir, 0f));
            var start = new Vec3(aim.X - dir * 20f, aim.Y, 2f);
            ctx.Ball = BallState.InAir(start, new Vec3(dir * 8f, 0f, 12f), Vec3.Zero); // 체공 ≈ 2.6 s (높은 크로스)
            ctx.LastTouchTeam = 0;
            ctx.LastTouchSlot = 7;
            ctx.LastKickSlot = 7;
            ctx.LastKickKind = (byte)BallActionKind.Pass;
            ctx.LastKickSubstep = ctx.Kinematics.Substep;
            ctx.Actions.Aerials.OnLoftedKick(ctx, 7, BallActionKind.Pass, (byte)((int)CrossType.Lofted + 1));
            Assert.IsTrue(ctx.Actions.Aerials.TryLanding(ctx, out land, out _, out _));
            ctx.Kinematics.Place(9, land + new Vec2(-dir * 5f, 0f), new Vec2(dir, 0f));
            ctx.Kinematics.Place(14, land + new Vec2(dir * 5f, 1f), new Vec2(-dir, 0f));
            ctx.Kinematics.Place(13, land + new Vec2(dir * 9f, -4f), new Vec2(-dir, 0f));
            ctx.Actions.InvalidateWorld();
        }

        [Test]
        public void AerialChase_BothTeamsContest_HeaderOccurs()
        {
            var sim = InPlaySim();
            var ctx = sim.Context;
            LaunchCross(sim, out var land);
            Assert.IsTrue(ctx.Actions.Aerials.IsArmed);
            var both0 = ctx.Actions.Aerials.BothTeamContests;
            var head0 = ctx.Actions.Aerials.Headers + ctx.Actions.Aerials.KeeperWins;
            var pred = land;
            sim.StepTick();
            var st = StageOf(sim);
            foreach (var s in new[] { 9, 14 })
            {
                var d = st.DecisionOf(s);
                Assert.AreEqual(DecisionKind.MoveTo, d.Kind, $"슬롯 {s} 착지점으로");
                Assert.Less((d.Point - pred).Length(), 1f, $"슬롯 {s} 목표 = 예측 착지점 (항력 포함)");
            }
            for (int i = 0; i < 40 && ctx.Flow.InPlay; i++) sim.StepTick();
            Assert.Greater(ctx.Actions.Aerials.BothTeamContests, both0, "양 팀 참여 경합");
            Assert.Greater(ctx.Actions.Aerials.Headers + ctx.Actions.Aerials.KeeperWins, head0, "헤더(또는 GK 처리)");
        }

        [Test]
        public void AerialChase_EndsOnDecideOrCancel()
        {
            var sim = InPlaySim();
            var ctx = sim.Context;
            LaunchCross(sim, out _);
            Assert.IsTrue(ctx.Actions.Aerials.TryLanding(ctx, out _, out _, out var kt));
            Assert.AreEqual(0, kt);
            ctx.Actions.Aerials.Cancel();
            Assert.IsFalse(ctx.Actions.Aerials.TryLanding(ctx, out _, out _, out _), "취소되면 추격 끝");
        }

        [Test]
        public void AerialChase_ImmobileExcluded()
        {
            var sim = InPlaySim();
            var ctx = sim.Context;
            LaunchCross(sim, out var land);
            ctx.Kinematics.SetImmobile(9, ctx.Kinematics.Substep + 200);
            sim.StepTick();
            var d = StageOf(sim).DecisionOf(9);
            Assert.IsFalse(d.Kind == DecisionKind.MoveTo && (d.Point - land).Length() < 3f, "다운된 선수는 착지점 추격 안 함");
        }
    }
}
