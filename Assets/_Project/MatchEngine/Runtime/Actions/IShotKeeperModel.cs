using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 조준용 GK 평가 함수 자리 (specs/071 FR-13 ← 011 FR-14 P_beatGK). RNG 없음. 지금은 <see cref="StandInKeeperBeat"/> 근사,
    /// 011 <c>SaveModel.BeatProbability</c> 연결은 AC-13(셀당 004 경로 적분이라 예산 확인 뒤).
    /// </summary>
    public interface IShotKeeperModel
    {
        /// <param name="distToGoal">공–골라인 셀 중심 거리 (m).</param>
        /// <param name="cellY">셀 중심 좌우 오프셋(골문 중앙 기준, m).</param>
        /// <param name="cellZ">셀 중심 높이 (m).</param>
        /// <param name="gkY">GK 좌우 오프셋(골문 중앙 기준, m). GK가 없으면 <see cref="float.NaN"/>.</param>
        float BeatProbability(float distToGoal, float cellY, float cellZ, float speed, float gkY);
    }
}
