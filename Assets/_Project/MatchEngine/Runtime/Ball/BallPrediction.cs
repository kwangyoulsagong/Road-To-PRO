using System;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 궤적 예측 결과 버퍼 (FR-30·31). 호출자가 만들어 재사용한다 (예측 중 할당 0).
    /// 샘플 i의 시각 = (i+1) × 샘플 간격. 공이 멈추거나 컨트롤되면 그 시점에서 샘플이 끝난다.
    /// </summary>
    public sealed class BallPrediction
    {
        public readonly float[] Times;
        public readonly Vec3[] Positions;
        public readonly Vec3[] Velocities;
        public readonly BallPhase[] Phases;
        internal readonly BallEventBuffer Scratch = new BallEventBuffer();

        public BallPrediction(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Times = new float[capacity];
            Positions = new Vec3[capacity];
            Velocities = new Vec3[capacity];
            Phases = new BallPhase[capacity];
        }

        public int Capacity => Times.Length;
        public int Count { get; internal set; }

        public bool HasLanding { get; internal set; }
        public Vec3 LandingPoint { get; internal set; }
        public float LandingTime { get; internal set; }

        public bool HasRest { get; internal set; }
        public Vec3 RestPoint { get; internal set; }
        public float RestTime { get; internal set; }

        public bool HasBoundary { get; internal set; }
        public BallBoundary Boundary { get; internal set; }
        public Vec3 BoundaryPoint { get; internal set; }
        public float BoundaryTime { get; internal set; }

        internal void Reset()
        {
            Count = 0;
            HasLanding = HasRest = HasBoundary = false;
            LandingPoint = RestPoint = BoundaryPoint = Vec3.Zero;
            LandingTime = RestTime = BoundaryTime = 0f;
            Boundary = BallBoundary.None;
        }
    }
}
