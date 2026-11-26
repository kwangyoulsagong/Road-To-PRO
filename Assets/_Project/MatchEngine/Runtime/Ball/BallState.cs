using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 공 상태 값 (specs/004). 위치 x·y = 피치 미터(0..길이, 0..폭), z = 공 중심 높이(m). 스핀은 rad/s 벡터.
    /// Phase는 <see cref="BallPhase.Bounce"/>가 될 수 없다. 직렬화 가능한 값만 가진다 (세이브 호환).
    /// </summary>
    public struct BallState
    {
        public const int NoOwner = -1;

        public BallPhase Phase;
        public Vec3 Position;
        public Vec3 Velocity;
        public Vec3 Spin;
        public int OwnerId;
        /// <summary>
        /// 골문으로 들어와 골 안에 있는 공의 골 쪽 (specs/059 FR-41 ⑥ 그물 주머니): −1 x = 0 골, +1 x = 길이 골, 0 아님.
        /// 골 안에서 그물을 밀고 들어간 공과 골문 밖으로 빗나가 그물 바깥을 지나는 공을 가른다. 피치로 돌아오면 0.
        /// </summary>
        public sbyte NetSide;

        public static BallState AtRest(Vec2 xy, float radius) => new BallState
        {
            Phase = BallPhase.Rest,
            Position = new Vec3(xy, radius),
            OwnerId = NoOwner,
        };

        public static BallState Controlled(int ownerId, Vec3 position) => new BallState
        {
            Phase = BallPhase.Controlled,
            Position = position,
            OwnerId = ownerId,
        };

        public static BallState InAir(Vec3 position, Vec3 velocity, Vec3 spin) => new BallState
        {
            Phase = BallPhase.Air,
            Position = position,
            Velocity = velocity,
            Spin = spin,
            OwnerId = NoOwner,
        };

        public static BallState RollingAt(Vec2 xy, Vec2 velocity, float radius) => new BallState
        {
            Phase = BallPhase.Rolling,
            Position = new Vec3(xy, radius),
            Velocity = new Vec3(velocity, 0f),
            OwnerId = NoOwner,
        };

        /// <summary>해시 입력 순서 고정: Phase, Position, Velocity, Spin, OwnerId, NetSide(059).</summary>
        public void AddTo(ref StableHasher h)
        {
            h.Add((byte)Phase);
            h.Add(Position.X); h.Add(Position.Y); h.Add(Position.Z);
            h.Add(Velocity.X); h.Add(Velocity.Y); h.Add(Velocity.Z);
            h.Add(Spin.X); h.Add(Spin.Y); h.Add(Spin.Z);
            h.Add(OwnerId);
            h.Add((byte)NetSide);
        }
    }
}
