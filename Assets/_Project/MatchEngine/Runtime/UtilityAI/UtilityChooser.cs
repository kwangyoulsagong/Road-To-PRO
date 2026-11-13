using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 공통 선택기 (specs/010 FR-20·42·45·92·94 — 010 오프볼·011 GK·072 온볼 공용).
    /// ① 포함 후보만 최댓값 정규화 U_i / max(|U_max|, ε) ② (+ 평가 노이즈) ③ + w_intent·m (결합 배수와 곱하지 않는 덧셈)
    /// ④ 기회주의 필터: 의도 밖 후보는 의도 일치 최고 점수 + θ_opp 이상일 때만 남김 ⑤ softmax(점수/τ). 동점 = 생성 순서.
    /// 할당 0 (고정 크기 버퍼). RNG는 <see cref="Pick"/>에서만, τ ≤ <see cref="SeedRng.MinTemperature"/>면 소비하지 않는다.
    /// </summary>
    public sealed class UtilityChooser
    {
        public const int MaxCandidates = 24;

        readonly float[] _score = new float[MaxCandidates];
        readonly bool[] _in = new bool[MaxCandidates];
        readonly float[] _soft = new float[MaxCandidates];
        readonly int[] _map = new int[MaxCandidates];
        int _count;

        public int Count => _count;
        public float Score(int i) => _score[i];
        public bool IsIn(int i) => _in[i];

        /// <summary>τ = τ_max − (τ_max − τ_min)·s + 가산 (FR-20·23). s = 판단 입력 능력치 정규화값.</summary>
        public static float Temperature(float tauMax, float tauMin, float s, float add)
        {
            if (s < 0f) s = 0f; else if (s > 1f) s = 1f;
            return tauMax - (tauMax - tauMin) * s + add;
        }

        /// <summary>결정 유지 (FR-42·45): 새 최고 점수가 현재보다 θ·|현재| 이상 높을 때만 재추첨.</summary>
        public static bool ShouldReroll(float current, float best, float threshold, float eps) =>
            best - current >= threshold * DMath.Max(DMath.Abs(current), eps);

        /// <summary>
        /// 점수 계산. match·include·noise는 비어 있으면 각각 0·전부 포함·0. 포함 후보가 하나도 없으면 전부 제외 상태.
        /// </summary>
        public void Evaluate(ReadOnlySpan<float> utility, ReadOnlySpan<float> match, ReadOnlySpan<bool> include, ReadOnlySpan<float> noise, float wIntent, float eps)
        {
            var n = utility.Length;
            if (n > MaxCandidates) throw new ArgumentException("후보 수 초과");
            _count = n;
            float umax = float.MinValue;
            var any = false;
            for (int i = 0; i < n; i++)
            {
                _in[i] = include.IsEmpty || include[i];
                if (!_in[i]) continue;
                any = true;
                if (utility[i] > umax) umax = utility[i];
            }
            var norm = any ? DMath.Max(DMath.Abs(umax), eps) : 1f;
            for (int i = 0; i < n; i++)
            {
                if (!_in[i]) { _score[i] = 0f; continue; }
                var s = utility[i] / norm;
                if (!noise.IsEmpty) s += noise[i];
                if (!match.IsEmpty && wIntent > 0f && match[i] > 0f) s += wIntent * match[i];
                _score[i] = s;
            }
        }

        /// <summary>기회주의 필터 (FR-94). 의도 일치 후보가 하나라도 있으면 true.</summary>
        public bool FilterOpportunistic(ReadOnlySpan<float> match, float thetaOpp)
        {
            if (match.IsEmpty) return false;
            float best = float.MinValue;
            var any = false;
            for (int i = 0; i < _count; i++)
                if (_in[i] && match[i] > 0f) { any = true; if (_score[i] > best) best = _score[i]; }
            if (!any) return false;
            for (int i = 0; i < _count; i++)
                if (_in[i] && match[i] <= 0f && _score[i] < best + thetaOpp) _in[i] = false;
            return true;
        }

        /// <summary>포함 후보 중 최고 점수 (동점 = 낮은 인덱스). 없으면 −1.</summary>
        public int Best()
        {
            int b = -1;
            for (int i = 0; i < _count; i++)
                if (_in[i] && (b < 0 || _score[i] > _score[b])) b = i;
            return b;
        }

        /// <summary>포함 후보에서 softmax 선택. 포함 후보가 없으면 0.</summary>
        public int Pick(float tau, SeedRng rng)
        {
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (_in[i]) { _soft[n] = _score[i]; _map[n] = i; n++; }
            if (n == 0) return 0;
            var pick = rng.PickSoftmax(new ReadOnlySpan<float>(_soft, 0, n), tau);
            return _map[pick];
        }
    }
}
