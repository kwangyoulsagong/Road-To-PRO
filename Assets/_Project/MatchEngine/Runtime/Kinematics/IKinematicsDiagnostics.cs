using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>선수 1명 서브스텝 진단 (specs/005 FR-51·52 → 040 '이동' 레이어, 054 검증).</summary>
    public readonly struct KinematicsSample
    {
        public readonly long Substep;
        public readonly int PlayerId;
        public readonly Vec2 DesiredVelocity;
        public readonly Vec2 Velocity;
        public readonly float LongAccel;
        public readonly float LatAccel;
        public readonly float FrictionUse;
        public readonly bool Pivoting;
        public readonly float PivotTurnRate;
        public readonly float W;
        public readonly float C;
        /// <summary>지연 대기 중인 명령 수 (인지 지연 상태).</summary>
        public readonly int PendingCommands;
        public readonly float SeparationCorrection;

        public KinematicsSample(long substep, int playerId, Vec2 desired, Vec2 velocity, float longAccel, float latAccel, float frictionUse,
            bool pivoting, float pivotTurnRate, float w, float c, int pending, float sepCorrection)
        {
            Substep = substep; PlayerId = playerId; DesiredVelocity = desired; Velocity = velocity;
            LongAccel = longAccel; LatAccel = latAccel; FrictionUse = frictionUse; Pivoting = pivoting; PivotTurnRate = pivotTurnRate;
            W = w; C = c; PendingCommands = pending; SeparationCorrection = sepCorrection;
        }
    }

    /// <summary>
    /// 운동학 진단 출력 (FR-51). 구현체는 시뮬 상태를 바꾸면 안 된다 (값을 in으로 받는다). null이면 호출 자체가 없다 (비용·할당 0).
    /// </summary>
    public interface IKinematicsDiagnostics
    {
        void OnPlayerSubstep(in KinematicsSample sample);
        /// <summary>명령 큐가 넘쳐 가장 오래된 미활성 명령을 버렸다.</summary>
        void OnCommandDropped(long substep, int playerId);
    }
}
