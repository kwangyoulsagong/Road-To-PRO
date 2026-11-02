using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    public enum BallEventKind : byte
    {
        /// <summary>상태 전이 (From → To, Reason).</summary>
        Transition = 0,
        /// <summary>지면 바운스: 위치·입사 속도·반발 속도 (FR-12).</summary>
        Bounce = 1,
        /// <summary>골포스트·크로스바 접촉 (FR-40).</summary>
        GoalFrameHit = 2,
        /// <summary>공 전체가 골라인·터치라인을 넘음 (FR-41).</summary>
        BoundaryCrossed = 3,
        /// <summary>킥 입력이 한도를 넘어 잘림 (경고).</summary>
        InputClamped = 4,
        /// <summary>Controlled 상태에서 소유 선수만 바뀜 (From = To = Controlled). 리뷰 R-1.</summary>
        OwnerChanged = 5,
        /// <summary>릴리스 빌드에서 NaN·무한대를 보정함 (디버그는 예외). 리뷰 R-5.</summary>
        NumericSanitized = 6,
    }

    public enum BallBoundary : byte
    {
        None = 0,
        /// <summary>x = 0 골라인.</summary>
        GoalLineLow = 1,
        /// <summary>x = 피치 길이 골라인.</summary>
        GoalLineHigh = 2,
        /// <summary>y = 0 터치라인.</summary>
        TouchlineLow = 3,
        /// <summary>y = 피치 폭 터치라인.</summary>
        TouchlineHigh = 4,
    }

    public enum GoalFramePart : byte
    {
        None = 0,
        /// <summary>y가 작은 쪽 포스트.</summary>
        PostLow = 1,
        /// <summary>y가 큰 쪽 포스트.</summary>
        PostHigh = 2,
        Crossbar = 3,
    }

    /// <summary>
    /// 공 이벤트 (specs/004). <see cref="Fraction"/>은 서브스텝 안 시각 비율 [0, 1].
    /// 필요 없는 필드는 기본값.
    /// </summary>
    public readonly struct BallEvent
    {
        public readonly BallEventKind Kind;
        public readonly float Fraction;
        public readonly Vec3 Position;
        public readonly Vec3 VelocityIn;
        public readonly Vec3 VelocityOut;
        public readonly BallPhase From;
        public readonly BallPhase To;
        public readonly BallTransitionReason Reason;
        public readonly BallBoundary Boundary;
        /// <summary>골라인 통과 시 공 중심이 골문 안(포스트 사이, 크로스바 아래)이었는지. 골 판정은 012.</summary>
        public readonly bool InGoalMouth;
        /// <summary>골대 접촉 부위. 골라인 쪽은 <see cref="Boundary"/>(GoalLineLow/High)로.</summary>
        public readonly GoalFramePart Part;
        /// <summary>컨트롤 관련 이벤트(TakenControl·OwnerChanged)의 이전·새 소유자 id. 그 밖엔 −1 (리뷰 NR-2).</summary>
        public readonly int OwnerFrom;
        public readonly int OwnerTo;

        public BallEvent(BallEventKind kind, float fraction, Vec3 position, Vec3 velocityIn, Vec3 velocityOut,
            BallPhase from, BallPhase to, BallTransitionReason reason, BallBoundary boundary, bool inGoalMouth, GoalFramePart part,
            int ownerFrom = BallState.NoOwner, int ownerTo = BallState.NoOwner)
        {
            OwnerFrom = ownerFrom;
            OwnerTo = ownerTo;
            Kind = kind;
            Fraction = fraction;
            Position = position;
            VelocityIn = velocityIn;
            VelocityOut = velocityOut;
            From = from;
            To = to;
            Reason = reason;
            Boundary = boundary;
            InGoalMouth = inGoalMouth;
            Part = part;
        }

        public static BallEvent Transition(float fraction, Vec3 pos, BallPhase from, BallPhase to, BallTransitionReason reason) =>
            new BallEvent(BallEventKind.Transition, fraction, pos, default, default, from, to, reason, BallBoundary.None, false, GoalFramePart.None);
    }
}
