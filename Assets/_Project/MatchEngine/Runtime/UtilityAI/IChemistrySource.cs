namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 동료 케미 스냅샷 (specs/072 FR-12, 033 소유). 경기 시작 시 033이 넘긴 값 — 072는 패스 대상 점수에만 배수 1 + k·chem로 곱한다.
    /// 033 구현 전에는 <see cref="StandInChemistry"/>(기본 0).
    /// </summary>
    public interface IChemistrySource
    {
        /// <summary>슬롯 a → b 케미 (0~1).</summary>
        float Chemistry(int a, int b);
    }

    /// <summary>033 전 대체: 슬롯 쌍 대칭 표, 기본 0. 경기 중 바뀌지 않는다(스냅샷).</summary>
    public sealed class StandInChemistry : IChemistrySource
    {
        readonly float[] _v = new float[DecisionWorld.N * DecisionWorld.N];

        public float Chemistry(int a, int b) => (uint)a < DecisionWorld.N && (uint)b < DecisionWorld.N ? _v[a * DecisionWorld.N + b] : 0f;

        /// <summary>슬롯 s의 행·열을 0으로 (교체로 선수가 바뀜).</summary>
        public void ClearSlot(int s)
        {
            for (int o = 0; o < DecisionWorld.N; o++) { _v[s * DecisionWorld.N + o] = 0f; _v[o * DecisionWorld.N + s] = 0f; }
        }

        public void Set(int a, int b, float chem)
        {
            if (!(chem >= 0f && chem <= 1f)) throw new System.ArgumentOutOfRangeException(nameof(chem), chem, "케미는 0~1");
            _v[a * DecisionWorld.N + b] = chem;
            _v[b * DecisionWorld.N + a] = chem;
        }
    }
}
