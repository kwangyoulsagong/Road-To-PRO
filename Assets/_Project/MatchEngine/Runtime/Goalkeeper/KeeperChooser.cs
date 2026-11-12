using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>GK 행동 후보 종류 (specs/011 FR-16·94). 돌진·클레임(073)·배급(074)은 각 스펙이 추가한다.</summary>
    public enum KeeperActionKind : byte
    {
        /// <summary>위치 선정 목표로 (FR-01~07).</summary>
        Position = 0,
        /// <summary>박스 안 느슨한 공 경합 (FR-16).</summary>
        Contest = 1,
        /// <summary>지점 이동 (시나리오 의도 지점 등).</summary>
        MoveTo = 2,
    }

    /// <summary>후보 하나: 종류·목표 지점·기대효용.</summary>
    public struct KeeperCandidate
    {
        public KeeperActionKind Kind;
        public Vec2 Point;
        public float Utility;
    }

    /// <summary>시나리오 의도 (010 FR-91 의도 패킷의 GK 부분). 종류·목표 지점·타이밍 창 안 여부.</summary>
    public struct KeeperIntent
    {
        public bool Has;
        public KeeperActionKind Kind;
        public Vec2 Point;
        public bool InWindow;
    }

    /// <summary>
    /// GK 행동 선택 (specs/011 FR-94, 010 FR-20·92·94의 대체 구현 — 010 구현 때 010 공용 선택기로 옮긴다).
    /// ① 기대효용이 음수인 후보는 뺀다(위치 선정은 항상 남김, 073 FR-02 "EU &gt; 0이면 실행") ② 최댓값 정규화 U_i / max(|U_max|, ε)
    /// ③ 의도 일치 후보에 + w_intent·m (m = 종류 일치 × max(0, 1 − d/r_intent) × 창 안 1 / 밖 0.5) ④ 의도가 있으면 의도 밖 후보는
    /// 의도 최고 점수 + θ_opp보다 높을 때만 남김 ⑤ softmax(점수/τ), τ = τ_max − (τ_max − τ_min)·n(Decisions).
    /// 할당 없음. RNG는 softmax 1회.
    /// </summary>
    public sealed class KeeperChooser
    {
        public const int MaxCandidates = 8;
        const float Eps = 1e-6f;

        readonly GoalkeeperParams _p;
        readonly float[] _score = new float[MaxCandidates];
        readonly bool[] _in = new bool[MaxCandidates];
        readonly float[] _soft = new float[MaxCandidates];
        readonly int[] _map = new int[MaxCandidates];

        public KeeperChooser(GoalkeeperParams p) => _p = p;

        /// <summary>의도 일치도 m (010 FR-92).</summary>
        public float Match(in KeeperCandidate c, in KeeperIntent intent)
        {
            if (!intent.Has || c.Kind != intent.Kind) return 0f;
            var d = (c.Point - intent.Point).Length();
            var near = _p.ChoiceRIntent > Eps ? 1f - d / _p.ChoiceRIntent : 0f;
            if (near <= 0f) return 0f;
            return near * (intent.InWindow ? 1f : 0.5f);
        }

        /// <summary>후보 중 하나를 고른다 (인덱스). 후보 0은 위치 선정이어야 한다.</summary>
        public int Choose(KeeperCandidate[] cands, int count, in KeeperIntent intent, float decisionsN, SeedRng rng)
        {
            if (count <= 1) return 0;
            if (count > MaxCandidates) count = MaxCandidates;
            float umax = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                _in[i] = i == 0 || cands[i].Utility >= 0f; // ① 음수 기대효용 제외
                if (_in[i] && cands[i].Utility > umax) umax = cands[i].Utility;
            }
            var norm = DMath.Max(DMath.Abs(umax), Eps);
            float bestIntent = float.MinValue;
            var anyIntent = false;
            for (int i = 0; i < count; i++)
            {
                if (!_in[i]) continue;
                var m = _p.ChoiceWIntent > 0f ? Match(in cands[i], in intent) : 0f; // 보너스 0 = 의도 없음과 같음 (AC-27)
                _score[i] = cands[i].Utility / norm + _p.ChoiceWIntent * m; // ② ③
                if (m > 0f) { anyIntent = true; if (_score[i] > bestIntent) bestIntent = _score[i]; }
            }
            if (anyIntent) // ④ 기회주의 이탈 제한
                for (int i = 0; i < count; i++)
                    if (_in[i] && Match(in cands[i], in intent) <= 0f && _score[i] < bestIntent + _p.ChoiceThetaOpp) _in[i] = false;
            int n = 0;
            for (int i = 0; i < count; i++)
                if (_in[i]) { _soft[n] = _score[i]; _map[n] = i; n++; }
            if (n == 0) return 0;
            var tau = KeeperMapping.Temperature(_p, decisionsN);
            var pick = rng.PickSoftmax(new System.ReadOnlySpan<float>(_soft, 0, n), tau);
            return _map[pick];
        }
    }
}
