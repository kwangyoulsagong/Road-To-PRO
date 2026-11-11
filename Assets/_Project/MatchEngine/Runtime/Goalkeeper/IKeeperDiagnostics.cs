using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>
    /// GK 진단 출력 (specs/011 FR-82 → 040 FR-30 'GK 위험도' 레이어). 읽기 전용 통보 — 시뮬 결과·RNG 소비를 바꾸지 않는다.
    /// 싱크가 없으면(null) 호출도 할당도 없다.
    /// </summary>
    public interface IKeeperDiagnostics
    {
        /// <summary>위험도 후보 점 하나 (목적 함수 값 포함).</summary>
        void OnRiskCandidate(int team, Vec2 point, float risk, float objective);
        /// <summary>이번 평가의 최종 목표점과 각 이등분선(공 → 골라인 위 이등분점).</summary>
        void OnTarget(int team, Vec2 target, Vec2 bisectorFrom, Vec2 bisectorTo);
    }
}
