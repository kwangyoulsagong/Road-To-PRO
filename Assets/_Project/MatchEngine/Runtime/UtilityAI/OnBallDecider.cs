using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using BallParams = RoadToPro.MatchEngine.Ball.BallParams;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>온볼 결정 결과 (specs/072).</summary>
    public struct OnBallResult
    {
        public OnBallItem Item;
        public int Target;
        public Vec2 Point;
        public PassOption Pass;
        public int Defender;
        /// <summary>선택 후보의 성공 확률 (드리블 1:1 대체 판정에 쓴다).</summary>
        public float P;
        /// <summary>슈팅: 대체 xG (071 대체 슈팅 결과에 쓴다).</summary>
        public float Xg;
        /// <summary>넘어지기: 파울 유도 기대 가치·접촉 강도 구간 (0 없음, 1 약함).</summary>
        public float FoulValue;
        public int ContactBin;
        /// <summary>지시 무시 (FR-31·32) — 같은 틱에 이벤트 1건.</summary>
        public bool IgnoredInstruction;
        public Consideration IgnoredKind;
        public float IgnoredWeight;
        /// <summary>기회주의 이탈 — 같은 틱에 013 의도 해제 통보 (010 FR-94, AC-07).</summary>
        public bool ReleaseIntent;
        public DecisionSource Source;
        public bool Fallback;
    }

    /// <summary>
    /// 공 소유 선수 1명 온볼 결정 (specs/072, 010 공통 틀) — 단계와 테스트 픽스처가 같은 경로를 쓴다.
    /// 순서(FR-25): ① 집중 저하 굴림 ② (의도 단계가 바뀌면) 준수 굴림 ③ 시야·후보(FR-01~04) ④ 점수(FR-10~13) ⑤ 노이즈(후보 2개당 균등 난수 2개)
    /// ⑥ 정규화·의도 보너스(FR-14)·기회주의 필터 ⑦ softmax ⑧ 지시 무시 판정(FR-32, RNG 없음). 온볼은 행동 1회라 결정 유지(010 FR-42)는 쓰지 않는다. 할당 0.
    /// </summary>
    public sealed class OnBallDecider
    {
        readonly DecisionParams _p;
        readonly IStyleMultiplierSource _style;
        readonly OnBallCandidates _cands;
        readonly OnBallScorer _scorer;
        readonly UtilityChooser _chooser = new UtilityChooser();
        readonly UtilityChooser _full = new UtilityChooser();
        readonly float[] _u = new float[UtilityChooser.MaxCandidates];
        readonly float[] _uf = new float[UtilityChooser.MaxCandidates];
        readonly float[] _m = new float[UtilityChooser.MaxCandidates];
        readonly float[] _noise = new float[UtilityChooser.MaxCandidates];
        readonly bool[] _in = new bool[UtilityChooser.MaxCandidates];
        readonly bool[] _inf = new bool[UtilityChooser.MaxCandidates];
        readonly IOnBallEvaluator _eval;

        public OnBallDecider(DecisionParams p, IStyleMultiplierSource style, IOnBallEvaluator eval, IChemistrySource chem)
        {
            _p = p;
            _style = style;
            _eval = eval;
            _cands = new OnBallCandidates(p, eval, chem);
            _scorer = new OnBallScorer(p, style);
        }

        public OnBallCandidates Candidates => _cands;
        public UtilityChooser Chooser => _chooser;
        public OnBallScorer Scorer => _scorer;
        public IOnBallEvaluator Evaluator => _eval;
        public float LastTemperature { get; private set; }
        public int LastPick { get; private set; }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        float LeaderRelief(DecisionWorld w, int s)
        {
            var t = DecisionWorld.TeamOf(s);
            var r2 = _p.LeaderRadius * _p.LeaderRadius;
            float best = 0f;
            for (int o = t * DecisionWorld.PerTeam; o < (t + 1) * DecisionWorld.PerTeam; o++)
            {
                if (o == s || !w.Active[o] || (w.Pos[o] - w.Pos[s]).LengthSq() > r2) continue;
                var l = DecisionAttributes.N(w.Mind[o].Leadership);
                if (l > best) best = l;
            }
            return EvalNoise.LeaderRelief(_p, best);
        }

        static float NearestOpponentDistance(DecisionWorld w, int s)
        {
            var opp = 1 - DecisionWorld.TeamOf(s);
            float best = float.MaxValue;
            for (int o = opp * DecisionWorld.PerTeam; o < (opp + 1) * DecisionWorld.PerTeam; o++)
            {
                if (!w.Active[o]) continue;
                var d = (w.Pos[o] - w.Pos[s]).LengthSq();
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 1e3f : DMath.Sqrt(best);
        }

        /// <summary>의도 준수 확률 (010 FR-93 [대체] — <see cref="OffBallDecider.ComplyProbability"/>와 같은 식).</summary>
        public float ComplyProbability(DecisionWorld w, int s, Consideration kind)
        {
            var p = _p.ComplyBase + _p.ComplyTeamwork * DecisionAttributes.N(w.Mind[s].Teamwork)
                    - _p.ComplyHabit * _style.ConflictingHabitLevel(s, kind) - _p.ComplyTendency * _style.TendencyConflict(s, kind);
            return p < _p.ComplyMin ? _p.ComplyMin : (p > _p.ComplyMax ? _p.ComplyMax : p);
        }

        /// <summary>
        /// 결정. aw는 같은 틱 008 평가 월드, pressure는 008 볼 소유자 압박(0~1), oneTouch = 008 원터치 창 안, heldSeconds = 보유 시간.
        /// </summary>
        public OnBallResult Decide(DecisionWorld w, ActionWorld aw, ActionParams ap, BallParams bp, int s, ref PlayerDecisionState st, SeedRng rng,
            float pressure, bool oneTouch, float heldSeconds)
        {
            var tick = w.Tick;
            var mind = w.Mind[s];

            // ① 집중 저하 (010 FR-26)
            if (!st.InLapse(tick))
            {
                var h = EvalNoise.LapseHazard(_p, DecisionAttributes.N(w.Kin[s].Concentration), w.C[s], LeaderRelief(w, s));
                if (rng.NextFloat() < h) st.LapseUntil = tick + (long)(_p.LapseDuration / w.Dt + 0.5f);
            }
            var lapse = st.InLapse(tick);

            // ② 의도 준수 (010 FR-93): 단계당 1회
            ref readonly var intent = ref w.Intent[s];
            if (intent.Has)
            {
                if (intent.StepKey != st.IntentKey)
                {
                    st.IntentKey = intent.StepKey;
                    st.Complied = rng.NextFloat() < ComplyProbability(w, s, intent.Kind);
                }
            }
            else st.IntentKey = -1;
            var intentActive = intent.Has && st.Complied;

            // ③ 후보 ④ 점수
            _cands.Generate(w, aw, ap, bp, s, pressure, oneTouch, heldSeconds, in intent, intentActive);
            var n = _cands.Count;
            var anyIn = false;
            for (int i = 0; i < n; i++)
            {
                _scorer.Score(w, s, oneTouch, ref _cands[i]);
                if (_cands[i].Mult > 0f) anyIn = true;
            }
            if (!anyIn) // 금지 지시로 전부 빠짐 → 강제 폴백 (FR-04)
            {
                _cands.ForceFallback(w, aw, ap, s, pressure, heldSeconds);
                n = _cands.Count;
                for (int i = 0; i < n; i++)
                {
                    ref var c = ref _cands[i];
                    _scorer.Score(w, s, oneTouch, ref c);
                    if (c.Mult <= 0f) { c.Mult = 1f; c.MultFull = 1f; c.Gain = c.GainBase; c.Utility = c.Gain - c.Loss; }
                }
            }

            // ⑤ 노이즈 (010 FR-21·26)
            var sigma = EvalNoise.UnderPressure(EvalNoise.Sigma(_p, DecisionAttributes.N(mind.Decisions), DecisionAttributes.N(mind.Vision)),
                EvalNoise.Pressure(_p, NearestOpponentDistance(w, s)), DecisionAttributes.N(mind.Composure));
            if (lapse) sigma *= _p.LapseNoiseMul;
            for (int i = 0; i < n; i += 2)
            {
                var u1 = 1f - rng.NextFloat();
                var u2 = rng.NextFloat();
                var rad = DMath.Sqrt(-2f * DMath.Log(u1)) * sigma;
                var ang = DMath.TwoPi * u2;
                _noise[i] = rad * DMath.Cos(ang);
                if (i + 1 < n) _noise[i + 1] = rad * DMath.Sin(ang);
            }
            var anyMatch = false;
            for (int i = 0; i < n; i++)
            {
                ref var c = ref _cands[i];
                c.Noise = _noise[i];
                c.Match = intentActive ? MatchOf(in c, in intent) : 0f;
                if (c.Match > 0f) anyMatch = true;
                _u[i] = c.Utility;
                _uf[i] = OnBallScorer.FullComplianceUtility(in c);
                _m[i] = c.Match;
                _in[i] = c.Mult > 0f;
                _inf[i] = c.MultFull > 0f;
            }

            // ⑥ 정규화 + 노이즈 + 의도 보너스, 기회주의 필터
            var us = new ReadOnlySpan<float>(_u, 0, n);
            var ms = new ReadOnlySpan<float>(_m, 0, n);
            var ns = new ReadOnlySpan<float>(_noise, 0, n);
            _chooser.Evaluate(us, ms, new ReadOnlySpan<bool>(_in, 0, n), ns, _p.IntentW, _p.NormEps);
            if (intentActive) _chooser.FilterOpportunistic(ms, _p.IntentThetaOpp);

            // ⑦ softmax
            var tau = UtilityChooser.Temperature(_p.TauMax, _p.TauMin, DecisionAttributes.N(mind.Decisions), EvalNoise.FamiliarityTau(_p, DecisionAttributes.N(mind.Familiarity)));
            LastTemperature = tau;
            var pick = _chooser.Pick(tau, rng);
            if (_chooser.Best() < 0) pick = 0;
            LastPick = pick;
            ref var ch = ref _cands[pick];

            var r = new OnBallResult
            {
                Item = ch.Item, Target = ch.Target, Point = ch.Point, Pass = ch.Pass, Defender = ch.Defender, P = ch.P, Fallback = _cands.Fallback,
            };
            if (ch.Item == OnBallItem.Shoot) r.Xg = ch.V;
            if (ch.Item == OnBallItem.Dive)
            {
                r.FoulValue = _eval.FoulValue(w, s);
                r.ContactBin = ch.Target >= 0 && (w.Pos[ch.Target] - w.Pos[s]).LengthSq() <= 1f ? 1 : 0;
            }

            // ⑧ 지시 무시 (FR-32, 003 FR-42): 억제된 행동이 선택됐고, 준수율 1이었다면 다른 후보가 최고점. 준수율 효과가 없으면(배수 동일) 무시 아님.
            if (ch.Suppressed)
            {
                var differs = false;
                for (int i = 0; i < n; i++) if (_cands[i].Mult != _cands[i].MultFull) { differs = true; break; }
                if (differs)
                {
                    _full.Evaluate(new ReadOnlySpan<float>(_uf, 0, n), ms, new ReadOnlySpan<bool>(_inf, 0, n), ns, _p.IntentW, _p.NormEps);
                    if (intentActive) _full.FilterOpportunistic(ms, _p.IntentThetaOpp);
                    var bf = _full.Best();
                    if (bf >= 0 && bf != pick)
                    {
                        r.IgnoredInstruction = true;
                        r.IgnoredKind = ch.SuppressedKind;
                        _style.Breakdown(s, ch.SuppressedKind, out var b);
                        r.IgnoredWeight = b.Instruction;
                    }
                }
            }

            // 출처 (010 FR-99)
            if (intent.Has && !st.Complied) r.Source = DecisionSource.IntentIgnored;
            else if (intentActive && ch.Match > 0f) r.Source = DecisionSource.IntentMatch;
            else if (intentActive && anyMatch) { r.Source = DecisionSource.Opportunistic; r.ReleaseIntent = true; }
            else r.Source = DecisionSource.OnBall;

            st.HasAction = false;
            st.Source = r.Source;
            st.LastDecisionTick = tick;
            return r;
        }

        /// <summary>
        /// 의도 일치도 (FR-14, 010 FR-92): 패스류는 다음 패스 대상과 같은 대상이면, 슈팅·크로스·홀드는 단계 행동과 같은 종류면 1,
        /// 캐리·돌파는 종류가 같고 단계 목표점에 가까울수록 (창 밖 감쇠).
        /// 대상이 시야 집합 밖이면 후보 자체가 없어 0 (FR-02 시야 우선).
        /// </summary>
        public float MatchOf(in OnBallCandidate c, in IntentPacket intent)
        {
            if (!intent.Has) return 0f;
            bool hit;
            if (OnBallItems.IsPass(c.Item))
                hit = intent.NextPassTarget >= 0 && c.Target == intent.NextPassTarget
                      && (c.Item != OnBallItem.Cross || intent.Kind == Consideration.Cross || intent.Kind == Consideration.Pass || intent.Kind == Consideration.AssistPass);
            else
                switch (c.Item)
                {
                    case OnBallItem.Shoot: hit = intent.Kind == Consideration.Shot || intent.Kind == Consideration.LongShot; break;
                    case OnBallItem.Dribble:
                    case OnBallItem.Carry:
                        // 캐리·돌파는 종류 + 단계 목표점 근접도 (010 FR-92와 같은 식)
                        if (intent.Kind != Consideration.Dribble && intent.Kind != Consideration.CutIn) return 0f;
                        var near = _p.IntentR > 1e-6f ? 1f - (c.Point - intent.Point).Length() / _p.IntentR : 0f;
                        if (near <= 0f) return 0f;
                        return near * (intent.InWindow ? 1f : _p.IntentOutWindow);
                    case OnBallItem.Hold: hit = intent.Kind == Consideration.HoldBall; break;
                    default: hit = false; break;
                }
            if (!hit) return 0f;
            return intent.InWindow ? 1f : _p.IntentOutWindow;
        }
    }
}
