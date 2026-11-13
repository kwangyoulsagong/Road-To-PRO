using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 온볼 성공 확률·기대 가치 공급 (specs/072 FR-10·15·20). 패스는 008 평가를 직접 쓰고, 여기는 070(돌파)·071(슈팅 xG·크로스)·012(파울 유도) 자리다.
    /// 구현 전에는 <see cref="StandInOnBallEval"/>. RNG 없음, 할당 없음.
    /// </summary>
    public interface IOnBallEvaluator
    {
        /// <summary>선수 s가 수비수 d를 제치는 확률 (070 자리).</summary>
        float DribbleSuccess(DecisionWorld w, int s, int defender, float pressure);
        /// <summary>지점 from에서 슈팅 xG (071 FR-10 자리).</summary>
        float ShotXg(DecisionWorld w, int s, Vec2 from, float pressure);
        /// <summary>크로스 성공 = 뜬공 xPass에 크로스 능력 반영 (071 자리).</summary>
        float CrossSuccess(DecisionWorld w, int s, float loftedXPass);
        /// <summary>넘어졌을 때 파울 유도 기대 가치 (박스 안 = PK xG, 071 FR-11 자리).</summary>
        float FoulValue(DecisionWorld w, int s);
        /// <summary>패스 성공 확률에 곱할 오프사이드 위험 배수 (FR-15): 수신자가 오프사이드 위치면 1 − 회피, 아니면 1.</summary>
        float OffsideFactor(DecisionWorld w, int passer, int receiver);
        /// <summary>공격 팀 기준 지점이 상대 페널티 박스 안인가.</summary>
        bool InOppBox(DecisionWorld w, int team, Vec2 p);
    }
}
