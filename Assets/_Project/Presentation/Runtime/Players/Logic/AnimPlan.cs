using System;
using System.Collections.Generic;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic.Motion;

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
        /// <summary>자세 오버레이 (054 FR-20~22, 라디안). roll + = 오른쪽, pitch + = 앞.</summary>
        public float Roll, Pitch;
        /// <summary>회피 어깨 돌림 (054 FR-45, 라디안).</summary>
        public float Twist;
        public bool Present;

        public int Clip(int i) => i switch { 0 => _c0, 1 => _c1, 2 => _c2, 3 => _c3, 4 => _c4, 5 => _c5, _ => _c6 };
        public float Time(int i) => i switch { 0 => _t0, 1 => _t1, 2 => _t2, 3 => _t3, 4 => _t4, 5 => _t5, _ => _t6 };
        public float Weight(int i) => i switch { 0 => _w0, 1 => _w1, 2 => _w2, 3 => _w3, 4 => _w4, 5 => _w5, _ => _w6 };

        /// <summary>슬롯을 넘친 가중치(버려진 몫). 0이 아니면 Normalize가 나머지를 키워 합 1을 지킨다.</summary>
        public float Dropped;

        /// <summary>같은 클립은 합친다: 믹서에 클립당 입력이 하나라 따로 두면 앞 항목이 사라진다. 시각은 가중치가 큰 쪽 (054 FR-82).</summary>
        internal void Add(int c, float t, float w)
        {
            if (w <= 1e-5f) return;
            for (int i = 0; i < Count; i++)
            {
                if (Clip(i) != c) continue;
                float old = Weight(i);
                Set(i, c, w > old ? t : Time(i), old + w);
                return;
            }
            if (Count >= Capacity) { Dropped += w; return; }
            Set(Count++, c, t, w);
        }

        void Set(int i, int c, float t, float w)
        {
            switch (i)
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
            h.Add(Root.X); h.Add(Root.Z); h.Add(Yaw); h.Add(Roll); h.Add(Pitch); h.Add(Twist);
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
        /// <summary>보정이 들어오는 최소 시간(s). 크기 0.3 m면 smoothstep 속도 변화 ≤ 0.5 m/s/프레임 (054 FR-03).</summary>
        public const float MinRamp = 0.25f;

        static float Smooth(float x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

        public static float Ramp(in ActionChoice a, float t)
        {
            float a0 = Math.Min(a.Start - ActionClipSelector.ApproachWindow, a.ContactAt - MinRamp);
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
        MotionTrack _track;
        YawTrack _yaw;
        PostureTrack _posture;
        FootPlantPlan _plants;
        readonly TransitionClip[][] _trans = new TransitionClip[HighlightData.Entities][];
        float[] _scale;
        InteractionPlanner _interact;
        FaultFollower _fault;
        ContactKeys _keys;
        int[] _dribble;     // dribble_run_30·50·65 클립
        /// <summary>골대 기둥 (월드 X, Z). 표준 피치 105 m, 골대 폭 7.32 m (044 FR-03·004 GoalFrame과 같은 치수).</summary>
        public static readonly float[] GoalPosts = { -52.5f, -3.66f, -52.5f, 3.66f, 52.5f, -3.66f, 52.5f, 3.66f };
        /// <summary>드리블 런 속도 범위 (플랜 [제안] 2.5~7 m/s).</summary>
        public const float DribbleMin = 2.5f, DribbleMax = 7f;

        public ClipLibrary Library => _lib;
        public HighlightData Data => _d;
        public ActionTimeline Timeline(int e) => _tl[e];
        public MotionTrack Track => _track;
        public ContactKeys Keys => _keys;
        public bool HasSimFault(int e) => _fault.Active(e);
        public FootPlantPlan Plants => _plants;
        public IReadOnlyList<TransitionClip> Transitions(int e) => _trans[e];

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
        public static AnimPlan Build(ClipLibrary lib, AnimationMap map, HighlightData d, ILogger log, RunStyleChoice[] styles) =>
            Build(lib, map, d, log, styles, null);

        /// <param name="entityScale">엔티티별 키 배율(기준 1.80 m = 1). null이면 전부 1 (054 FR-87).</param>
        public static AnimPlan Build(ClipLibrary lib, AnimationMap map, HighlightData d, ILogger log, RunStyleChoice[] styles, float[] entityScale)
        {
            var p = new AnimPlan(lib, d);
            p._dribble = new[] { lib.IndexOf("dribble_run_30"), lib.IndexOf("dribble_run_50"), lib.IndexOf("dribble_run_65") };
            if (styles != null)
                for (int e = 0; e < HighlightData.Entities && e < styles.Length; e++)
                {
                    p._style[e] = lib.Style(styles[e]);
                    p._kick[e] = styles[e].Kick;
                }
            p._scale = entityScale;
            p.BuildLocomotion(log);
            p.BuildActions(map, log);
            p._keys = new ContactKeys(d);
            p._track = new MotionTrack(d);
            p._fault = new FaultFollower(d, p._track);
            p._interact = new InteractionPlanner(d, GoalPosts);
            p._yaw = new YawTrack(d);
            p._posture = new PostureTrack(d, p._track);
            for (int e = 0; e < HighlightData.Entities; e++)
                p._trans[e] = TransitionPlanner.Plan(lib, d, p._track, e, p._tl[e]).ToArray();
            p._plants = new FootPlantPlan(p, d, entityScale);
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
                    v = f == 0 ? v : LocomotionBlend.LimitSpeed(prev, v, dt, log, Highlight.Logic.JumpClassifier.Label(Highlight.Logic.JumpClassifier.Classify(_d, f, e)));
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
                    phase += (IsDribbling(f, e, v) ? DribbleRate(v) : LocomotionBlend.PhaseRate(_lib, v, dir, _style[e])) * dt;
                    phase -= (float)Math.Floor(phase);
                }
            }
        }

        bool IsDribbling(int f, int e, float v) => _d.BallOwner[f] == e && v >= DribbleMin && v <= DribbleMax && _dribble[0] >= 0;

        /// <summary>드리블 런 두 클립과 비율 (속도 사이 보간).</summary>
        void DribblePair(float v, out int a, out int b, out float w)
        {
            float v0 = _lib.Get(_dribble[0]).Speed, v1 = _lib.Get(_dribble[1]).Speed, v2 = _lib.Get(_dribble[2]).Speed;
            if (v <= v1)
            {
                a = _dribble[0];
                b = _dribble[1];
                w = v <= v0 ? 0f : (v - v0) / (v1 - v0);
            }
            else
            {
                a = _dribble[1];
                b = _dribble[2];
                w = v >= v2 ? 1f : (v - v1) / (v2 - v1);
            }
        }

        float DribbleRate(float v)
        {
            DribblePair(v, out var a, out var b, out var w);
            var ca = _lib.Get(a);
            var cb = _lib.Get(b);
            float stride = ca.Speed * ca.Length + (cb.Speed * cb.Length - ca.Speed * ca.Length) * w;
            return stride > 0f ? v / stride : 0f;
        }

        float ScaleOf(int e) => _scale != null && e < _scale.Length && _scale[e] > 0f ? _scale[e] : 1f;

        int EntityFor(in AnimEvent ev, MapWho who)
        {
            switch (who)
            {
                case MapWho.Other: return ev.Other;
                case MapWho.Referee: return HighlightData.Players;
                default: return ev.Entity;
            }
        }

        readonly List<int> _gateBuf = new List<int>();
        /// <summary>태클 동작 허용 거리 (058 FR-50, highlight.tackleMaxDistance).</summary>
        public const float TackleMaxDistance = TackleGate.DefaultMaxDistance;

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
                if (TackleGate.Applies(ev.Kind)) // 058 FR-50·51: 상대가 가까울 때만 태클 동작
                {
                    TackleGate.Filter(_lib, cands, _d, ev, e, TackleMaxDistance, _gateBuf, log);
                    if (_gateBuf.Count == 0) continue;
                    cands = _gateBuf;
                }
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
                    now = Math.Max(now, tl.LastContactBefore(t));   // 앞 접촉이 끝나기 전에 시작하지 않는다 (043 FR-60, 054 FR-82)
                    float contact = Math.Max(t, now);
                    var q = new ActionRequest(now, contact, _d.Pos(e, contact), _d.YawAt(e, contact), ev.BallPos, ev.BallVelOut,
                        SpeedAt(e, contact - 0.3f), ev.Part, _kick[e], ScaleOf(e));
                    choice = ActionClipSelector.Select(_lib, cands, q);
                    if (MissesBall(ev)) choice = choice.WithRootOffset(Vec3.Zero);    // 닿지 않는 동작: 공에 맞추지 않는다 (054 FR-36·44·83)
                    else if (_lib.Get(choice.Clip).HasContact)
                    {
                        // 접촉 순간 루트 오차 ≤ 10 cm (054 FR-02). 남는 차이는 화면 공 보정(≤ 15 cm, FR-35)이 맡는다
                        var offset = choice.RootOffset;
                        float m = offset.Length();
                        var clamped = m > MotionDefaults.RootContactMax ? offset * (MotionDefaults.RootContactMax / m) : offset;
                        var residual = offset - clamped;
                        // 높이 차도 화면 공이 맡는다 (시뮬 공 높이는 선수 키를 모른다: 002·008 전까지)
                        var pull = residual * -1f + new Vec3(0, _lib.Get(choice.Clip).ContactPoint.Y * ScaleOf(e) - ev.BallPos.Y, 0);
                        if (pull.LengthSq() > 1e-8f) _ballPulls.Add((choice.ContactAt, pull));
                        choice = choice.WithRootOffset(clamped);
                    }
                    tl.EndBefore(choice.Start, choice.ContactAt);
                }
                tl.Add(choice, hit, ev.Seq);
                if (ev.Kind == SimEventKind.Clearance && ev.Part == BodyPart.Head && ev.Other >= 0 && ev.Other < HighlightData.Entities)
                    AddHeaderLoser(ev, map);
            }
        }

        /// <summary>
        /// 시뮬 판정이 공에 닿지 않음인가 (054 FR-36·44·83). 실패한 태클·선방·경합은 닿지 않지만,
        /// 실패한 트래핑·빗맞은 슈팅·패스는 공에 닿은 뒤 결과가 나쁜 것이라 접촉한다.
        /// </summary>
        public static bool MissesBall(in AnimEvent ev)
        {
            if (ev.Outcome != ActionOutcome.Fail) return false;
            switch (ev.Kind)
            {
                case SimEventKind.Tackle:
                case SimEventKind.Save:
                case SimEventKind.Catch:
                case SimEventKind.Punch:
                case SimEventKind.Parry:
                case SimEventKind.DiveStart: // 011: 접촉 없는 다이빙(실점)
                case SimEventKind.Block:
                case SimEventKind.Interception:
                case SimEventKind.Clearance:
                case SimEventKind.Duel:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>헤딩 경합 패자 (054 FR-43): 같이 뛰지만 정점이 늦어 공에 닿지 않는다. 지연 [제안] 0.07 s.</summary>
        const float LoserDelay = 0.07f;

        void AddHeaderLoser(in AnimEvent ev, AnimationMap map)
        {
            int clip = _lib.IndexOf("header_jump");
            if (clip < 0) return;
            var c = _lib.Get(clip);
            float contact = ev.Time + LoserDelay;
            float start = Math.Max(_d.StartTime, contact - c.ContactTime);
            float rate = contact > start ? c.ContactTime / (contact - start) : 1f;
            _tl[ev.Other].Add(new ActionChoice(clip, start, rate, contact, start + c.Length / rate, 1f, Vec3.Zero, false), false, ev.Seq);
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
            // 접촉이 끝나기 전에 빠지지 않는다: 다음 접촉이 바로 오면(FR-82) 접촉 뒤 남은 시간 안에 빠진다
            float fout = hit ? ActionFadeOut : Math.Min(ActionFadeOut, Math.Max(1e-3f, a.End - a.ContactAt));
            return Smooth((t - a.Start) / fin) * Smooth((a.End - t) / fout);
        }

        /// <summary>재생 시각 t(시뮬 초)의 엔티티 e 포즈 지시. 같은 t → 같은 결과 (AC-12).</summary>
        public void Evaluate(int e, float t, ref PoseSample s)
        {
            EvaluateBase(e, t, ref s);
        }

        /// <summary>시뮬 공 + 접촉 키 (054 FR-52).</summary>
        public Vec3 BallKeyed(float t) => _keys.Ball(t);

        /// <summary>전환·액션·로코모션 합성 + 루트·방향·자세. 발 디딤 계획의 입력이기도 하다.</summary>
        internal void EvaluateBase(int e, float t, ref PoseSample s)
        {
            s = default;
            const int E = HighlightData.Entities;
            _d.FrameAt(t, out var f, out var a);
            int i0 = f * E + e, i1 = i0 + E;
            s.Present = a < 0.5f ? _d.Present[i0] : _d.Present[i1];
            var simPos = _d.Pos(e, t);
            s.Yaw = _yaw.At(e, t);
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
            _interact.At(e, t, out var ix, out var iz, out var twist);
            off += new Vec3(ix, 0, iz);
            s.RootOffset = RootCorrection.Clamp(off);
            s.Twist = twist * (1f - actW);
            if (_fault.Active(e))
            {
                _fault.At(e, t, out var fx, out var fz);
                s.RootOffset += new Vec3(fx, 0, fz);
            }
            s.Root = simPos + s.RootOffset;
            // 전환 동작 (액션이 쓰고 남은 몫)
            var trans = _trans[e];
            float transW = 0f;
            for (int k = 0; k < trans.Length; k++)
            {
                ref readonly var tc = ref trans[k];
                if (t < tc.Start) break;
                if (t >= tc.End) continue;
                float w = Smooth((t - tc.Start) / TransitionPlanner.FadeIn) * Smooth((tc.End - t) / TransitionPlanner.FadeOut);
                w = Math.Min(w, 1f - actW - transW);
                if (w <= 0f) continue;
                var c = _lib.Get(tc.Clip);
                s.Add(tc.Clip, Math.Min(c.Length, (t - tc.Start) * tc.Rate), w);
                transW += w;
            }
            // 자세 오버레이: 액션 중에는 줄이고, 자세를 이미 가진 전환 클립 동안 숙임·젖힘은 뺀다
            _posture.At(e, t, out var roll, out var pitch);
            s.Roll = roll * (1f - actW);
            s.Pitch = pitch * (1f - actW - transW);
            // 로코모션 (나머지 가중치)
            float locoW = 1f - actW - transW;
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
                if (IsDribbling(a < 0.5f ? f : f + 1, e, v) && Math.Abs(((dir + 180f) % 360f) - 180f) < 30f)
                {
                    DribblePair(v, out var da, out var db, out var dw);
                    s.Add(da, ph * _lib.Get(da).Length, (1f - dw) * locoW);
                    s.Add(db, ph * _lib.Get(db).Length, dw * locoW);
                }
                else
                {
                    var ls = LocomotionBlend.Evaluate(_lib, v, dir, ph, t, _style[e]);
                    for (int i = 0; i < ls.Count; i++) s.Add(ls.Clip(i), ls.Time(i), ls.Weight(i) * locoW);
                }
            }
            s.Normalize();
        }

        /// <summary>화면 공 위치 = 시뮬 공 + 접촉 직전 보정 (FR-44).</summary>
        public Vec3 BallVisual(float t)
        {
            var b = _keys.Ball(t);
            for (int i = 0; i < _ballPulls.Count; i++)
                b += BallVisualCorrection.Offset(_ballPulls[i].pull, _ballPulls[i].contactAt, t);
            return b;
        }
    }
}
