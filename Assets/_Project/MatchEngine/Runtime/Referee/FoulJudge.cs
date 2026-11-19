using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>판정 임계 3개 (파울 · 경고 · 퇴장). 진짜 판정은 기본 임계, 내린 판정은 성향·반복 반칙 보정 임계.</summary>
    public readonly struct FoulThresholds
    {
        public readonly float Foul, Yellow, Red;
        public FoulThresholds(float foul, float yellow, float red) { Foul = foul; Yellow = yellow; Red = red; }
    }

    /// <summary>
    /// 파울 진짜 판정 (specs/012 FR-21·22·26, IFAB Law 12). 상태 없음·RNG 없음. 플랜 P1 계수:
    /// 심각도 = wI·강도 + wB·뒤에서 + wS·발바닥 + wH·min(1, 높이/기준) + wL·늦음, 파울 점수 = 심각도 × (1 − k·공 먼저) × (슬라이딩 장인 ? 1.4 : 1).
    /// 경중 3단계: 파울만(부주의) · 경고(무모) · 퇴장(과도한 힘). 파울 여부는 012만 정한다 (008·070은 특징값만).
    /// </summary>
    public static class FoulJudge
    {
        static float C01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;

        public static float Severity(RefereeParams p, in ContactDetail c)
        {
            var h = p.FoulHeightFull > 1e-3f ? C01(c.Height / p.FoulHeightFull) : 0f;
            return p.FoulWIntensity * C01(c.Intensity) + p.FoulWBehind * C01(c.FromBehind) + p.FoulWStuds * C01(c.StudsUp)
                + p.FoulWHeight * h + p.FoulWLate * C01(c.Lateness);
        }

        /// <summary>진짜 파울 점수 (0 이상, 슬라이딩 장인 배수로 1을 넘을 수 있다).</summary>
        public static float Score(RefereeParams p, in ContactDetail c)
        {
            var s = Severity(p, in c) * (1f - p.FoulBallFirstK * C01(c.BallFirst));
            if ((c.HabitBits & ContactDetail.HabitSlideMaster) != 0 && c.Sliding) s *= p.SlideMasterMul;
            return s < 0f ? 0f : s;
        }

        /// <summary>기본 임계 (진짜 판정 — 규칙상 정답).</summary>
        public static FoulThresholds Base(RefereeParams p) => new FoulThresholds(p.FoulThreshold, p.YellowThreshold, p.RedThreshold);

        /// <summary>
        /// 심판 임계 (FR-30 성향 + FR-26 반복 반칙). priorFouls = 이 선수가 이번 경기에 이미 범한 파울 수(이번 것 제외).
        /// </summary>
        public static FoulThresholds ForReferee(RefereeParams p, RefereeProfile r, int priorFouls)
        {
            var foul = p.FoulThreshold + (r.FoulThreshold - 0.5f) * p.FoulShift;
            var strict = (r.Strictness - 0.5f) * p.StrictShift;
            var yellow = p.YellowThreshold - strict;
            if (priorFouls + 1 >= p.RepeatFoulCountInt) yellow -= p.RepeatFoulDrop;
            var red = p.RedThreshold - strict;
            if (yellow < foul) yellow = foul;
            if (red < yellow) red = yellow;
            return new FoulThresholds(foul, yellow, red);
        }

        /// <summary>점수 → 판정 단계 (<see cref="EventCodes"/>.Decision*). 임계 초과(&gt;)만 해당 단계.</summary>
        public static int Decide(float score, in FoulThresholds t)
        {
            if (!(score > t.Foul)) return EventCodes.DecisionNone;
            if (score > t.Red) return EventCodes.DecisionRed;
            if (score > t.Yellow) return EventCodes.DecisionYellow;
            return EventCodes.DecisionFoul;
        }

        /// <summary>진짜 판정 단계 (기본 임계).</summary>
        public static int TrueDecision(RefereeParams p, in ContactDetail c) => Decide(Score(p, in c), Base(p));
    }
}
