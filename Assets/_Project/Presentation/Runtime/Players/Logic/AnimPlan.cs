using System;
using System.Collections.Generic;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>한 엔티티의 한 시각 포즈 지시: 클립·클립 시각·가중치 목록 + 화면 루트 위치·정면 각. 할당 없는 값 타입.</summary>
    public struct PoseSample
    {
        public const int Capacity = 7;
        public int Count;
        int _c0, _c1, _c2, _c3, _c4, _c5, _c6;
        float _t0, _t1, _t2, _t3, _t4, _t5, _t6;
        float _w0, _w1, _w2, _w3, _w4, _w5, _w6;
        /// <summary>화면 루트(바닥) 위치 = 시뮬 위치 + 루트 보정.</summary>
        public Vec3 Root;
        public Vec3 RootOffset;
        public float Yaw;
        public bool Present;

        public int Clip(int i) => i switch { 0 => _c0, 1 => _c1, 2 => _c2, 3 => _c3, 4 => _c4, 5 => _c5, _ => _c6 };
        public float Time(int i) => i switch { 0 => _t0, 1 => _t1, 2 => _t2, 3 => _t3, 4 => _t4, 5 => _t5, _ => _t6 };
        public float Weight(int i) => i switch { 0 => _w0, 1 => _w1, 2 => _w2, 3 => _w3, 4 => _w4, 5 => _w5, _ => _w6 };

        /// <summary>슬롯을 넘친 가중치(버려진 몫). 0이 아니면 Normalize가 나머지를 키워 합 1을 지킨다.</summary>
        public float Dropped;

        internal void Add(int c, float t, float w)
        {
            if (w <= 1e-5f) return;
            if (Count >= Capacity) { Dropped += w; return; }
            switch (Count++)
            {
                case 0: _c0 = c; _t0 = t; _w0 = w; break;
                case 1: _c1 = c; _t1 = t; _w1 = w; break;
                case 2: _c2 = c; _t2 = t; _w2 = w; break;
                case 3: _c3 = c; _t3 = t; _w3 = w; break;
                case 4: _c4 = c; _t4 = t; _w4 = w; break;
                case 5: _c5 = c; _t5 = t; _w5 = w; break;
                default: _c6 = c; _t6 = t; _w6 = w; break;
            }
        }

        internal void Normalize()
        {
            if (Dropped <= 0 || Count == 0) return;
            float sum = 0;
            for (int i = 0; i < Count; i++) sum += Weight(i);
            float k = 1f / sum;
            _w0 *= k; _w1 *= k; _w2 *= k; _w3 *= k; _w4 *= k; _w5 *= k; _w6 *= k;
        }

        /// <summary>결정론 비교용 해시 (AC-12).</summary>
        public ulong Hash()
        {
            var h = Core.Hashing.StableHasher.Create();
            h.Add(Count);
            for (int i = 0; i < Count; i++) { h.Add(Clip(i)); h.Add(Time(i)); h.Add(Weight(i)); }
            h.Add(Root.X); h.Add(Root.Z); h.Add(Yaw);
            return h.Value;
        }
    }

    /// <summary>
    /// 화면 루트 보정 (specs/043 FR-46, AC-10). 액션마다 필요한 루트 이동(ActionChoice.RootOffset)을
    /// 접근 구간(시작 − ApproachWindow → 접촉)에 걸쳐 0 → 1로 들이고, 접촉 → 끝 + Release에 걸쳐 1 → 0으로 뺀다(smoothstep).
    /// 합친 보정은 크기 MaxOffset(0.3 m, 054 FR-02 최대)을 넘지 않는다 — 넘는 몫은 버리고 진단 플래그.
    /// </summary>
    public static class RootCorrection
    {
        public const float MaxOffset = 0.3f;
        public const float Release = 0.25f;

        static float Smooth(float x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

        public static float Ramp(in ActionChoice a, float t)
        {
            float a0 = a.Start - ActionClipSelector.ApproachWindow;
            if (t <= a0 || t >= a.End + Release) return 0f;
            if (t < a.ContactAt) return Smooth((t - a0) / Math.Max(1e-4f, a.ContactAt - a0));
            return 1f - Smooth((t - a.ContactAt) / Math.Max(1e-4f, a.End + Release - a.ContactAt));
        }

        public static Vec3 Clamp(Vec3 o)
        {
            float m = o.Length();
            return m > MaxOffset ? o * (MaxOffset / m) : o;
        }
    }

    /// <summary>
    /// 화면 공 보정 (specs/043 FR-44): 접촉 직전 BallPullTime 동안 화면상의 공을 클립 접촉점 쪽으로 최대 BallPullMax만큼 당긴다.
    /// 접촉 순간 이후에는 0 — 화면 공 = 시뮬 공. 시뮬 공 위치는 바꾸지 않는다.
    /// </summary>
    public static class BallVisualCorrection
    {
        public const float BallPullTime = 0.15f;
        /// <summary>054 FR-35 [제안] 15 cm.</summary>
        public const float BallPullMax = 0.15f;

        public static Vec3 Offset(Vec3 residual, float contactAt, float t)
        {
            if (t >= contactAt || t <= contactAt - BallPullTime) return Vec3.Zero;
            float w = (t - (contactAt - BallPullTime)) / BallPullTime;
            w = w * w * (3 - 2 * w);
            float m = residual.Length();
            var r = m > BallPullMax ? residual * (BallPullMax / m) : residual;
            return r * w;
        }
    }

    /// <summary>
    /// 하이라이트 애니메이션 계획 (specs/043 FR-40~62, AC-12). Build가 시뮬 기록에서 엔티티별 로코모션 위상(30 Hz 프레임 경계)과
    /// 액션 목록을 미리 정하고, Evaluate(t)는 재생 시각 t만으로 포즈 지시를 계산한다 — 렌더 fps·배속과 무관.
    /// </summary>
    public sealed class AnimPlan
    {
        public const float ActionFadeIn = 0.12f, HitFadeIn = 0.05f, ActionFadeOut = 0.18f;
        /// <summary>이벤트 시각보다 이만큼 이전부터 액션을 준비할 수 있다.</summary>
        public const float Lookahead = 3f;
        /// <summary>골 후 세리머니 시작 지연.</summary>
        public const float CelebrateDelay = 0.6f;

        readonly ClipLibrary _lib;
        readonly HighlightData _d;
        readonly float[] _speed, _dir, _phase;     // [frame * E + e]
        readonly ActionTimeline[] _tl;
        readonly LocoStyle[] _style;            // 엔티티별 정면 원형 로코모션 (055)
        readonly KickArchetype[] _kick;         // 엔티티별 킥 원형 (055)
        readonly List<(float contactAt, Vec3 pull)> _ballPulls = new List<(float, Vec3)>();

        public ClipLibrary Library => _lib;
        public HighlightData Data => _d;
        public ActionTimeline Timeline(int e) => _tl[e];

        AnimPlan(ClipLibrary lib, HighlightData d)
        {
            _lib = lib;
            _d = d;
            int n = d.Frames * HighlightData.Entities;
            _speed = new float[n];
            _dir = new float[n];
            _phase = new float[n];
            _tl = new ActionTimeline[HighlightData.Entities];
            for (int e = 0; e < _tl.Length; e++) _tl[e] = new ActionTimeline();
            _style = new LocoStyle[HighlightData.Entities];
            _kick = new KickArchetype[HighlightData.Entities];
        }

        public static AnimPlan Build(ClipLibrary lib, AnimationMap map, HighlightData d, ILogger log) => Build(lib, map, d, log, null);

        /// <summary>styles[e] = 엔티티 e의 달리기·킥 원형 (055 FR-51). null이거나 None이면 공유 클립·원형 점수 없음.</summary>
        public static AnimPlan Build(ClipLibrary lib, AnimationMap map, HighlightData d, ILogger log, RunStyleChoice[] styles)
        {
            var p = new AnimPlan(lib, d);
            if (styles != null)
                for (int e = 0; e < HighlightData.Entities && e < styles.Length; e++)
                {
                    p._style[e] = lib.Style(styles[e]);
                    p._kick[e] = styles[e].Kick;
                }
            p.BuildLocomotion(log);
            p.BuildActions(map, log);
            return p;
        }

        void BuildLocomotion(ILogger log)
        {
            const int E = HighlightData.Entities;
            float dt = 1f / HighlightData.FrameRate;
            for (int e = 0; e < E; e++)
            {
                float prev = 0f, phase = 0f;
                for (int f = 0; f < _d.Frames; f++)
                {
                    int f0 = Math.Max(0, f - 1), f1 = Math.Min(_d.Frames - 1, f + 1);
                    int i = f * E + e;
                    // 이웃 프레임이 기록 없음이면 현재 프레임으로 대체(교체 투입 순간 속도 폭주 방지)
                    if (!_d.Present[f0 * E + e]) f0 = f;
                    if (!_d.Present[f1 * E + e]) f1 = f;
                    int i0 = f0 * E + e, i1 = f1 * E + e;
                    float span = Math.Max(1, f1 - f0) * dt;
                    float vx = f1 == f0 ? 0f : (_d.X[i1] - _d.X[i0]) / span, vz = f1 == f0 ? 0f : (_d.Z[i1] - _d.Z[i0]) / span;
                    float v = (float)Math.Sqrt(vx * vx + vz * vz);
                    if (!_d.Present[i]) v = 0;
                    v = f == 0 ? v : LocomotionBlend.LimitSpeed(prev, v, dt, log);
                    prev = v;
                    float dir = 0f;
                    if (v > 0.05f)
                    {
                        dir = ((float)Math.Atan2(vx, vz) - _d.Yaw[i]) * 180f / (float)Math.PI;
                        dir = ((dir % 360f) + 360f) % 360f;
                    }
                    _speed[i] = v;
                    _dir[i] = dir;
                    _phase[i] = phase;
                    phase += LocomotionBlend.PhaseRate(_lib, v, dir, _style[e]) * dt;
                    phase -= (float)Math.Floor(phase);
                }
            }
        }

        int EntityFor(in AnimEvent ev, MapWho who)
        {
            switch (who)
            {
                case MapWho.Other: return ev.Other;
                case MapWho.Referee: return HighlightData.Players;
                default: return ev.Entity;
            }
        }

        void BuildActions(AnimationMap map, ILogger log)
        {
            var evs = new List<AnimEvent>(_d.Events);
            evs.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Seq.CompareTo(b.Seq));
            foreach (var ev in evs)
            {
                if (map.IsNone(ev.Kind)) continue;
                var cands = map.Has(ev.Kind) ? map.Candidates(ev.Kind, ev.Outcome) : map.Resolve(ev.Kind, log);
                if (cands.Count == 0) continue;
                var who = map.Who(ev.Kind);
                int e = EntityFor(ev, who);
                if (e < 0 || e >= HighlightData.Entities) continue;
                bool hit = ev.Kind == SimEventKind.Foul || ev.Kind == SimEventKind.Injury;
                float t = ev.Time + (ev.Kind == SimEventKind.Goal ? CelebrateDelay : 0f);
                if (t > _d.EndTime) continue;
                var tl = _tl[e];
                ActionChoice choice;
                if (hit)
                {
                    var victim = _d.Pos(e, t);
                    int attacker = ev.Entity == e ? ev.Other : ev.Entity;
                    var push = attacker >= 0 && attacker != e ? victim - _d.Pos(attacker, t) : new Vec3(0, 0, 1);
                    int clip = HitDirection.FallClip(_lib, push, _d.YawAt(e, t));
                    var c = _lib.Get(clip);
                    choice = new ActionChoice(clip, t, 1f, t, t + c.Length, 1f, Vec3.Zero, false);
                }
                else
                {
                    float now = tl.EarliestStart(Math.Max(_d.StartTime, ev.Time - Lookahead), false);
                    float contact = Math.Max(t, now);
                    var q = new ActionRequest(now, contact, _d.Pos(e, contact), _d.YawAt(e, contact), ev.BallPos, ev.BallVelOut,
                        SpeedAt(e, contact - 0.3f), ev.Part, _kick[e]);
                    choice = ActionClipSelector.Select(_lib, cands, q);
                    if (_lib.Get(choice.Clip).HasContact)
                    {
                        var clamped = RootCorrection.Clamp(choice.RootOffset);
                        var residual = choice.RootOffset - clamped;
                        if (residual.LengthSq() > 1e-8f) _ballPulls.Add((choice.ContactAt, residual * -1f));
                    }
                }
                tl.Add(choice, hit, ev.Seq);
            }
        }

        public float SpeedAt(int e, float t)
        {
            _d.FrameAt(t, out var f, out var a);
            int i0 = f * HighlightData.Entities + e, i1 = i0 + HighlightData.Entities;
            return _speed[i0] + (_speed[i1] - _speed[i0]) * a;
        }

        static float Smooth(float x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

        float ActionWeight(in ActionChoice a, bool hit, float t)
        {
            if (t < a.Start || t >= a.End) return 0f;
            float fin = hit ? HitFadeIn : ActionFadeIn;
            return Smooth((t - a.Start) / fin) * Smooth((a.End - t) / ActionFadeOut);
        }

        /// <summary>재생 시각 t(시뮬 초)의 엔티티 e 포즈 지시. 같은 t → 같은 결과 (AC-12).</summary>
        public void Evaluate(int e, float t, ref PoseSample s)
        {
            s = default;
            const int E = HighlightData.Entities;
            _d.FrameAt(t, out var f, out var a);
            int i0 = f * E + e, i1 = i0 + E;
            s.Present = a < 0.5f ? _d.Present[i0] : _d.Present[i1];
            var simPos = _d.Pos(e, t);
            s.Yaw = _d.YawAt(e, t);
            // 액션 (최대 2개: 이전 액션이 끝나며 빠지는 중일 수 있다)
            var tl = _tl[e];
            float actW = 0f;
            Vec3 off = Vec3.Zero;
            for (int k = 0; k < tl.Count; k++)
            {
                var ac = tl.Get(k);
                off += ac.RootOffset * RootCorrection.Ramp(ac, t);
                float w = ActionWeight(ac, tl.IsHit(k), t);
                if (w <= 0) continue;
                w = Math.Min(w, 1f - actW);
                var c = _lib.Get(ac.Clip);
                float ct = Math.Min(c.Length, Math.Max(0f, (t - ac.Start) * ac.Rate));
                s.Add(ac.Clip, ct, w);
                actW += w;
            }
            s.RootOffset = RootCorrection.Clamp(off);
            s.Root = simPos + s.RootOffset;
            // 로코모션 (나머지 가중치)
            float locoW = 1f - actW;
            if (locoW > 1e-5f)
            {
                float v = _speed[i0] + (_speed[i1] - _speed[i0]) * a;
                float d0 = _dir[i0], d1 = _dir[i1];
                float dd = (float)Math.IEEERemainder(d1 - d0, 360.0);
                float dir = d0 + dd * a;
                dir -= 360f * (float)Math.Floor(dir / 360f); // 0°/359.99° 사이 보간이 음수가 되면 엉뚱한 방향 클립을 고른다 (056 T14a)
                float ph0 = _phase[i0], ph1 = _phase[i1];
                float dp = ph1 - ph0;
                if (dp < 0) dp += 1f;
                float ph = ph0 + dp * a;
                var ls = LocomotionBlend.Evaluate(_lib, v, dir, ph, t, _style[e]);
                for (int i = 0; i < ls.Count; i++) s.Add(ls.Clip(i), ls.Time(i), ls.Weight(i) * locoW);
            }
            s.Normalize();
        }

        /// <summary>화면 공 위치 = 시뮬 공 + 접촉 직전 보정 (FR-44).</summary>
        public Vec3 BallVisual(float t)
        {
            var b = _d.BallAt(t);
            for (int i = 0; i < _ballPulls.Count; i++)
                b += BallVisualCorrection.Offset(_ballPulls[i].pull, _ballPulls[i].contactAt, t);
            return b;
        }
    }
}
