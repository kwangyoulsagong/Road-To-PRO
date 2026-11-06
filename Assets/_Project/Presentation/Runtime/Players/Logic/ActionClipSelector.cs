using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 액션 클립 요청 (시각은 시뮬 초 = 재생 시계 기준). 하이라이트는 이미 시뮬된 데이터라 접촉 시각·위치를 미리 안다(research 06 § 3.2).
    /// </summary>
    public readonly struct ActionRequest
    {
        /// <summary>이 액션을 시작할 수 있는 가장 이른 시각(앞 액션·장면 시작).</summary>
        public readonly float Now;
        /// <summary>시뮬 접촉 시각.</summary>
        public readonly float ContactTime;
        /// <summary>접촉 순간 시뮬 선수 위치(루트, 바닥) · 정면 각(라디안, 0 = +Z).</summary>
        public readonly Vec3 SimPos;
        public readonly float Yaw;
        /// <summary>접촉 순간 시뮬 공 중심 위치·결과 공 속도.</summary>
        public readonly Vec3 Ball;
        public readonly Vec3 BallVelOut;
        /// <summary>접촉 직전 선수 속력(m/s) — 접근 보정 걸음 계산.</summary>
        public readonly float Speed;
        /// <summary>시뮬이 정한 사용 부위(None이면 제한 없음).</summary>
        public readonly BodyPart Part;
        /// <summary>선수 킥 원형 (055 FR-61). None이면 원형 점수 없음(043과 같은 선택).</summary>
        public readonly KickArchetype KickStyle;
        /// <summary>선수 키 배율 (054 FR-87): 클립 접촉점도 같이 커진다.</summary>
        public readonly float Scale;

        public ActionRequest(float now, float contactTime, Vec3 simPos, float yaw, Vec3 ball, Vec3 ballVelOut, float speed, BodyPart part,
            KickArchetype kickStyle = KickArchetype.None, float scale = 1f)
        {
            KickStyle = kickStyle;
            Scale = scale;
            Now = now;
            ContactTime = contactTime;
            SimPos = simPos;
            Yaw = yaw;
            Ball = ball;
            BallVelOut = ballVelOut;
            Speed = speed;
            Part = part;
        }
    }

    /// <summary>선택 결과. Start부터 Rate 배로 재생하면 ContactAt에 접촉 프레임이 온다.</summary>
    public readonly struct ActionChoice : IEquatable<ActionChoice>
    {
        public readonly int Clip;
        public readonly float Start, Rate, ContactAt, End;
        /// <summary>접근 보정 보폭 배율 (1 ± MaxStrideChange 안, AC-08).</summary>
        public readonly float StrideScale;
        /// <summary>시뮬 루트 → 클립이 요구하는 루트 위치 차(수평, m). 화면 루트 보정(RootCorrection)이 접근 동안 나눠 적용.</summary>
        public readonly Vec3 RootOffset;
        /// <summary>보정 한계 안 후보가 없어 남은 차이를 루트 보정이 강제로 맞춘다(FR-46).</summary>
        public readonly bool Snap;

        public ActionChoice(int clip, float start, float rate, float contactAt, float end, float strideScale, Vec3 rootOffset, bool snap)
        {
            Clip = clip;
            Start = start;
            Rate = rate;
            ContactAt = contactAt;
            End = end;
            StrideScale = strideScale;
            RootOffset = rootOffset;
            Snap = snap;
        }

        public ActionChoice WithEnd(float end) => new ActionChoice(Clip, Start, Rate, ContactAt, end, StrideScale, RootOffset, Snap);
        public ActionChoice WithRootOffset(Vec3 o) => new ActionChoice(Clip, Start, Rate, ContactAt, End, StrideScale, o, Snap);

        public bool Equals(ActionChoice o) => Clip == o.Clip && Start.Equals(o.Start) && Rate.Equals(o.Rate) && ContactAt.Equals(o.ContactAt) &&
                                              End.Equals(o.End) && StrideScale.Equals(o.StrideScale) && RootOffset == o.RootOffset && Snap == o.Snap;
        public override bool Equals(object obj) => obj is ActionChoice o && Equals(o);
        public override int GetHashCode() => Clip * 397 ^ Start.GetHashCode();
    }

    /// <summary>
    /// 액션 클립 선택 (specs/043 FR-42·43·61, AC-08·11).
    /// ① 접촉 시각 정렬: 시작 = 시뮬 접촉 시각 − 클립 접촉 시각. 시간이 모자라면(원터치) 지금 시작해 빠르게 재생 — 접촉이 늦는 일은 없다.
    /// ② 접근 보정: 클립 접촉점이 시뮬 공 위치에 오도록 필요한 루트 이동을, 시작 전 마지막 2~3걸음 보폭 ±15%로 흡수. 넘으면 그 후보를 버린다.
    /// ③ 점수 = 시간 압축 + 보정량 + 공 높이·속도·접근 각 범위 밖 정도. 가장 작은 후보, 같으면 클립 순서.
    /// 모든 후보가 보정 한계를 넘으면 점수 최소 후보 + 보폭은 한계로 자르고 나머지는 루트 보정(Snap).
    /// </summary>
    public static class ActionClipSelector
    {
        public const float MaxStrideChange = 0.15f;
        /// <summary>이 배속까지는 일반 후보로 원터치를 처리. 넘으면 최단 클립을 필요한 배속으로.</summary>
        public const float MaxCompress = 1.6f;
        /// <summary>접근 보정이 일어나는 시작 전 구간(초).</summary>
        public const float ApproachWindow = 0.9f;
        const float StandStep = 0.45f;

        public static ActionChoice Select(ClipLibrary lib, IReadOnlyList<int> candidates, in ActionRequest q)
        {
            if (candidates == null || candidates.Count == 0) throw new ArgumentException("후보 없음");
            float sy = (float)Math.Sin(q.Yaw), cy = (float)Math.Cos(q.Yaw);
            var fwd = new Vec3(sy, 0, cy);
            float avail = Math.Max(0f, q.ContactTime - q.Now);
            float ballSpeed = q.BallVelOut.Length();
            float approach = 0f;
            var bh = new Vec3(q.BallVelOut.X, 0, q.BallVelOut.Z);
            if (bh.LengthSq() > 1e-6f)
            {
                float a = (float)Math.Atan2(bh.X, bh.Z) - q.Yaw;
                approach = (float)(Math.IEEERemainder(a, 2 * Math.PI) * 180.0 / Math.PI);
            }

            int best = -1, bestAny = -1;
            float bestScore = float.MaxValue, bestAnyScore = float.MaxValue;
            ActionChoice bestC = default, bestAnyC = default;
            for (int n = 0; n < candidates.Count; n++)
            {
                var c = lib.Get(candidates[n]);
                if (q.Part != BodyPart.None && c.HasContact && c.ContactPart != q.Part) continue;
                float lead = c.HasContact ? c.ContactTime : 0f;
                float rate = 1f, start;
                if (lead <= avail) start = q.ContactTime - lead;
                else
                {
                    start = q.Now;
                    rate = avail > 1e-4f ? lead / avail : 1e4f;
                }
                // 접근 보정량
                Vec3 offset = default;
                float stride = 1f;
                bool fits = true;
                if (c.HasContact)
                {
                    var cp = c.ContactPoint * q.Scale;
                    var world = new Vec3(cp.X * cy + cp.Z * sy, 0, -cp.X * sy + cp.Z * cy);
                    var need = new Vec3(q.Ball.X - world.X, 0, q.Ball.Z - world.Z);
                    offset = new Vec3(need.X - q.SimPos.X, 0, need.Z - q.SimPos.Z);
                    float window = Math.Min(ApproachWindow, Math.Max(0f, start - q.Now));
                    float steps, stepLen;
                    if (q.Speed > 0.5f)
                    {
                        float stepsPerSec = 2f * LocomotionBlend.PhaseRate(lib, q.Speed, 0f);
                        stepLen = q.Speed / Math.Max(stepsPerSec, 1e-3f);
                        steps = Math.Max(1f, Math.Min(3f, window * stepsPerSec));
                    }
                    else
                    {
                        stepLen = StandStep;
                        steps = Math.Max(1f, Math.Min(3f, window * 2f));
                    }
                    float cap = MaxStrideChange * steps * stepLen;
                    float mag = offset.Length();
                    fits = mag <= cap + 1e-6f;
                    float along = Vec3.Dot(offset, fwd) / (steps * stepLen);
                    stride = 1f + Math.Max(-MaxStrideChange, Math.Min(MaxStrideChange, along));
                }
                float score = (rate - 1f) * 4f + offset.Length() + StylePenalty(q.KickStyle, c.KickStyle);
                if (c.HasContact)
                {
                    score += Outside(q.Ball.Y, c.BallHeightMin, c.BallHeightMax) * 5f;
                    score += Outside(ballSpeed, c.BallSpeedMin, c.BallSpeedMax) * 0.05f;
                    score += Outside(approach, c.ApproachMin, c.ApproachMax) * 0.01f;
                }
                var choice = new ActionChoice(c.Index, start, rate, q.ContactTime > start ? q.ContactTime : start + lead / rate,
                    start + c.Length / rate, stride, offset, !fits);
                bool ok = fits && rate <= MaxCompress;
                if (ok && score < bestScore - 1e-6f) { best = n; bestScore = score; bestC = choice; }
                // 대체 경로: 원터치는 최단 리드 우선, 그 외 점수
                float anyScore = rate > MaxCompress ? 1000f + lead : score;
                if (anyScore < bestAnyScore - 1e-6f) { bestAny = n; bestAnyScore = anyScore; bestAnyC = choice; }
            }
            if (best >= 0) return bestC;
            if (bestAny >= 0) return bestAnyC;
            // 부위가 맞는 후보가 없으면 부위 제한 없이 다시
            return Select(lib, candidates, new ActionRequest(q.Now, q.ContactTime, q.SimPos, q.Yaw, q.Ball, q.BallVelOut, q.Speed, BodyPart.None, q.KickStyle, q.Scale));
        }

        static float Outside(float v, float lo, float hi) => v < lo ? lo - v : v > hi ? v - hi : 0f;

        /// <summary>055 FR-61: 같은 상황 후보 안에서 선수 원형과 같은 원형 클립 0, 원형 없는 클립 MismatchNeutral, 다른 원형 클립 MismatchOther.</summary>
        public const float MismatchNeutral = 0.3f, MismatchOther = 1.0f;

        static float StylePenalty(KickArchetype want, KickArchetype clip)
        {
            if (want == KickArchetype.None) return 0f;
            if (clip == want) return 0f;
            return clip == KickArchetype.None ? MismatchNeutral : MismatchOther;
        }
    }

    /// <summary>
    /// 선수 한 명의 액션 목록 (specs/043 FR-60). 시작 시각·시뮬 순서(seq) 순.
    /// 일반 액션은 앞 액션의 접촉 프레임이 지나기 전에 시작하지 않는다. 피격(넘어짐 등)은 즉시 — 앞 액션은 피격 시각에서 끝난다.
    /// </summary>
    public sealed class ActionTimeline
    {
        readonly struct Entry
        {
            public readonly ActionChoice A;
            public readonly bool Hit;
            public readonly long Seq;

            public Entry(ActionChoice a, bool hit, long seq)
            {
                A = a;
                Hit = hit;
                Seq = seq;
            }
        }

        readonly List<Entry> _e = new List<Entry>();

        public int Count => _e.Count;
        public ActionChoice Get(int i) => _e[i].A;
        public bool IsHit(int i) => _e[i].Hit;
        public long SeqOf(int i) => _e[i].Seq;
        public void Clear() => _e.Clear();

        /// <summary>t에 시작하려는 액션이 실제로 시작할 수 있는 가장 이른 시각.</summary>
        public float EarliestStart(float t, bool hit)
        {
            if (hit) return t;
            float s = t;
            bool moved = true;
            while (moved)
            {
                moved = false;
                for (int i = 0; i < _e.Count; i++)
                {
                    var a = _e[i].A;
                    if (a.Start <= s && s < a.ContactAt) { s = a.ContactAt; moved = true; }
                }
            }
            return s;
        }

        /// <summary>시각 t 이전(이하)의 마지막 접촉 시각. 없으면 음의 무한대.</summary>
        public float LastContactBefore(float t)
        {
            float best = float.NegativeInfinity;
            for (int i = 0; i < _e.Count; i++)
            {
                float c = _e[i].A.ContactAt;
                if (!_e[i].Hit && c <= t && c > best) best = c;
            }
            return best;
        }

        /// <summary>새 접촉이 앞 액션이 끝나기 전에 오면 앞 액션을 접촉 전에 끝낸다 (specs/054 FR-82: 끝내지 않고 다음 접촉으로 잇는다).</summary>
        public void EndBefore(float newStart, float newContact)
        {
            for (int i = 0; i < _e.Count; i++)
            {
                var e = _e[i];
                if (e.A.End <= newStart || e.A.Start >= newStart) continue;
                float end = Math.Max(e.A.ContactAt + 1e-3f, Math.Min(newStart + AnimPlan.ActionFadeOut, newContact));
                if (end < e.A.End) _e[i] = new Entry(e.A.WithEnd(end), e.Hit, e.Seq);
            }
        }

        public void Add(ActionChoice a, bool hit, long seq)
        {
            if (hit)
                for (int i = 0; i < _e.Count; i++)
                {
                    var e = _e[i];
                    if (e.A.Start <= a.Start && a.Start < e.A.End) _e[i] = new Entry(e.A.WithEnd(a.Start), e.Hit, e.Seq);
                }
            int at = _e.Count;
            for (int i = 0; i < _e.Count; i++)
            {
                var e = _e[i];
                if (a.Start < e.A.Start || (a.Start == e.A.Start && seq < e.Seq)) { at = i; break; }
            }
            _e.Insert(at, new Entry(a, hit, seq));
        }

        /// <summary>t에 재생 중인 액션 인덱스(가장 늦게 시작한 것), 없으면 −1.</summary>
        public int ActiveAt(float t)
        {
            int r = -1;
            for (int i = 0; i < _e.Count; i++)
            {
                var a = _e[i].A;
                if (a.Start <= t && t < a.End) r = i;
                if (a.Start > t) break;
            }
            return r;
        }
    }
}
