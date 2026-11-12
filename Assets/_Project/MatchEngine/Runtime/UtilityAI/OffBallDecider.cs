using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>오프볼 결정 결과.</summary>
    public struct OffBallResult
    {
        public Consideration Kind;
        public Vec2 Point;
        public int Target;
        public Intensity Intensity;
        public float MaxSpeed;
        public DecisionSource Source;
        /// <summary>기회주의 이탈 — 같은 틱에 013에 의도 해제를 통보해야 한다 (FR-94).</summary>
        public bool ReleaseIntent;
        /// <summary>정지 구간에서 서 있음 (FR-43a) — 목표점 = 멈춘 위치.</summary>
        public bool Settled;
        /// <summary>이번 결정에서 softmax로 재추첨했는가 (아니면 유지, FR-45).</summary>
        public bool Rerolled;
    }

    /// <summary>
    /// 선수 1명 오프볼 결정 (specs/010 FR-03~26·42~46·70~72·80~104 공통 경로 — 단계와 테스트 픽스처가 같이 쓴다).
    /// 순서: ① 집중 저하 굴림 ② (의도 단계가 바뀌면) 준수 굴림 ③ 후보 생성 ④ 점수 ⑤ 노이즈 표본(후보 2개당 균등 난수 2개) ⑥ 정규화·의도 보너스·기회주의 필터
    /// ⑦ 유지 판정(최소 유지·15 %) — 재추첨할 때만 ⑧ softmax ⑨ 목표점 이동 제한 ⑩ 이동 강도.
    /// RNG는 모두 주입된 선수 결정 스트림, 위 순서 고정(FR-25). 할당 0.
    /// </summary>
    public sealed class OffBallDecider
    {
        readonly DecisionParams _p;
        readonly IStyleMultiplierSource _style;
        readonly OffBallCandidates _cands;
        readonly OffBallScorer _scorer;
        readonly UtilityChooser _chooser = new UtilityChooser();
        readonly float[] _u = new float[OffBallCandidates.Capacity];
        readonly float[] _m = new float[OffBallCandidates.Capacity];
        readonly float[] _noise = new float[OffBallCandidates.Capacity];
        readonly bool[] _in = new bool[OffBallCandidates.Capacity];

        public OffBallDecider(DecisionParams p, ISpaceEvaluator space, IStyleMultiplierSource style)
        {
            _p = p;
            _style = style;
            _cands = new OffBallCandidates(p, space, style);
            _scorer = new OffBallScorer(p, style);
        }

        /// <summary>마지막 결정의 후보 (텔레메트리·테스트 읽기 전용).</summary>
        public OffBallCandidates Candidates => _cands;
        public UtilityChooser Chooser => _chooser;
        public IStyleMultiplierSource Style => _style;
        public float LastTemperature { get; private set; }
        public int LastPick { get; private set; }
        /// <summary>마지막 결정이 즉시 재결정 트리거였는가 (FR-72 두 단계 허용 확인용).</summary>
        public bool LastTrigger { get; private set; }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>준수 확률 (FR-93 [대체] f).</summary>
        public float ComplyProbability(DecisionWorld w, int s, Consideration kind)
        {
            var p = _p.ComplyBase + _p.ComplyTeamwork * DecisionAttributes.N(w.Mind[s].Teamwork)
                    - _p.ComplyHabit * _style.ConflictingHabitLevel(s, kind) - _p.ComplyTendency * _style.TendencyConflict(s, kind);
            return p < _p.ComplyMin ? _p.ComplyMin : (p > _p.ComplyMax ? _p.ComplyMax : p);
        }

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

        float NearestOpponentDistance(DecisionWorld w, int s)
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

        public OffBallResult Decide(DecisionWorld w, int s, ref PlayerDecisionState st, SeedRng rng, bool trigger)
        {
            var tick = w.Tick;
            LastTrigger = trigger;
            var m = w.Mind[s];
            var kin = w.Kin[s];

            // ① 집중 저하 (FR-26)
            if (!st.InLapse(tick))
            {
                var h = EvalNoise.LapseHazard(_p, DecisionAttributes.N(kin.Concentration), w.C[s], LeaderRelief(w, s));
                if (rng.NextFloat() < h) st.LapseUntil = tick + (long)(_p.LapseDuration / w.Dt + 0.5f);
            }
            var lapse = st.InLapse(tick);
            var delay = w.Profile[s].PerceptionDelay + (lapse ? _p.LapseDelay : 0f);

            // ② 의도 준수 (FR-93): 단계당 1회
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
            _cands.Generate(w, s, delay, in intent, intentActive);
            var n = _cands.Count;
            for (int i = 0; i < n; i++) _scorer.Score(w, s, ref _cands[i]);

            // ⑤ 노이즈 (FR-21·26)
            var sigma = EvalNoise.UnderPressure(EvalNoise.Sigma(_p, DecisionAttributes.N(m.Decisions), DecisionAttributes.N(m.Vision)),
                EvalNoise.Pressure(_p, NearestOpponentDistance(w, s)), DecisionAttributes.N(m.Composure));
            if (lapse) sigma *= _p.LapseNoiseMul;
            for (int i = 0; i < n; i += 2) // Box–Muller 1회로 정규 표본 2개 (cos·sin) — 후보 순서대로, 소비는 균등 난수 2개 / 후보 2개
            {
                var u1 = 1f - rng.NextFloat();
                var u2 = rng.NextFloat();
                var rad = DMath.Sqrt(-2f * DMath.Log(u1)) * sigma;
                var ang = DMath.TwoPi * u2;
                _noise[i] = rad * DMath.Cos(ang);
                if (i + 1 < n) _noise[i + 1] = rad * DMath.Sin(ang);
            }
            for (int i = 0; i < n; i++)
            {
                ref var c = ref _cands[i];
                c.Noise = _noise[i];
                c.Match = intentActive ? MatchOf(in c, in intent) : 0f;
                _u[i] = c.Utility;
                _m[i] = c.Match;
                _in[i] = c.Mult > 0f;
            }

            // ⑥ 정규화 + 노이즈 + 의도 보너스, 기회주의 필터
            _chooser.Evaluate(new ReadOnlySpan<float>(_u, 0, n), new ReadOnlySpan<float>(_m, 0, n), new ReadOnlySpan<bool>(_in, 0, n),
                new ReadOnlySpan<float>(_noise, 0, n), _p.IntentW, _p.NormEps);
            if (intentActive) _chooser.FilterOpportunistic(new ReadOnlySpan<float>(_m, 0, n), _p.IntentThetaOpp);

            // ⑦ 유지 판정
            int cur = -1;
            if (st.HasAction)
                for (int i = 0; i < n; i++)
                    if (_cands[i].Kind == st.Kind && _chooser.IsIn(i)) { cur = i; break; }
            var tau = UtilityChooser.Temperature(_p.TauMax, _p.TauMin, DecisionAttributes.N(m.Decisions), EvalNoise.FamiliarityTau(_p, DecisionAttributes.N(m.Familiarity)));
            LastTemperature = tau;
            int pick;
            var rerolled = false;
            var best = _chooser.Best();
            if (cur >= 0 && !trigger && (tick - st.StartTick) * w.Dt < _p.HoldMinTime) pick = cur;
            else if (cur >= 0 && !trigger && best >= 0 && !UtilityChooser.ShouldReroll(_chooser.Score(cur), _chooser.Score(best), _p.HoldThreshold, _p.NormEps)) pick = cur;
            else { pick = _chooser.Pick(tau, rng); rerolled = true; } // ⑧
            if (best < 0) pick = 0;
            LastPick = pick;

            ref var ch = ref _cands[pick];
            var r = new OffBallResult { Kind = ch.Kind, Target = ch.Target, Rerolled = rerolled };

            // ⑨ 목표점 이동 제한 (FR-46): 같은 행동 유지 중이면 결정 틱당 2 m
            var point = ch.Point;
            if (st.HasAction && st.Kind == ch.Kind && !trigger)
            {
                var d = point - st.Point;
                var len = d.Length();
                if (len > _p.TargetMaxStep) point = st.Point + d * (_p.TargetMaxStep / len);
            }
            r.Point = point;

            // 정지 구간 (FR-43a): 형태·라인 유지 중 목표와 settle_in 안이면 선다, settle_out을 넘으면 다시 따른다
            if (ch.Kind == Consideration.HoldShape || ch.Kind == Consideration.LineHold)
            {
                var err = (ch.Point - w.Pos[s]).Length();
                var was = st.Settled && st.HasAction && st.Kind == ch.Kind;
                if (was ? err <= _p.SettleOut : err < _p.SettleIn)
                {
                    r.Settled = true;
                    r.Point = point = was ? st.Point : w.Pos[s];
                }
            }

            // 출처 (FR-99)
            if (intent.Has && !st.Complied) r.Source = DecisionSource.IntentIgnored;
            else if (intentActive && ch.Match > 0f) r.Source = DecisionSource.IntentMatch;
            else if (intentActive) { r.Source = DecisionSource.Opportunistic; r.ReleaseIntent = true; }
            else r.Source = DecisionSource.General;

            // ⑩ 이동 강도 (FR-70~72)
            var team = DecisionWorld.TeamOf(s);
            var sprintPress = w.GegenActive[team] || w.PressIntensity[team] >= _p.PressSprintAt; // FR-100·101
            r.Intensity = IntensityPicker.Pick(_p, ch.Kind, (point - w.Pos[s]).Length(), w.C[s], DecisionAttributes.N(m.WorkRate), sprintPress, st.Intensity, trigger, w.PossTeam >= 0 && w.PossTeam != team);
            r.MaxSpeed = w.Profile[s].MaxSpeed * IntensityPicker.SpeedRatio(_p, r.Intensity);

            // 상태 갱신
            if (!st.HasAction || st.Kind != ch.Kind) st.StartTick = tick;
            st.HasAction = true;
            st.Kind = ch.Kind;
            st.Point = point;
            st.Target = ch.Target;
            st.Intensity = r.Intensity;
            st.Source = r.Source;
            st.LastDecisionTick = tick;
            st.Settled = r.Settled;
            return r;
        }

        /// <summary>의도 일치도 m (FR-92): 종류 일치 × max(0, 1 − d/r_intent) × 창 안 1 / 밖 감쇠.</summary>
        public float MatchOf(in OffBallCandidate c, in IntentPacket intent)
        {
            if (!intent.Has || c.Kind != intent.Kind) return 0f;
            var d = (c.Point - intent.Point).Length();
            var near = _p.IntentR > 1e-6f ? 1f - d / _p.IntentR : 0f;
            if (near <= 0f) return 0f;
            return near * (intent.InWindow ? 1f : _p.IntentOutWindow);
        }
    }
}
