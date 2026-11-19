using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>GK 배급 방식 (specs/074 FR-10). 번호는 이벤트·세이브에 들어가므로 바꾸지 않는다.</summary>
    public enum DistributionKind : byte
    {
        Roll = 0,
        Throw = 1,
        ShortKick = 2,
        LongKick = 3,
        Punt = 4,
        DropKick = 5,
    }

    /// <summary>GK가 가진 공의 상태 — 쓸 수 있는 방식을 정한다 (FR-10·12).</summary>
    public enum DistributionSource : byte
    {
        /// <summary>손에 든 공 (캐치 뒤): 굴리기·던지기·펀트·드롭킥 + 내려놓고 짧은 킥.</summary>
        Hands = 0,
        /// <summary>골킥 (데드볼, 손 없음): 짧은·롱 골킥.</summary>
        GoalKick = 1,
        /// <summary>발로 받은 공 (백패스 등): 짧은 킥·롱킥.</summary>
        Feet = 2,
    }

    /// <summary>배급 결정 입력 (상황 경향 FR-12 · 팀 지시 · 역할 · 시나리오 의도).</summary>
    public struct DistributionContext
    {
        public DistributionSource Source;
        /// <summary>캐치 + 상대 미정렬 (G-DS-01 빠른 스로).</summary>
        public bool Quick;
        /// <summary>압박 높음 (G-DS-03 롱 배급).</summary>
        public bool HighPress;
        /// <summary>팀 지시 Directness 0~1 (009/072 FR-11).</summary>
        public float Directness;
        public KeeperRole Role;
        public float DecisionsN;
        /// <summary>시나리오 의도 지점 (011 FR-94 효용 보너스, G-DS). 없으면 HasIntent false.</summary>
        public bool HasIntent;
        public Vec2 IntentPoint;
    }

    /// <summary>배급 후보 하나 (받는 선수별 최선 방식).</summary>
    public struct DistributionCandidate
    {
        public DistributionKind Kind;
        public int Target;
        public Vec2 Aim;
        public float Speed, Elevation;
        /// <summary>성공 확률 (008 xPass 또는 롱 계열 007 낙하점 컨트롤).</summary>
        public float P;
        public float DeltaXt;
        /// <summary>기대효용 = P × max(floor, 1 + xtK·ΔxT) × 경향 배수.</summary>
        public float Eu;
    }

    /// <summary>
    /// GK 배급 (specs/074 FR-10~13, 리서치 04 § A6). 후보 = 같은 팀 필드 선수(슬롯 오름차순) × 공 상태가 허용하는 방식(번호 오름차순), 방식별 사거리 밖은 제외.
    /// 성공 확률: 굴리기·던지기·짧은 킥 = 008 패스 모델(굴리기는 땅볼만), 롱 골킥·펀트·드롭킥 = 받는 선수 위치 낙하점의 007 지점 질의(공 도착 시각 = 포물선 근사).
    /// EU = P × max(floor, 1 + xtK·ΔxT) — 008 대체 패스 점수와 같은 꼴(xT가 상대 골 60 m 밖에서 0이라 순수 ΔxT × P면 자기 진영 배급이 전부 0이 된다).
    /// 경향(빠른 스로·숏·롱)·Directness·역할은 배수, 의도는 010 공용 선택기의 보너스로만. 받는 선수마다 최선 방식 1개 → 010 정규화 + softmax(온도 = Decisions) 1회.
    /// 양수 EU 후보가 없으면 롱 배급 강제(가장 앞의 동료, 사거리 안으로 자름). 할당 없음.
    /// </summary>
    public sealed class DistributionModel
    {
        public const int Kinds = 6;
        public const int MaxTargets = 10;
        const float Eps = 1e-6f;

        readonly GoalkeeperParams _p;
        readonly DistributionCandidate[] _cands = new DistributionCandidate[MaxTargets];
        readonly float[] _u = new float[MaxTargets];
        readonly float[] _m = new float[MaxTargets];
        readonly bool[] _in = new bool[MaxTargets];
        readonly UtilityAI.UtilityChooser _chooser = new UtilityAI.UtilityChooser();
        int _count;

        public DistributionModel(GoalkeeperParams p) => _p = p;

        public int Count => _count;
        public ref readonly DistributionCandidate Candidate(int i) => ref _cands[i];

        public static bool Allowed(DistributionKind k, DistributionSource s) => s switch
        {
            DistributionSource.Hands => k != DistributionKind.LongKick,
            DistributionSource.GoalKick => k == DistributionKind.ShortKick || k == DistributionKind.LongKick,
            _ => k == DistributionKind.ShortKick || k == DistributionKind.LongKick,
        };

        public static bool IsHand(DistributionKind k) => k == DistributionKind.Roll || k == DistributionKind.Throw;
        public static bool IsLong(DistributionKind k) => k == DistributionKind.LongKick || k == DistributionKind.Punt || k == DistributionKind.DropKick;

        public void Range(DistributionKind k, out float min, out float max)
        {
            switch (k)
            {
                case DistributionKind.Roll: min = _p.DistRollMin; max = _p.DistRollMax; return;
                case DistributionKind.Throw: min = _p.DistThrowMin; max = _p.DistThrowMax; return;
                case DistributionKind.ShortKick: min = _p.DistShortMin; max = _p.DistShortMax; return;
                case DistributionKind.DropKick: min = _p.DistDropMin; max = _p.DistDropMax; return;
                default: min = _p.DistLongMin; max = _p.DistLongMax; return;
            }
        }

        public bool InRange(DistributionKind k, float dist)
        {
            Range(k, out var lo, out var hi);
            return dist >= lo && dist <= hi;
        }

        public float Elevation(DistributionKind k) => k == DistributionKind.Punt ? _p.DistElevPunt : (k == DistributionKind.DropKick ? _p.DistElevDrop : _p.DistElevLong);

        /// <summary>σ_angle 배수 (굴리기 낮음 · 던지기 중간 · 짧은 킥 패스 공용 · 롱 계열 1).</summary>
        public float SigmaMul(DistributionKind k) => k switch
        {
            DistributionKind.Roll => _p.DistSigmaRoll,
            DistributionKind.Throw => _p.DistSigmaThrow,
            DistributionKind.ShortKick => _p.DistSigmaShort,
            _ => 1f,
        };

        /// <summary>FR-12 경향·Directness·역할 배수 (011 FR-71).</summary>
        public float Multiplier(DistributionKind k, in DistributionContext c)
        {
            var m = 1f;
            if (c.Quick && k == DistributionKind.Throw) m *= _p.DistQuickBonus;
            if (c.Source == DistributionSource.Feet && k == DistributionKind.ShortKick) m *= _p.DistShortBonus;
            if (IsLong(k))
            {
                if (c.HighPress) m *= _p.DistLongBonus;
                var d = 1f + _p.DistDirectnessK * (c.Directness - 0.5f);
                m *= d > 0.05f ? d : 0.05f;
                if (c.Role == KeeperRole.Traditional) m *= _p.DistRoleTraditionalLong;
            }
            else if (c.Role == KeeperRole.Sweeper && (k == DistributionKind.ShortKick || k == DistributionKind.Roll)) m *= _p.DistRoleSweeperShort;
            return m;
        }

        /// <summary>EU 꼴 (008 대체 패스 점수와 같음).</summary>
        public static float Utility(ActionParams ap, float p, float deltaXt) => p * DMath.Max(ap.StandInFloor, 1f + ap.StandInXtK * deltaXt);

        /// <summary>
        /// 후보 평가: 월드 w(이번 틱으로 채운 것), GK 슬롯 gk, 공 위치 from, GK에 대한 압박. 받는 선수별 최선 방식만 남긴다. 반환 = 후보 수(0이면 강제 롱 대상 없음 아님 — <see cref="Forced"/>).
        /// </summary>
        public int Evaluate(ActionWorld w, ActionParams ap, BallParams bp, int gk, Vec2 from, float pressure, in DistributionContext c)
        {
            _count = 0;
            var team = ActionWorld.TeamOf(gk);
            var t0 = team * ActionWorld.PerTeam;
            var fromXt = w.Threat(ap, team, from);
            for (int o = t0; o < t0 + ActionWorld.PerTeam; o++)
            {
                if (o == gk || !w.Active[o]) continue;
                var aim = w.Pos[o];
                var dist = (aim - from).Length();
                var best = default(DistributionCandidate);
                var has = false;
                // ① 짧은 계열 (008 패스 모델) — 던지기·짧은 킥은 같은 평가(Any)를 한 번만, 굴리기는 Any 최선이 땅볼이면 그것을 그대로(같은 결과)
                var anyDone = false;
                var anyOpt = default(PassOption);
                for (int ki = 0; ki < Kinds; ki++)
                {
                    var k = (DistributionKind)ki;
                    if (IsLong(k) || !Allowed(k, c.Source) || !InRange(k, dist)) continue;
                    if (!anyDone)
                    {
                        anyOpt = PassModel.Best(ap, bp, w, gk, o, from, aim, pressure, PassModel.LoftMode.Any);
                        anyDone = true;
                    }
                    var opt = k != DistributionKind.Roll || (anyOpt.Valid && anyOpt.Elevation <= 0f) ? anyOpt
                        : PassModel.Best(ap, bp, w, gk, o, from, aim, pressure, PassModel.LoftMode.GroundOnly);
                    if (!opt.Valid || !InRange(k, (opt.Aim - from).Length())) continue; // 008 조준점이 사거리 밖이면 그 방식 후보에서 제외
                    var cand = new DistributionCandidate { Kind = k, Target = o, Aim = opt.Aim, Speed = opt.Speed, Elevation = opt.Elevation, P = opt.XPass };
                    cand.DeltaXt = w.Threat(ap, team, cand.Aim) - fromXt;
                    cand.Eu = Utility(ap, cand.P, cand.DeltaXt) * Multiplier(k, in c);
                    if (!has || cand.Eu > best.Eu) { best = cand; has = true; }
                }
                // ② 롱 계열 (007 낙하점 지점 질의) — 받는 선수 위치가 같으므로 질의는 받는 선수당 최대 1회(첫 방식의 공 도착 시각),
                //    P = 1이어도 이 선수의 짧은 계열 최선을 못 넘으면 질의하지 않는다 (결과 동일 — 받는 선수별 최선만 남기므로)
                var dxt = w.Threat(ap, team, aim) - fromXt;
                var queried = false;
                var pLong = 0f;
                for (int ki = 0; ki < Kinds; ki++)
                {
                    var k = (DistributionKind)ki;
                    if (!IsLong(k) || !Allowed(k, c.Source) || !InRange(k, dist)) continue;
                    var e = Elevation(k);
                    if (!PassModel.LoftedApprox(bp, dist, e, out var sp, out var tf)) continue;
                    var mul = Multiplier(k, in c);
                    var upper = Utility(ap, 1f, dxt) * mul;
                    if (has && upper <= best.Eu) continue;
                    if (!queried)
                    {
                        pLong = w.PitchControl.Point(w.ControlPlayersView, team, aim, tf, true, gk).Att;
                        queried = true;
                    }
                    var cand = new DistributionCandidate { Kind = k, Target = o, Aim = aim, Speed = sp, Elevation = e, P = pLong, DeltaXt = dxt };
                    cand.Eu = Utility(ap, pLong, dxt) * mul;
                    if (!has || cand.Eu > best.Eu) { best = cand; has = true; }
                }
                if (has && best.Eu > 0f && _count < MaxTargets) _cands[_count++] = best;
            }
            return _count;
        }

        /// <summary>
        /// 후보 0 경계: 롱 배급 강제 — 공격 방향으로 가장 앞의 동료, 거리는 롱 사거리 안으로 자른다(손이면 펀트, 아니면 롱 골킥). 동료가 없으면 앞으로 롱 최소 사거리.
        /// </summary>
        public DistributionCandidate Forced(ActionWorld w, BallParams bp, int gk, Vec2 from, DistributionSource source)
        {
            var team = ActionWorld.TeamOf(gk);
            var dir = w.AttackDir[team];
            var t0 = team * ActionWorld.PerTeam;
            var target = -1;
            var bestX = float.MinValue;
            for (int o = t0; o < t0 + ActionWorld.PerTeam; o++)
            {
                if (o == gk || !w.Active[o]) continue;
                var x = w.Pos[o].X * dir;
                if (x > bestX) { bestX = x; target = o; }
            }
            var k = source == DistributionSource.Hands ? DistributionKind.Punt : DistributionKind.LongKick;
            Range(k, out var lo, out var hi);
            var aim = target >= 0 ? w.Pos[target] : new Vec2(from.X + dir * lo, from.Y);
            var d = aim - from;
            var len = d.Length();
            var u = len > Eps ? d / len : new Vec2(dir, 0f);
            var dist = len < lo ? lo : (len > hi ? hi : len);
            aim = from + u * dist;
            aim = new Vec2(Interp.Clamp(aim.X, 1f, w.PitchL - 1f), Interp.Clamp(aim.Y, 1f, w.PitchW - 1f));
            var e = Elevation(k);
            PassModel.LoftedApprox(bp, (aim - from).Length(), e, out var sp, out _);
            return new DistributionCandidate { Kind = k, Target = target, Aim = aim, Speed = sp, Elevation = e };
        }

        /// <summary>010 공용 선택기: 정규화 + 의도 보너스(지점 일치, r_intent) + 기회주의 필터 + softmax(τ = Decisions). RNG는 softmax 1회.</summary>
        public int Choose(in DistributionContext c, SeedRng rng)
        {
            if (_count <= 1) return 0;
            for (int i = 0; i < _count; i++)
            {
                _u[i] = _cands[i].Eu;
                _in[i] = true;
                var m = 0f;
                if (c.HasIntent && _p.ChoiceWIntent > 0f && _p.ChoiceRIntent > Eps)
                {
                    var near = 1f - (_cands[i].Aim - c.IntentPoint).Length() / _p.ChoiceRIntent;
                    m = near > 0f ? near : 0f;
                }
                _m[i] = m;
            }
            var ms = new ReadOnlySpan<float>(_m, 0, _count);
            _chooser.Evaluate(new ReadOnlySpan<float>(_u, 0, _count), ms, new ReadOnlySpan<bool>(_in, 0, _count), default, _p.ChoiceWIntent, Eps);
            _chooser.FilterOpportunistic(ms, _p.ChoiceThetaOpp);
            return _chooser.Pick(KeeperMapping.Temperature(_p, c.DecisionsN), rng);
        }

        /// <summary>074 FR-14: 캐치 때 뽑는 배급 마감(s) = 목표 + |N(0, σ_hold·(1 − n_Decisions))|. RNG 정규 1.</summary>
        public static float DeadlineSeconds(GoalkeeperParams p, float decisionsN, SeedRng rng) =>
            p.HoldTarget + DMath.Abs(rng.Normal(0f, p.HoldSigma * (1f - decisionsN)));

        /// <summary>
        /// 074 FR-14 손 보유 배급 시점: 최소 대기(빠른 스로 조건이면 0) 전에는 기다리고, 그 뒤 최선 EU ≥ goEu이거나 후보가 없거나 마감이면 배급.
        /// </summary>
        public static bool ReleaseNow(GoalkeeperParams p, float elapsed, float deadline, bool quick, bool anyCandidate, float bestEu)
        {
            if (elapsed < (quick ? 0f : p.HoldSettle)) return false;
            return !anyCandidate || bestEu >= p.HoldGoEu || elapsed >= deadline;
        }

        /// <summary>EU 최댓값 후보 (동점 = 낮은 인덱스). 없으면 −1.</summary>
        public int Best()
        {
            var b = -1;
            for (int i = 0; i < _count; i++)
                if (b < 0 || _cands[i].Eu > _cands[b].Eu) b = i;
            return b;
        }
    }

    /// <summary>
    /// 배급 실행 오차 (specs/074 FR-10·13): σ_angle = (6° − 4.5°·n) × 방식 배수(011 FR-70 Distribution 매핑), 롱 계열 거리 오차 σ_dist = sigmaDist·(1 − 0.75·n).
    /// 롱 계열은 조준점을 흔들고(각도·거리 정규 2회) 호출자가 004 역산으로 초속을 구한다. 짧은 계열은 008 실행 오차(<see cref="PassExecution.Apply"/>)에 GK σ를 넣는다.
    /// </summary>
    public static class DistributionExecution
    {
        public static float SigmaAngleDeg(GoalkeeperParams p, DistributionModel m, DistributionKind k, float distributionN) =>
            KeeperMapping.DistributionSigmaDeg(p, distributionN) * m.SigmaMul(k);

        /// <summary>σ_dist ∝ 1/Distribution: 011 σ_angle 매핑(6 − 4.5n)과 같은 비율 1 − 0.75n (n = 1이면 1/4).</summary>
        public static float SigmaDist(GoalkeeperParams p, float distributionN) => p.DistSigmaDist * (1f - 0.75f * distributionN);

        /// <summary>롱 계열 조준점 흔들기: 각도 N(0, σ_angle)·거리 비 N(0, σ_dist). RNG 정규 정확히 2.</summary>
        public static Vec2 PerturbAim(Vec2 from, Vec2 aim, float sigmaAngleDeg, float sigmaDist, SeedRng rng, out float angleErrDeg, out float distErr)
        {
            angleErrDeg = rng.Normal(0f, sigmaAngleDeg);
            distErr = rng.Normal(0f, sigmaDist);
            var d = aim - from;
            var len = d.Length();
            if (len < 1e-5f) return aim;
            var a = angleErrDeg * (3.14159265f / 180f);
            var c = DMath.Cos(a);
            var s = DMath.Sin(a);
            var u = d / len;
            var r = new Vec2(u.X * c - u.Y * s, u.X * s + u.Y * c);
            var f = 1f + distErr;
            if (f < 0.2f) f = 0.2f;
            return from + r * (len * f);
        }
    }
}
