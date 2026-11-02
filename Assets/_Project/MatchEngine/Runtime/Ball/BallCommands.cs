using System;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 외부 입력 (specs/004 FR-10·13). 누가 어떤 속도·스핀으로 차는지는 008이 정한다.
    /// </summary>
    public static class BallCommands
    {
        /// <summary>
        /// 킥: Controlled → Air(속도 z &gt; 0) 또는 Rolling(z 성분 무시). 속도·스핀이 한도를 넘으면 잘라 경고 이벤트를 남긴다.
        /// 위치가 지면 아래면 지면으로 올린다.
        /// </summary>
        public static void Kick(ref BallState s, Vec3 velocity, Vec3 spin, BallParams p, BallEventBuffer events)
        {
            if (s.Phase != BallPhase.Controlled) throw new InvalidOperationException($"킥은 Controlled 상태에서만: 현재 {s.Phase}");
            CheckFinite(velocity, nameof(velocity));
            CheckFinite(spin, nameof(spin));

            var inVel = velocity;
            var clamped = false;
            var vs = velocity.LengthSq();
            if (vs > p.LimitSpeed * p.LimitSpeed)
            {
                velocity = velocity * (p.LimitSpeed / DMath.Sqrt(vs));
                clamped = true;
            }
            var ws = spin.LengthSq();
            if (ws > p.LimitSpin * p.LimitSpin)
            {
                spin = spin * (p.LimitSpin / DMath.Sqrt(ws));
                clamped = true;
            }

            var pos = s.Position;
            if (pos.Z < p.Radius) pos = new Vec3(pos.X, pos.Y, p.Radius);

            if (clamped)
                events.Add(new BallEvent(BallEventKind.InputClamped, 0f, pos, inVel, velocity, BallPhase.Controlled, BallPhase.Controlled,
                    BallTransitionReason.None, BallBoundary.None, false, GoalFramePart.None));

            var lofted = velocity.Z > 0f;
            var prevOwnerOfKick = s.OwnerId;
            s.Phase = lofted ? BallPhase.Air : BallPhase.Rolling;
            s.Position = lofted ? pos : new Vec3(pos.X, pos.Y, p.Radius);
            s.Velocity = lofted ? velocity : new Vec3(velocity.X, velocity.Y, 0f);
            s.Spin = spin;
            s.OwnerId = BallState.NoOwner;
            events.Add(new BallEvent(BallEventKind.Transition, 0f, s.Position, default, default, BallPhase.Controlled, s.Phase,
                lofted ? BallTransitionReason.LoftedKick : BallTransitionReason.GroundKick, BallBoundary.None, false, GoalFramePart.None,
                prevOwnerOfKick, BallState.NoOwner));
        }

        /// <summary>
        /// 컨트롤 판정(008·011) 결과: Air·Rolling·Rest → Controlled. 이미 Controlled면 소유자만 바꾸고
        /// 소유자가 달라졌을 때 <see cref="BallEventKind.OwnerChanged"/>를 남긴다.
        /// </summary>
        public static void TakeControl(ref BallState s, int ownerId, Vec3 position, BallEventBuffer events)
        {
            if (ownerId < 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
            CheckFinite(position, nameof(position));
            var from = s.Phase;
            var prevOwner = s.OwnerId;
            s.Phase = BallPhase.Controlled;
            s.Position = position;
            s.Velocity = Vec3.Zero;
            s.Spin = Vec3.Zero;
            s.OwnerId = ownerId;
            if (from != BallPhase.Controlled)
                events.Add(new BallEvent(BallEventKind.Transition, 0f, position, default, default, from, BallPhase.Controlled,
                    BallTransitionReason.TakenControl, BallBoundary.None, false, GoalFramePart.None, BallState.NoOwner, ownerId));
            else if (prevOwner != ownerId)
                events.Add(new BallEvent(BallEventKind.OwnerChanged, 0f, position, default, default, BallPhase.Controlled, BallPhase.Controlled,
                    BallTransitionReason.OwnerChanged, BallBoundary.None, false, GoalFramePart.None, prevOwner, ownerId));
        }

        /// <summary>Controlled 상태의 공 위치 갱신 (소유 선수 기준 오프셋, 계산은 005/008).</summary>
        public static void SetControlledPosition(ref BallState s, Vec3 position)
        {
            if (s.Phase != BallPhase.Controlled) throw new InvalidOperationException($"Controlled 상태가 아니다: {s.Phase}");
            CheckFinite(position, nameof(position));
            s.Position = position;
        }

        static void CheckFinite(Vec3 v, string what)
        {
            if (!FloatGuard.IsFinite(v.X) || !FloatGuard.IsFinite(v.Y) || !FloatGuard.IsFinite(v.Z))
                throw new NumericException($"{what}: 유한하지 않은 입력");
        }
    }
}
