using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 선수 운동 상태 (specs/005). 값 타입. 직렬화·해시 순서는 <see cref="AddTo"/> (plan 데이터·스키마).
    /// </summary>
    public struct PlayerMotionState
    {
        public int Id;
        public Vec2 Position;
        public Vec2 Velocity;
        /// <summary>몸이 향한 방향 (단위 벡터). 피벗이 아니면 속도 방향과 같다.</summary>
        public Vec2 Facing;
        /// <summary>저속 피벗 상태 (히스테리시스, FR-12).</summary>
        public bool Pivoting;
        /// <summary>급성 W′ (0~1, 1 = 가득), FR-30.</summary>
        public float W;
        /// <summary>만성 C (0~1, 0 = 생생), FR-31.</summary>
        public float C;
        /// <summary>현재 활성 명령 (지연이 지난 것).</summary>
        public SteeringCommand Active;

        public void AddTo(ref StableHasher h)
        {
            h.Add(Id);
            h.Add(Position.X); h.Add(Position.Y);
            h.Add(Velocity.X); h.Add(Velocity.Y);
            h.Add(Facing.X); h.Add(Facing.Y);
            h.Add(Pivoting);
            h.Add(W); h.Add(C);
            Active.AddTo(ref h);
        }
    }

    /// <summary>선수 초기 설정 (경기 시작·교체 투입). 교체 선수는 W = 1, C = 0 (FR-35).</summary>
    public readonly struct PlayerSetup
    {
        public readonly int Id;
        public readonly KinematicAttributes Attributes;
        public readonly Vec2 Position;
        public readonly Vec2 Velocity;
        public readonly Vec2 Facing;
        public readonly float W;
        public readonly float C;

        public PlayerSetup(int id, in KinematicAttributes attributes, Vec2 position, Vec2 velocity = default, Vec2 facing = default, float w = 1f, float c = 0f)
        {
            Id = id; Attributes = attributes; Position = position; Velocity = velocity;
            var f = facing.Normalized();
            if (f == Vec2.Zero) f = velocity.Normalized();
            if (f == Vec2.Zero) f = new Vec2(1f, 0f);
            Facing = f;
            W = w; C = c;
        }
    }
}
