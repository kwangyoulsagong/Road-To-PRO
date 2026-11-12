using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/008 고정 상황: 실경기 컨텍스트를 인플레이까지 돌린 뒤 선수·공을 배치하고 액션 시스템(예약 킥·수신)을 서브스텝 단위로 직접 부른다.
    /// 단계 밖에서 낸 이벤트는 임시 로그로 비운다.
    /// </summary>
    public sealed class ActionFixture
    {
        public readonly MatchSim Sim;
        public MatchContext Ctx => Sim.Context;
        public readonly System.Collections.Generic.List<SimEvent> Events = new System.Collections.Generic.List<SimEvent>();
        long _abs;

        public ActionFixture(ulong seed = 8008UL)
        {
            Sim = MatchTestKit.Sim(seed);
            while (!Ctx.Flow.InPlay) Sim.StepTick();
            Sim.StepTicks(20);
            Ctx.Publisher.Clear();
            _abs = Ctx.Kinematics.Substep + 1000;
            ClearPitch();
        }

        /// <summary>모든 선수를 피치 구석으로 치운다 (서로·공과 멀리).</summary>
        public void ClearPitch()
        {
            for (int s = 0; s < Squad.Slots; s++)
                if (Ctx.IsSlotActive(s)) Ctx.Kinematics.Place(s, new Vec2(2f + (s % 11) * 1.5f, s < 11 ? 1f : 67f), new Vec2(1f, 0f));
            Ctx.Actions.InvalidateWorld();
        }

        public void Put(int slot, Vec2 pos, Vec2 facing)
        {
            Ctx.Kinematics.Place(slot, pos, facing);
            Ctx.Actions.InvalidateWorld();
        }

        public void Loose(Vec2 pos, Vec2 vel, int lastTeam, BallActionKind lastKick, int lastKicker)
        {
            Ctx.Ball = BallState.RollingAt(pos, vel, Ctx.BallParams.Radius);
            Ctx.LastTouchTeam = lastTeam;
            Ctx.LastTouchSlot = lastKicker;
            Ctx.LastKickKind = (byte)lastKick;
            Ctx.LastKickSlot = lastKicker;
            Ctx.LastKickSubstep = _abs - 1000;
        }

        public void Own(int slot)
        {
            var p = Ctx.SlotState(slot);
            var bp = p.Position + p.Facing * 0.45f;
            BallCommands.TakeControl(ref Ctx.Ball, slot, new Vec3(bp.X, bp.Y, Ctx.BallParams.Radius), Ctx.BallEvents);
            Ctx.LastTouchTeam = Squad.TeamOfSlot(slot);
            Ctx.LastTouchSlot = slot;
        }

        /// <summary>수신 판정을 n 서브스텝 (공은 004로 적분). 잡히면 멈춘다.</summary>
        public void RunReception(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _abs++;
                Ctx.BallEvents.Clear();
                if (Ctx.Ball.Phase != BallPhase.Controlled) BallStepper.Step(ref Ctx.Ball, Ctx.BallParams, Ctx.BallEvents, out _);
                Ctx.Actions.Resolve(Ctx, _abs, i % 10);
                if (Ctx.Ball.Phase == BallPhase.Controlled) break;
            }
            Flush();
        }

        /// <summary>공 동작을 예약하고 n 서브스텝 동안 실행 판정.</summary>
        public bool Kick(in BallAction a, int n)
        {
            _abs++;
            var ok = Ctx.Actions.Schedule(Ctx, in a, _abs);
            for (int i = 0; i < n && Ctx.Actions.Pending.Active; i++)
            {
                _abs++;
                Ctx.Actions.OnSubstep(Ctx, _abs, i % 10);
            }
            Flush();
            return ok;
        }

        public void Flush()
        {
            var log = new SimEventLog(256);
            Ctx.Publisher.Flush(log, null);
            for (int i = 0; i < log.Count; i++) Events.Add(log[i]);
        }

        public int Count(SimEventKind k)
        {
            var c = 0;
            foreach (var e in Events) if (e.Kind == k) c++;
            return c;
        }
    }
}
