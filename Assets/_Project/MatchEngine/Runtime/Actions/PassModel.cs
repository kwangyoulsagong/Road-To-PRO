using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>속도 후보 하나의 평가 결과 (FR-11·12).</summary>
    public struct PassOption
    {
        public bool Valid;
        public Vec2 Aim;
        /// <summary>초속 (m/s, 수평+수직 합).</summary>
        public float Speed;
        /// <summary>발사각 (rad). 0 = 땅볼.</summary>
        public float Elevation;
        public float ArriveTime, ArriveSpeed;
        public float Logit, XPass;
        /// <summary>속도 후보 번호 (0~3 땅볼, 4~5 뜬공).</summary>
        public int Candidate;
        public bool Lofted => Elevation > 0f;
    }

    /// <summary>
    /// 패스 평가 (specs/008 FR-11·12·14) — 순수 함수, RNG 없음. 실행 오차(FR-13)는 <see cref="PassExecution"/>.
    /// logit P = b0 − b1·dist/10 − b2·pressureOnPasser − b3·lanePressure + b4·receiverPC + b5·forwardness + b6·(Passing − 0.5) + b7·(Vision − 0.5).
    /// 속도 후보별로 레인 압박에 시간 배수(느린 공 = 상대에게 시간)와 수신 난이도 ln λ_recv를 더해 가장 큰 P를 고른다(ΔxT는 같은 목표에서 상수).
    /// </summary>
    public static class PassModel
    {
        public const int GroundCandidates = 4, LoftedCandidates = 2, Candidates = GroundCandidates + LoftedCandidates;
        const float Eps = 1e-6f;

        /// <summary>FR-11 기본 logit.</summary>
        public static float Logit(ActionParams p, float dist, float pressOnPasser, float lanePressure, float receiverPC, float forwardness, float aPassing, float aVision)
        {
            var l = p.XpB0 - p.XpB1 * dist / 10f - p.XpB2 * pressOnPasser - p.XpB3 * lanePressure + p.XpB4 * receiverPC + p.XpB5 * forwardness;
            l = ActionMath.LogitAdd(l, p.XpB6, aPassing);
            return ActionMath.LogitAdd(l, p.XpB7, aVision);
        }

        public static float XPass(ActionParams p, float dist, float pressOnPasser, float lanePressure, float receiverPC, float forwardness, float aPassing, float aVision)
            => ActionMath.Prob(Logit(p, dist, pressOnPasser, lanePressure, receiverPC, forwardness, aPassing, aVision), p);

        /// <summary>forwardness ∈ [−1, 1]: 패스 방향의 공격 방향 성분.</summary>
        public static float Forwardness(Vec2 from, Vec2 to, int attackDir)
        {
            var d = to - from;
            var len = d.Length();
            if (len < Eps) return 0f;
            var f = d.X * attackDir / len;
            return f < -1f ? -1f : (f > 1f ? 1f : f);
        }

        /// <summary>
        /// 레인 압박 (FR-11 lanePressure, 형태 [대체]): 패서 상대 중 선분 안쪽(투영 0~1)에서 레인 반폭 안에 있는 선수마다 w = 1 − 횡거리/반폭, 합성 1 − Π(1 − w).
        /// </summary>
        public static float LanePressure(ActionParams p, ActionWorld w, int passerTeam, Vec2 from, Vec2 to)
        {
            var d = to - from;
            var lenSq = d.LengthSq();
            if (lenSq < Eps) return 0f;
            var inv = 1f / lenSq;
            var len = DMath.Sqrt(lenSq);
            var half = DMath.Max(p.XpLaneWidth, 1e-3f);
            var opp = 1 - passerTeam;
            var prod = 1f;
            for (int o = opp * ActionWorld.PerTeam; o < (opp + 1) * ActionWorld.PerTeam; o++)
            {
                if (!w.Active[o]) continue;
                var r = w.Pos[o] - from;
                var t = (r.X * d.X + r.Y * d.Y) * inv;
                if (t <= 0f || t >= 1f) continue;
                var lat = DMath.Abs(r.X * d.Y - r.Y * d.X) / len;
                if (lat >= half) continue;
                prod *= lat / half; // 1 − w
            }
            return 1f - prod;
        }

        /// <summary>FR-12 수신 감쇠 λ_recv 배수 = lerp(1, decayMin, (v − 12)/12), 퍼스트터치가 높을수록 감쇠가 줄어든다(relief·a_FT).</summary>
        public static float RecvDecay(ActionParams p, float arriveSpeed, float aFirstTouch)
        {
            var raw = ActionMath.Lerp(1f, p.RecvDecayMin, (arriveSpeed - p.RecvSpeedLo) / DMath.Max(p.RecvSpeedSpan, Eps));
            return raw + (1f - raw) * p.RecvTouchRelief * aFirstTouch;
        }

        /// <summary>땅볼 초속 v0로 거리 dist에 도착할 때 속도·시간 (004 구름 표). 멈추기 전에 못 가면 false.</summary>
        public static bool GroundArrival(BallParams bp, float v0, float dist, out float arriveSpeed, out float time)
        {
            var table = bp.RollTable;
            var x0 = table.DistanceAtSpeed(v0);
            var x1 = x0 + dist;
            if (dist <= 0f) { arriveSpeed = v0; time = 0f; return true; }
            if (x1 >= table.TotalDistance) { arriveSpeed = 0f; time = 0f; return false; }
            table.Sample(x1, out var s1, out arriveSpeed);
            time = (s1 - table.StepAtDistance(x0)) * bp.Dt;
            return arriveSpeed >= bp.ToRestSpeed;
        }

        /// <summary>구름 표 시작값(x0·step0)을 이미 알 때 — <see cref="GroundArrival"/>과 같은 값.</summary>
        public static bool GroundArrivalFrom(BallParams bp, float v0, float x0, float step0, float dist, out float arriveSpeed, out float time)
        {
            var table = bp.RollTable;
            if (dist <= 0f) { arriveSpeed = v0; time = 0f; return true; }
            var x1 = x0 + dist;
            if (x1 >= table.TotalDistance) { arriveSpeed = 0f; time = 0f; return false; }
            table.Sample(x1, out var s1, out arriveSpeed);
            time = (s1 - step0) * bp.Dt;
            return arriveSpeed >= bp.ToRestSpeed;
        }

        /// <summary>뜬공 진공 탄도 근사 (평가용): 발사각 θ로 dist에 떨어지는 초속·체공 시간. 실행은 004 역산을 쓴다.</summary>
        public static bool LoftedApprox(BallParams bp, float dist, float elevation, out float speed, out float time)
        {
            var g = DMath.Abs(bp.Gravity);
            var s2 = DMath.Sin(2f * elevation);
            if (!(dist > 0f) || s2 <= Eps) { speed = 0f; time = 0f; return false; }
            speed = DMath.Sqrt(dist * g / s2);
            time = 2f * speed * DMath.Sin(elevation) / g;
            return speed <= bp.LimitSpeed;
        }

        /// <summary>발사각 삼각함수를 미리 안 때 (<see cref="LoftedApprox(BallParams, float, float, out float, out float)"/>와 같은 값).</summary>
        public static bool LoftedApprox(BallParams bp, float dist, float sin2e, float sine, out float speed, out float time)
        {
            var g = DMath.Abs(bp.Gravity);
            if (!(dist > 0f) || sin2e <= Eps) { speed = 0f; time = 0f; return false; }
            speed = DMath.Sqrt(dist * g / sin2e);
            time = 2f * speed * sine / g;
            return speed <= bp.LimitSpeed;
        }

        public static float GroundSpeed(ActionParams p, int i) => i switch { 0 => p.SpeedG0, 1 => p.SpeedG1, 2 => p.SpeedG2, _ => p.SpeedG3 };
        public static float Lead(ActionParams p, int i) => i switch { 0 => p.Lead0, 1 => p.Lead1, 2 => p.Lead2, _ => p.Lead3 };

        /// <summary>
        /// FR-12: 목표점 aim으로 보내는 속도 후보(땅볼 4 + 뜬공 2)를 평가해 xPass가 가장 큰 것을 고른다. 1 m 이하 초근접은 최저 속도 땅볼만.
        /// 후보가 하나도 없으면 Valid = false.
        /// </summary>
        public static PassOption Best(ActionParams p, BallParams bp, ActionWorld w, int passer, int receiver, Vec2 from, Vec2 aim, float pressOnPasser)
            => Best(p, bp, w, passer, receiver, from, aim, pressOnPasser, LoftMode.Any);

        /// <summary>후보 제한: 시나리오가 땅볼·공중을 지정할 때 (013 동사).</summary>
        /// <remarks>ThroughSpeed = 스루볼: 조준점을 푼 땅볼 속도(후보 2) 하나만 (조준점이 속도에 따라 달라서 다른 속도는 의미 없음).</remarks>
        public enum LoftMode : byte { Any = 0, GroundOnly = 1, LoftedOnly = 2, ThroughSpeed = 3 }

        public static PassOption Best(ActionParams p, BallParams bp, ActionWorld w, int passer, int receiver, Vec2 from, Vec2 aim, float pressOnPasser, LoftMode mode)
        {
            var best = default(PassOption);
            if (receiver == passer || receiver < 0) return best;
            var team = ActionWorld.TeamOf(passer);
            var dist = (aim - from).Length();
            var lane = LanePressure(p, w, team, from, aim);
            var pc = w.Control(p, receiver, aim);
            var fwd = Forwardness(from, aim, w.AttackDir[team]);
            var baseNoLane = Logit(p, dist, pressOnPasser, 0f, pc, fwd, w.Passing[passer], w.Vision[passer]);
            var refTime = dist / DMath.Max(p.LaneRefSpeed, Eps);
            var ft = w.FirstTouch[receiver];
            var n = dist <= 1f ? 1 : Candidates;
            var cached = ReferenceEquals(w.RollFor, bp) && ReferenceEquals(w.RollParams, p);
            var bestLogit = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (mode == LoftMode.GroundOnly && i >= GroundCandidates) break;
                if (mode == LoftMode.ThroughSpeed && dist > 1f && i != 2) { if (i > 2) break; continue; }
                if (mode == LoftMode.LoftedOnly && i < GroundCandidates && dist > 1f) continue;
                var o = default(PassOption);
                o.Aim = aim;
                o.Candidate = i;
                float laneMul;
                if (i < GroundCandidates)
                {
                    o.Speed = GroundSpeed(p, i);
                    var ok = cached
                        ? GroundArrivalFrom(bp, o.Speed, w.RollX0[i], w.RollStep0[i], dist, out o.ArriveSpeed, out o.ArriveTime)
                        : GroundArrival(bp, o.Speed, dist, out o.ArriveSpeed, out o.ArriveTime);
                    if (!ok) continue;
                    var tm = refTime > Eps ? o.ArriveTime / refTime : 1f;
                    laneMul = tm < 0.5f ? 0.5f : (tm > 2f ? 2f : tm);
                }
                else
                {
                    var li = i - GroundCandidates;
                    o.Elevation = (li == 0 ? p.LoftLow : p.LoftHigh) * ActionMath.DegToRad;
                    var ok = cached
                        ? LoftedApprox(bp, dist, w.LoftSin2[li], w.LoftSin[li], out o.Speed, out o.ArriveTime)
                        : LoftedApprox(bp, dist, o.Elevation, out o.Speed, out o.ArriveTime);
                    if (!ok) continue;
                    o.ArriveSpeed = o.Speed;
                    laneMul = p.LoftLane;
                }
                var logit = baseNoLane - p.XpB3 * lane * laneMul + DMath.Log(RecvDecay(p, o.ArriveSpeed, ft));
                if (o.Lofted) logit -= p.LoftPenalty;
                o.Logit = logit;
                o.Valid = true;
                // xPass는 logit에 단조 — 최고 logit 하나만 확률로 바꾼다 (동률은 낮은 후보)
                if (logit > bestLogit) { bestLogit = logit; best = o; }
            }
            if (best.Valid) best.XPass = ActionMath.Prob(best.Logit, p);
            return best;
        }

        /// <summary>
        /// FR-14 2차방정식: |q + v_r·τ| = u·τ (q = r + lead·û − s)의 가장 작은 양의 해 (상한 horizon). 해 없으면 false.
        /// </summary>
        public static bool SolveIntercept(Vec2 q, Vec2 vr, float u, float horizon, out float tau)
        {
            tau = 0f;
            var a = vr.LengthSq() - u * u;
            var b = 2f * Vec2.Dot(q, vr);
            var c = q.LengthSq();
            if (DMath.Abs(a) < 1e-6f)
            {
                if (b >= 0f) return false;
                tau = -c / b;
                return tau > 0f && tau <= horizon;
            }
            var disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            var sq = DMath.Sqrt(disc);
            var t1 = (-b - sq) / (2f * a);
            var t2 = (-b + sq) / (2f * a);
            if (t1 > t2) { var tmp = t1; t1 = t2; t2 = tmp; }
            if (t1 > 0f && t1 <= horizon) { tau = t1; return true; }
            if (t2 > 0f && t2 <= horizon) { tau = t2; return true; }
            return false;
        }

        /// <summary>
        /// FR-14 스루볼 조준점: 러너(r, v_r)의 진행 방향으로 lead만큼 앞. 먼저 평균 속도 u = 0.8·v0로 2차방정식의 가장 작은 양의 해를 구하고,
        /// 구름 감속을 반영해 h(τ) = t_공(|aim(τ) − s|) − τ = 0을 할선법으로 고정 6회 다듬는다. 해 없음·도달 불가면 false(후보 제외).
        /// 러너가 거의 서 있으면 공격 방향을 진행 방향으로 쓴다.
        /// </summary>
        public static bool ThroughAim(ActionParams p, BallParams bp, Vec2 from, Vec2 r, Vec2 vr, int attackDir, float lead, float v0, out Vec2 aim, out float tau)
        {
            aim = default;
            tau = 0f;
            var sp = vr.Length();
            var dir = sp > 0.5f ? vr / sp : new Vec2(attackDir, 0f);
            var q = r + dir * lead - from;
            if (!SolveIntercept(q, vr, v0 * 0.8f, p.LeadHorizon, out var t0)) return false;
            if (!BallTime(bp, from, q, vr, v0, t0, out var b0)) return false;
            var h0 = b0 - t0;
            var t1 = b0;
            for (int it = 0; it < 6; it++)
            {
                if (!(t1 > 0f) || t1 > p.LeadHorizon) return false;
                if (!BallTime(bp, from, q, vr, v0, t1, out var b1)) return false;
                var h1 = b1 - t1;
                var den = h1 - h0;
                var next = DMath.Abs(den) < 1e-7f ? b1 : t1 - h1 * (t1 - t0) / den;
                t0 = t1; h0 = h1; t1 = next;
            }
            if (!(t1 > 0f) || t1 > p.LeadHorizon) return false;
            tau = t1;
            aim = from + q + vr * tau;
            return BallTime(bp, from, q, vr, v0, tau, out _);
        }

        /// <summary>조준점 aim(τ) = s + q + v_r·τ까지 땅볼 v0의 도착 시간.</summary>
        static bool BallTime(BallParams bp, Vec2 from, Vec2 q, Vec2 vr, float v0, float tau, out float t)
        {
            var d = (q + vr * tau).Length();
            return GroundArrival(bp, v0, d, out _, out t);
        }

        /// <summary><see cref="ThroughAim"/>의 구름 시작값 캐시판 (같은 값). v0는 후보 ci의 속도.</summary>
        public static bool ThroughAim(ActionParams p, BallParams bp, ActionWorld w, int ci, Vec2 from, Vec2 r, Vec2 vr, int attackDir, float lead, out Vec2 aim, out float tau)
        {
            var v0 = GroundSpeed(p, ci);
            if (!(ReferenceEquals(w.RollFor, bp) && ReferenceEquals(w.RollParams, p))) return ThroughAim(p, bp, from, r, vr, attackDir, lead, v0, out aim, out tau);
            aim = default;
            tau = 0f;
            var x0 = w.RollX0[ci];
            var st0 = w.RollStep0[ci];
            var sp = vr.Length();
            var dir = sp > 0.5f ? vr / sp : new Vec2(attackDir, 0f);
            var q = r + dir * lead - from;
            if (!SolveIntercept(q, vr, v0 * 0.8f, p.LeadHorizon, out var t0)) return false;
            if (!GroundArrivalFrom(bp, v0, x0, st0, (q + vr * t0).Length(), out _, out var b0)) return false;
            var h0 = b0 - t0;
            var t1 = b0;
            for (int it = 0; it < 6; it++)
            {
                if (!(t1 > 0f) || t1 > p.LeadHorizon) return false;
                if (!GroundArrivalFrom(bp, v0, x0, st0, (q + vr * t1).Length(), out _, out var b1)) return false;
                var h1 = b1 - t1;
                var den = h1 - h0;
                var next = DMath.Abs(den) < 1e-7f ? b1 : t1 - h1 * (t1 - t0) / den;
                t0 = t1; h0 = h1; t1 = next;
            }
            if (!(t1 > 0f) || t1 > p.LeadHorizon) return false;
            tau = t1;
            aim = from + q + vr * tau;
            return GroundArrivalFrom(bp, v0, x0, st0, (q + vr * tau).Length(), out _, out _);
        }
    }
}
