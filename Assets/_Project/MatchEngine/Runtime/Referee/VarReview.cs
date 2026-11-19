using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// VAR 판정 (specs/012 FR-70~74, 플랜 P6·P7·P9). 4대 사안(득점·PK·직접 퇴장·신원 오인)만, "명백한 오류"일 때만 개입.
    /// 사실 판정(오프사이드·골라인·공격 측 핸드볼)은 항상 정정, 주관 판정은 OFR 확률. 시뮬 틱을 쓰지 않는다 — 체크 시간은 정지 시간에만 들어간다(FR-73).
    /// RNG는 인자로만 (체크 시간 1회 + 주관이면 OFR 1회).
    /// </summary>
    public static class VarReview
    {
        /// <summary>FR-71: 개입 대상 사안인가 (일반 파울 = 아님).</summary>
        public static bool Reviewable(int incident) =>
            incident == EventCodes.VarGoalIncident || incident == EventCodes.VarPenaltyIncident || incident == EventCodes.VarRedIncident || incident == EventCodes.VarIdentityIncident;

        /// <summary>P6 명백한 오류 (주관): |인지 − 진짜| &gt; 임계 그리고 판정 단계가 다름.</summary>
        public static bool ClearError(RefereeParams p, float perceived, float trueScore, int givenDecision, int trueDecision) =>
            givenDecision != trueDecision && DMath.Abs(perceived - trueScore) > p.VarClearError;

        /// <summary>P9 체크 시간 (틱, 10 Hz). 근소 오프사이드 · 사실 · 주관 구간 균등. RNG 1회.</summary>
        public static long CheckTicks(RefereeParams p, bool factual, bool tight, SeedRng rng, int ticksPerSecond = 10)
        {
            float lo, hi;
            if (factual && tight) { lo = p.VarTightMin; hi = p.VarTightMax; }
            else if (factual) { lo = p.VarFactMin; hi = p.VarFactMax; }
            else { lo = p.VarOfrMin; hi = p.VarOfrMax; }
            var s = rng.Range(lo, hi);
            return (long)(s * ticksPerSecond + 0.5f);
        }

        /// <summary>P7 OFR 뒤 번복 (주관). RNG 1회.</summary>
        public static bool OfrOverturn(RefereeParams p, SeedRng rng) => rng.Bernoulli(p.VarOfrOverturn);

        /// <summary>
        /// FR-72 개입 판정. 사실 판정이면 오류 = 번복(RNG 없음), 주관이면 명백한 오류일 때 OFR 확률. rng 소비: 주관 + 명백한 오류일 때 1회.
        /// </summary>
        public static bool Overturns(RefereeParams p, bool factual, bool error, SeedRng rng)
        {
            if (!error) return false;
            return factual || OfrOverturn(p, rng);
        }
    }
}
