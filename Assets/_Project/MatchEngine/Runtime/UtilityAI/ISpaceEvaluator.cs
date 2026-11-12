using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 공간 평가 (specs/010 FR-04·18, 007 피치 컨트롤·xT 자리). 007 구현 전에는 <see cref="StandInSpace"/>.
    /// <see cref="Control"/>은 피치 컨트롤 지점 질의(007 FR-10)라 횟수를 센다 — 침투 목표점에만 쓴다(FR-04).
    /// </summary>
    public interface ISpaceEvaluator
    {
        /// <summary>선수 s가 지점 p에 먼저 닿을 확률 근사 (0~1). 질의 횟수에 들어간다.</summary>
        float Control(DecisionWorld w, int s, Vec2 p);
        /// <summary>팀 관점 지점 위협도 (xT 대체, 0~1). 질의 아님.</summary>
        float Threat(DecisionWorld w, int team, Vec2 p);
        /// <summary>지점 주변 여유 (상대 최근접 거리 기반, 0~1). 질의 아님.</summary>
        float Openness(DecisionWorld w, int s, Vec2 p, float delay);
        int QueryCount { get; }
        void ResetQueries();
    }
}
