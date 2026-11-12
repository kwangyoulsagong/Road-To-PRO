namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>결합 배수 분해 (FR-11·60 텔레메트리).</summary>
    public struct MultiplierBreakdown
    {
        public float Role, Tendency, Habit, Instruction, Weather;
        public float Combined;
    }

    /// <summary>
    /// 결합 배수 캐시 질의 (specs/010 FR-11·80, 003 FR-30~32·41 소유). 010은 곱하는 순서만 고정하고 배수를 다시 정의하지 않는다.
    /// 003 구현 전에는 <see cref="StandInStyleSource"/>.
    /// </summary>
    public interface IStyleMultiplierSource
    {
        /// <summary>선수(슬롯) s의 고려 항목 k 결합 배수 (역할 × 성향 × 버릇 × 실효 지시 × 날씨, 클램프 포함). 0이면 그 후보는 제외.</summary>
        float Multiplier(int slot, Consideration k);
        void Breakdown(int slot, Consideration k, out MultiplierBreakdown b);
        /// <summary>역할 자유도 (FR-85 이탈 상한).</summary>
        float Freedom(int slot);
        /// <summary>의도 종류 k와 충돌하는 버릇의 최고 레벨 0/1/2 (FR-93).</summary>
        int ConflictingHabitLevel(int slot, Consideration intentKind);
        /// <summary>의도 종류 k와 성향의 충돌 정도 0~1 (FR-93).</summary>
        float TendencyConflict(int slot, Consideration intentKind);
        /// <summary>버릇 h의 레벨 (FR-81 출발 시점 보정 등).</summary>
        int HabitLevel(int slot, Habit h);
    }

    /// <summary>결합 배수 계산식 (003 FR-30·31 규칙의 대체, 곱셈 순서 고정).</summary>
    public static class StyleMath
    {
        /// <summary>결합 배수 상한 [대체] (003 FR-30 클램프 전).</summary>
        public const float MaxCombined = 5f;

        /// <summary>실효 지시 가중치 = 1 + 준수율·(w − 1). 금지 지시(w = 0)에 준수율 1이면 정확히 0 (003 FR-31).</summary>
        public static float EffectiveInstruction(float weight, float compliance)
        {
            if (weight <= 0f && compliance >= 1f) return 0f;
            var c = compliance < 0f ? 0f : (compliance > 1f ? 1f : compliance);
            var e = 1f + c * (weight - 1f);
            return e < 0f ? 0f : e;
        }

        /// <summary>역할 × 성향 × 버릇 × 지시 × 날씨, [0, MaxCombined].</summary>
        public static float Combine(float role, float tendency, float habit, float instruction, float weather = 1f)
        {
            var m = role * tendency * habit * instruction * weather;
            return m < 0f ? 0f : (m > MaxCombined ? MaxCombined : m);
        }
    }
}
