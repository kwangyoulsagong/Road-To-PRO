using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-19: 접촉 서브스텝·도달 거리·부위·006 FR-82 필드, 예약 킥 취소.</summary>
    public class KickContactTests
    {
        sealed class ContactProbe : IActionDiagnostics
        {
            readonly MatchContext _ctx;
            public readonly List<(SimEventKind kind, float dist, int sub, bool hands)> Contacts = new List<(SimEventKind, float, int, bool)>();
            public ContactProbe(MatchContext ctx) => _ctx = ctx;
            public void OnPassCandidate(long tick, int passer, int receiver, in PassOption option, float deltaXt, float pressure, float score) { }
            public void OnPassChosen(long tick, int passer, int receiver, in PassOption option) { }
            public void OnContact(long tick, int substep, int slot, SimEventKind kind, float value)
            {
                var d = (_ctx.Ball.Position.XY - _ctx.SlotState(slot).Position).Length();
                var hands = Squad.IsKeeperSlot(slot) && RoadToPro.MatchEngine.Match.Stages.PhysicsStage.InOwnBox(_ctx, Squad.TeamOfSlot(slot), _ctx.Ball.Position);
                Contacts.Add((kind, d, substep, hands));
            }
        }

        static bool IsKick(SimEventKind k) => k == SimEventKind.Pass || k == SimEventKind.Shot || k == SimEventKind.Clearance || k == SimEventKind.Tackle;
        static bool IsReception(SimEventKind k) => k == SimEventKind.PassReceived || k == SimEventKind.Interception || k == SimEventKind.BallRecovered;

        [Test]
        public void ContactWithinReach_SubstepsSpread_FieldsFilled()
        {
            var p = ActionTestKit.P;
            var hist = new int[10];
            var kicks = 0;
            for (ulong seed = 50; kicks < 1000 && seed < 60; seed++)
            {
                var sim = MatchTestKit.Sim(seed);
                var probe = new ContactProbe(sim.Context);
                sim.Context.Actions.Diagnostics = probe;
                sim.RunToEnd();
                foreach (var c in probe.Contacts)
                {
                    if (IsKick(c.kind))
                    {
                        var reach = c.kind == SimEventKind.Tackle ? sim.Context.Params.TackleRange : p.KickReach;
                        Assert.LessOrEqual(c.dist, reach + 1e-4f, $"{c.kind} 접촉 거리");
                        kicks++;
                    }
                    else if (IsReception(c.kind))
                        Assert.LessOrEqual(c.dist, p.CtrlRadius * (c.hands ? p.CtrlKeeperMul : 1f) + 1e-4f, $"{c.kind} 컨트롤 거리");
                }
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    ref readonly var e = ref log[i];
                    if (!IsKick(e.Kind) && !IsReception(e.Kind)) continue;
                    Assert.That(e.Substep, Is.InRange(0, 9), "틱+서브스텝 시각");
                    Assert.AreNotEqual(BodyPart.None, e.Part, "사용 부위");
                    Assert.GreaterOrEqual(e.PlayerId, 0);
                    if (IsKick(e.Kind)) hist[e.Substep]++;
                    if (e.Kind == SimEventKind.Pass)
                        Assert.IsTrue(e.Part == BodyPart.LeftFoot || e.Part == BodyPart.RightFoot, "패스 = 발");
                }
            }
            Assert.GreaterOrEqual(kicks, 1000);
            var total = 0;
            for (int i = 0; i < 10; i++) { Assert.Greater(hist[i], 0, $"서브스텝 {i}에 킥 없음"); total += hist[i]; }
            Assert.Less(hist[0], total * 0.3f, "서브스텝 0에 고정되지 않음");
        }

        [Test]
        public void LostBallBeforeContact_Cancelled()
        {
            var sim = MatchTestKit.Sim(77);
            var ctx = sim.Context;
            for (int t = 0; t < 30_000 && !(ctx.Actions.Pending.Active && ctx.Flow.InPlay); t++) sim.StepTick();
            Assume.That(ctx.Actions.Pending.Active, "예약 킥을 찾지 못함");
            var kicker = ctx.Actions.Pending.Action.KickerSlot;
            var cancelled = ctx.Actions.KicksCancelled;
            var before = sim.Log.Count;
            // 공을 빼앗긴 상황: 느슨한 공으로 강제
            var b = ctx.Ball;
            sim.ForceBallState(BallState.RollingAt(b.Position.XY + new Vec2(5f, 0f), new Vec2(0.1f, 0f), ctx.BallParams.Radius));
            sim.StepTick();
            sim.StepTick();
            sim.StepTick();
            Assert.IsFalse(ctx.Actions.Pending.Active);
            Assert.AreEqual(cancelled + 1, ctx.Actions.KicksCancelled);
            for (int i = before; i < sim.Log.Count; i++)
            {
                ref readonly var e = ref sim.Log[i];
                Assert.IsFalse(IsKick(e.Kind) && e.PlayerId == ctx.PlayerAtSlot(kicker) && e.Tick <= sim.Log[before].Tick + 3, "취소된 킥 이벤트 없음");
            }
        }

        [Test]
        public void ScheduledKick_CrossesTickBoundary()
        {
            var sim = MatchTestKit.Sim(78);
            var ctx = sim.Context;
            var crossed = 0;
            for (int t = 0; t < 6_000; t++)
            {
                sim.StepTick();
                ref readonly var pk = ref ctx.Actions.Pending;
                if (pk.Active && pk.Due / 10 > pk.Tick) crossed++;
            }
            Assert.Greater(crossed, 0, "준비 시간이 틱 경계를 넘는 예약");
        }
    }
}
