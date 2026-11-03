using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>조향 행동 종류 (specs/005 FR-20~26).</summary>
    public enum SteeringKind : byte
    {
        /// <summary>멈춤 (대형 앵커가 있으면 대형 추종만).</summary>
        Idle = 0,
        Seek = 1,
        Arrive = 2,
        PursuePlayer = 3,
        /// <summary>공 인터셉트 지점으로 전력 Arrive (지점은 결정 틱이 <see cref="TimeToIntercept.FindBallIntercept"/>로 구한다).</summary>
        InterceptBall = 4,
        Evade = 5,
        /// <summary>마킹: 상대와 우리 골대를 잇는 선 위, 상대에서 1~2 m (FR-25).</summary>
        OffsetPursuit = 6,
        /// <summary>저킹: 공격수와 자기 골문 사이 1.5~2.5 m, 측면 오프셋 (FR-26).</summary>
        OffsetArrive = 7,
        /// <summary>대형 위치 추종 (낮은 우선순위 Arrive).</summary>
        Formation = 8,
    }

    /// <summary>
    /// 결정 틱(10 Hz) 입력. 서브스텝 동안 유지된다 (FR-28). 팩토리로만 만든다.
    /// </summary>
    public readonly struct SteeringCommand
    {
        public readonly SteeringKind Kind;
        /// <summary>목표점 (Seek·Arrive·Intercept·Formation) 또는 지킬 골 위치 (마킹·저킹).</summary>
        public readonly Vec2 Point;
        /// <summary>대상 선수 id (Pursue·Evade·마킹·저킹). 없으면 −1.</summary>
        public readonly int TargetId;
        /// <summary>최고 속도 상한 (m/s). 0이면 Vmax_eff.</summary>
        public readonly float MaxSpeed;
        /// <summary>저킹 측면 오프셋 방향 (+1 = 공격수→골 방향의 왼쪽, −1 = 오른쪽, 0 = 없음). 약한 발 쪽으로 몬다.</summary>
        public readonly float Side;
        /// <summary>낮은 우선순위 대형 앵커 (FR-27).</summary>
        public readonly bool HasFormation;
        public readonly Vec2 FormationPoint;

        SteeringCommand(SteeringKind kind, Vec2 point, int targetId, float maxSpeed, float side, bool hasFormation, Vec2 formation)
        {
            Kind = kind; Point = point; TargetId = targetId; MaxSpeed = maxSpeed < 0f ? 0f : maxSpeed; Side = DMath.Sign(side);
            HasFormation = hasFormation; FormationPoint = formation;
        }

        public static SteeringCommand Idle() => new SteeringCommand(SteeringKind.Idle, Vec2.Zero, -1, 0f, 0f, false, Vec2.Zero);
        public static SteeringCommand Seek(Vec2 target, float maxSpeed = 0f) => new SteeringCommand(SteeringKind.Seek, target, -1, maxSpeed, 0f, false, Vec2.Zero);
        public static SteeringCommand Arrive(Vec2 target, float maxSpeed = 0f) => new SteeringCommand(SteeringKind.Arrive, target, -1, maxSpeed, 0f, false, Vec2.Zero);
        public static SteeringCommand Pursue(int targetId, float maxSpeed = 0f) => new SteeringCommand(SteeringKind.PursuePlayer, Vec2.Zero, targetId, maxSpeed, 0f, false, Vec2.Zero);
        public static SteeringCommand InterceptBall(Vec2 point) => new SteeringCommand(SteeringKind.InterceptBall, point, -1, 0f, 0f, false, Vec2.Zero);
        public static SteeringCommand Evade(int threatId, float maxSpeed = 0f) => new SteeringCommand(SteeringKind.Evade, Vec2.Zero, threatId, maxSpeed, 0f, false, Vec2.Zero);
        public static SteeringCommand Mark(int opponentId, Vec2 ownGoal, float maxSpeed = 0f) => new SteeringCommand(SteeringKind.OffsetPursuit, ownGoal, opponentId, maxSpeed, 0f, false, Vec2.Zero);
        public static SteeringCommand Jockey(int attackerId, Vec2 ownGoal, float weakFootSide) => new SteeringCommand(SteeringKind.OffsetArrive, ownGoal, attackerId, 0f, weakFootSide, false, Vec2.Zero);
        public static SteeringCommand Formation(Vec2 slot, float maxSpeed = 0f) => new SteeringCommand(SteeringKind.Formation, slot, -1, maxSpeed, 0f, false, Vec2.Zero);

        /// <summary>같은 명령에 낮은 우선순위 대형 앵커를 붙인다.</summary>
        public SteeringCommand WithFormation(Vec2 slot) => new SteeringCommand(Kind, Point, TargetId, MaxSpeed, Side, true, slot);

        public void AddTo(ref StableHasher h)
        {
            h.Add((byte)Kind);
            h.Add(Point.X); h.Add(Point.Y);
            h.Add(TargetId);
            h.Add(MaxSpeed);
            h.Add(Side);
            h.Add(HasFormation);
            h.Add(FormationPoint.X); h.Add(FormationPoint.Y);
        }
    }
}
