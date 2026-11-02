using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 공 진단 출력 (specs/004 FR-43 → 040 '공 궤적' 레이어). 상태·100Hz 궤적 점·스핀 축/크기는 <see cref="OnSubstep"/>,
    /// 바운스 지점 등은 <see cref="OnEvent"/>, 예측 착지점은 <see cref="OnPredictedLanding"/>.
    /// 구현체는 시뮬 상태를 바꾸면 안 된다 (값을 in으로 받는다). null이면 호출 자체가 없다 (비용·할당 0).
    /// </summary>
    public interface IBallDiagnostics
    {
        void OnSubstep(in BallState state, in BallSegment segment);
        void OnEvent(in BallEvent e);
        void OnPredictedLanding(Vec3 point, float timeSeconds);
    }
}
