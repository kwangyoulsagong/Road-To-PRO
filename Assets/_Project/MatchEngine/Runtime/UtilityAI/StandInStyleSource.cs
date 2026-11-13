using System;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 003 전 결합 배수 대체 구현 (specs/010 FR-11·80~82). 선수별 <see cref="PlayStyle"/>과 팀 지시 가중치·준수율로 배수 표를 미리 계산한다(틱 경로 할당 0).
    /// 성향 → 배수 = base + v/100 [대체] (003 FR-12 소유). 성향 축 ↔ 고려 항목 연결은 기획서 4.2.1 성향 표 예시(FR-80):
    /// 침투 → RunInBehind·BoxCrash(낮으면 DropToReceive), 오버래핑 → Overlap·Underlap, 방향 → Invert(낮으면 StayWide), 압박 적극성 → Press·CoverShadow.
    /// </summary>
    public sealed class StandInStyleSource : IStyleMultiplierSource
    {
        readonly int _slots;
        readonly float _tendencyBase;
        readonly PlayStyle[] _style;
        readonly float[][] _instr = { new float[Considerations.Count], new float[Considerations.Count] };
        readonly float[] _compliance;
        readonly float[] _weather = new float[Considerations.Count];
        readonly float[][] _table;
        readonly float[][] _tableFull;
        readonly float[][] _pinstr;

        public StandInStyleSource(int slots, float tendencyBase)
        {
            _slots = slots;
            _tendencyBase = tendencyBase;
            _style = new PlayStyle[slots];
            _compliance = new float[slots];
            _table = new float[slots][];
            _tableFull = new float[slots][];
            _pinstr = new float[slots][];
            for (int s = 0; s < slots; s++)
            {
                _style[s] = PlayStyle.Neutral();
                _compliance[s] = 1f;
                _table[s] = new float[Considerations.Count];
                _tableFull[s] = new float[Considerations.Count];
                _pinstr[s] = new float[Considerations.Count];
                for (int k = 0; k < Considerations.Count; k++) _pinstr[s][k] = 1f;
            }
            for (int k = 0; k < Considerations.Count; k++) { _instr[0][k] = 1f; _instr[1][k] = 1f; _weather[k] = 1f; }
            for (int s = 0; s < slots; s++) Rebuild(s);
        }

        public void SetStyle(int slot, PlayStyle style)
        {
            _style[slot] = style ?? PlayStyle.Neutral();
            Rebuild(slot);
        }

        public PlayStyle Style(int slot) => _style[slot];

        /// <summary>팀 지시 가중치 (003 FR-41 전 대체, 기본 1).</summary>
        public void SetInstructionWeight(int team, Consideration k, float weight)
        {
            _instr[team][(int)k] = weight;
            for (int s = 0; s < _slots; s++) if (TeamOf(s) == team) Rebuild(s);
        }

        /// <summary>개인 지시 가중치 (072 FR-11 "크로스 줄여라" 등, 팀 지시와 곱한다, 기본 1).</summary>
        public void SetPlayerInstructionWeight(int slot, Consideration k, float weight)
        {
            _pinstr[slot][(int)k] = weight;
            Rebuild(slot);
        }

        /// <summary>지시 준수율 (003 FR-40 전 대체, 기본 1).</summary>
        public void SetCompliance(int slot, float c)
        {
            _compliance[slot] = c;
            Rebuild(slot);
        }

        /// <summary>날씨 스타일 보정 (FR-105, 064 전 1.0).</summary>
        public void SetWeather(Consideration k, float m)
        {
            _weather[(int)k] = m;
            for (int s = 0; s < _slots; s++) Rebuild(s);
        }

        int TeamOf(int slot) => _slots == Match.Squad.Slots ? Match.Squad.TeamOfSlot(slot) : 0;

        void Rebuild(int s)
        {
            for (int k = 0; k < Considerations.Count; k++)
            {
                Breakdown(s, (Consideration)k, out var b);
                _table[s][k] = b.Combined;
                _tableFull[s][k] = StyleMath.Combine(b.Role, b.Tendency, b.Habit, StyleMath.EffectiveInstruction(InstructionWeight(s, (Consideration)k), 1f), b.Weather);
            }
        }

        public float Multiplier(int slot, Consideration k) => _table[slot][(int)k];

        public float MultiplierFullCompliance(int slot, Consideration k) => _tableFull[slot][(int)k];

        public float InstructionWeight(int slot, Consideration k) => _instr[TeamOf(slot)][(int)k] * _pinstr[slot][(int)k];

        public void Breakdown(int slot, Consideration k, out MultiplierBreakdown b)
        {
            var st = _style[slot];
            b.Role = st.RoleWeights[(int)k];
            b.Tendency = Tendency(st, k);
            float h = 1f;
            for (int i = 0; i < HabitTable.Count; i++) h *= HabitTable.Multiplier((Habit)i, st.HabitLevels[i], k);
            b.Habit = h;
            b.Instruction = StyleMath.EffectiveInstruction(InstructionWeight(slot, k), _compliance[slot]);
            b.Weather = _weather[(int)k];
            b.Combined = StyleMath.Combine(b.Role, b.Tendency, b.Habit, b.Instruction, b.Weather);
        }

        float T(float v) => _tendencyBase + v / 100f;

        float Tendency(PlayStyle st, Consideration k)
        {
            switch (k)
            {
                case Consideration.RunInBehind:
                case Consideration.BoxCrash: return T(st.Runs);
                case Consideration.DropToReceive: return T(100f - st.Runs);
                case Consideration.Overlap:
                case Consideration.Underlap: return T(st.Overlapping);
                case Consideration.Invert: return T(st.Direction);
                case Consideration.StayWide: return T(100f - st.Direction);
                case Consideration.Press:
                case Consideration.CoverShadow: return T(st.PressAggression);
                default: return 1f;
            }
        }

        public float Freedom(int slot) => _style[slot].Freedom;

        public int HabitLevel(int slot, Habit h) => _style[slot].HabitLevels[(int)h];

        public int ConflictingHabitLevel(int slot, Consideration intentKind)
        {
            var st = _style[slot];
            int max = 0;
            for (int i = 0; i < HabitTable.Count; i++)
            {
                var lv = st.HabitLevels[i];
                if (lv > max && HabitTable.Conflicts((Habit)i, intentKind)) max = lv;
            }
            return max;
        }

        public float TendencyConflict(int slot, Consideration intentKind)
        {
            var t = Tendency(_style[slot], intentKind); // 1.0 = 중립(50), 낮을수록 의도와 반대 성향
            var c = (1f - t) * 2f;
            return c < 0f ? 0f : (c > 1f ? 1f : c);
        }
    }
}
