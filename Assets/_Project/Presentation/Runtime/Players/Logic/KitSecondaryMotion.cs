using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 2차 움직임 입력: 선수 몸 틀(선수 = Unity 좌표: x 옆, y 위, z 앞) 골반 가속도 m/s².
    /// <see cref="SegmentStart"/> 이전 시각은 커널 창에서 잘린다(트랙 시작·컷 직후 튐 방지, 062 plan 위험).
    /// </summary>
    public interface IKitAccelSource
    {
        float SegmentStart(float t);
        Vec3 At(float t);
    }

    /// <summary>30 Hz 가속도 샘플 트랙 (테스트·제자리 클립). 끝 뒤는 마지막 값 유지, 시작 전·컷 앞은 창에서 잘린다.</summary>
    public sealed class KitAccelTrack : IKitAccelSource
    {
        public const float Rate = 30f;
        readonly float _start;
        readonly Vec3[] _s;
        readonly float[] _cuts;

        public KitAccelTrack(float startTime, Vec3[] samples30Hz, float[] cuts = null)
        {
            if (samples30Hz == null || samples30Hz.Length == 0) throw new ArgumentException("샘플 1개 이상");
            _start = startTime;
            _s = samples30Hz;
            _cuts = cuts ?? Array.Empty<float>();
            Array.Sort(_cuts);
        }

        /// <summary>클립 골반 가속도 커브를 repeats번 이어 붙인 트랙. 루프 클립은 마지막(= 처음) 샘플을 겹치지 않게 잇는다.</summary>
        public static KitAccelTrack FromClip(ClipInfo clip, float startTime, int repeats)
        {
            if (clip?.PelvisAccel == null) throw new ArgumentException("pelvisAccel 없음: " + clip?.Id);
            var a = clip.PelvisAccel;
            int per = clip.Loop ? clip.Frames : a.Length;
            int n = per * Math.Max(1, repeats) + (clip.Loop ? 1 : 0);
            var s = new Vec3[n];
            for (int i = 0; i < n; i++) s[i] = a[clip.Loop ? i % clip.Frames : Math.Min(i % per, a.Length - 1)];
            return new KitAccelTrack(startTime, s);
        }

        public float SegmentStart(float t)
        {
            float s = _start;
            for (int i = 0; i < _cuts.Length && _cuts[i] <= t; i++) s = _cuts[i];
            return s;
        }

        public Vec3 At(float t)
        {
            float u = (t - _start) * Rate;
            if (u <= 0) return _s[0];
            if (u >= _s.Length - 1) return _s[_s.Length - 1];
            int f = (int)u;
            float k = u - f;
            return _s[f] + (_s[f + 1] - _s[f]) * k;
        }
    }

    /// <summary>
    /// 062 FR-21·22 키트 2차 움직임 (천 시뮬 없음). 보조 뼈 6개 오프셋 = 골반 가속도와 감쇠 진동 커널의 합성곱:
    /// offset(t) = −G · Σ_j k(τ_j)·a(t − τ_j)·Δ, τ_j = j/60 s, 창 0.5 s(끝에서 0으로 가늘어짐).
    /// 적분 상태가 없는 재생 시각의 순수 함수라 fps·배속·평가 순서와 무관하다(AC-14). 축·이동 상자·고관절 게이트는
    /// Blender 관통 검사(checks.kit_motion)와 같은 값(player.layout.json "kitBones")을 쓴다. 할당 없음.
    /// </summary>
    public sealed class KitSecondaryMotion
    {
        public const int Bones = 6;
        /// <summary>커널 창 (s) — 몸이 멈추면 이 시간 안에 가라앉는다 (FR-21 [제안] 0.5 s).</summary>
        public const float Window = 0.5f;
        /// <summary>진폭 범위 (FR-21 [제안] 0.5~3 cm).</summary>
        public const float MinPeak = 0.005f, MaxOffset = 0.03f;

        // 감쇠 진동 (천 밑단의 늘어진 몫): 고유 진동수·감쇠비·이득. 이득은 질주·방향 전환·점프·세리머니 클립 진폭으로 맞췄다 (T17).
        public const float Frequency = 2.6f, Damping = 0.35f, Gain = 0.8f;
        const int Taps = 31;                 // 0 … 0.5 s, 1/60 s 간격
        const float Step = Window / (Taps - 1);

        static readonly float[] Kernel = BuildKernel();

        public readonly struct Bone
        {
            public readonly string Name, Parent;
            public readonly Vec3 Ax0, Ax1, Ax2, Min, Max;
            /// <summary>고관절 게이트: 방향(+1 굴곡 / −1 신전, 0 = 없음)·시작 각°·0이 되는 각°.</summary>
            public readonly int GateSign;
            public readonly float Gate0, Gate1;

            public Bone(string name, string parent, Vec3 a0, Vec3 a1, Vec3 a2, Vec3 min, Vec3 max, int gateSign, float g0, float g1)
            {
                Name = name; Parent = parent; Ax0 = a0; Ax1 = a1; Ax2 = a2; Min = min; Max = max;
                GateSign = gateSign; Gate0 = g0; Gate1 = g1;
            }
        }

        readonly Bone[] _bones;

        KitSecondaryMotion(Bone[] bones) { _bones = bones; }

        public Bone Get(int i) => _bones[i];
        public string BoneName(int i) => _bones[i].Name;

        /// <summary>player.layout.json 전체 텍스트에서 "kitBones"를 읽는다.</summary>
        public static KitSecondaryMotion FromLayout(string layoutJson)
        {
            var root = (JsonObject)JsonReader.Parse(layoutJson);
            if (!root.TryGet("kitBones", out var kb) || !(kb is JsonObject ko) || !ko.TryGet("bones", out var arr))
                throw new FormatException("layout.json에 kitBones 없음 (062 T15 이전 생성물)");
            var list = (List<object>)arr;
            if (list.Count != Bones) throw new FormatException("kitBones 개수 " + list.Count);
            var bones = new Bone[Bones];
            for (int i = 0; i < Bones; i++)
            {
                var o = (JsonObject)list[i];
                o.TryGet("name", out var nm);
                o.TryGet("parent", out var pa);
                o.TryGet("axes", out var ax);
                var axes = (List<object>)ax;
                int gs = 0;
                float g0 = 0, g1 = 0;
                if (o.TryGet("hipGate", out var hg) && hg is List<object> gl)
                {
                    gs = (int)(double)gl[0]; g0 = (float)(double)gl[1]; g1 = (float)(double)gl[2];
                }
                bones[i] = new Bone((string)nm, (string)pa, V(axes[0]), V(axes[1]), V(axes[2]), V(Get(o, "min")), V(Get(o, "max")), gs, g0, g1);
            }
            return new KitSecondaryMotion(bones);
        }

        static object Get(JsonObject o, string k) => o.TryGet(k, out var v) ? v : throw new FormatException("kitBones." + k);

        static Vec3 V(object o)
        {
            var l = (List<object>)o;
            return new Vec3((float)(double)l[0], (float)(double)l[1], (float)(double)l[2]);
        }

        static float[] BuildKernel()
        {
            // 바닥 가속도 a에 대한 질량-스프링 변위 임펄스 응답 h(τ) = e^(−ζωτ)·sin(ω_d τ)/ω_d (h(0) = 0 → 시작·컷 순간 튐 없음).
            // 창 끝 0.5 s에서 0이 되도록 코사인 테이퍼. 사다리꼴 합의 가중 Δ를 미리 곱한다.
            var k = new float[Taps];
            double w = 2 * Math.PI * Frequency, wd = w * Math.Sqrt(1 - Damping * Damping);
            for (int j = 0; j < Taps; j++)
            {
                double tau = j * (double)Step;
                double taper = 0.5 * (1 + Math.Cos(Math.PI * tau / Window));
                k[j] = (float)(Math.Exp(-Damping * w * tau) * Math.Sin(wd * tau) / wd * taper * Step);
            }
            return k;
        }

        /// <summary>테스트·제자리 트랙용.</summary>
        public void Evaluate(KitAccelTrack track, float t, float hipFlexL, float hipFlexR, Span<Vec3> offsets)
        {
            var src = new TrackSource(track);
            Evaluate(ref src, t, hipFlexL, hipFlexR, offsets);
        }

        struct TrackSource : IKitAccelSource
        {
            readonly KitAccelTrack _t;
            public TrackSource(KitAccelTrack t) { _t = t; }
            public float SegmentStart(float t) => _t.SegmentStart(t);
            public Vec3 At(float t) => _t.At(t);
        }

        /// <summary>
        /// 재생 시각 t의 보조 뼈 오프셋(뼈 상자 축 틀 = 선수 레스트 좌표 벡터, m). hipFlex는 골반 틀 허벅지 앞뒤 기울기(°, 앞 +).
        /// </summary>
        public void Evaluate<TS>(ref TS src, float t, float hipFlexL, float hipFlexR, Span<Vec3> offsets) where TS : struct, IKitAccelSource
        {
            float seg = src.SegmentStart(t);
            // 창 합성 (몸 틀 응답 하나를 뼈별 상자에 투영)
            float x = 0, y = 0, z = 0;
            for (int j = 1; j < Taps; j++)
            {
                float tp = t - j * Step;
                if (tp < seg) break;
                var a = src.At(tp);
                float k = Kernel[j];
                x += k * a.X; y += k * a.Y; z += k * a.Z;
            }
            var r = new Vec3(x, y, z) * (-Gain);
            for (int i = 0; i < _bones.Length && i < offsets.Length; i++)
            {
                ref readonly var b = ref _bones[i];
                float g = GateScale(b, hipFlexL, hipFlexR);
                float u0 = Soft(Dot(r, b.Ax0), b.Min.X * g, b.Max.X * g);
                float u1 = Soft(Dot(r, b.Ax1), b.Min.Y * g, b.Max.Y * g);
                float u2 = Soft(Dot(r, b.Ax2), b.Min.Z * g, b.Max.Z * g);
                var o = b.Ax0 * u0 + b.Ax1 * u1 + b.Ax2 * u2;
                float len = o.Length();
                if (len > MaxOffset) o = o * (MaxOffset / len);
                offsets[i] = o;
            }
        }

        static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>상자 [lo, hi] 안으로 부드럽게 포화 (tanh) — 경계에서 멈칫하는 꺾임이 없다. lo ≤ 0 ≤ hi.</summary>
        static float Soft(float v, float lo, float hi)
        {
            if (v > 0) return hi > 0 ? hi * (float)Math.Tanh(v / hi) : 0f;
            if (v < 0) return lo < 0 ? lo * (float)Math.Tanh(v / lo) : 0f;
            return 0f;
        }

        /// <summary>skeleton.kit_gate와 같다: 1 − smoothstep((sign·max(flex) − a0)/(a1 − a0)).</summary>
        public static float GateScale(in Bone b, float flexL, float flexR)
        {
            if (b.GateSign == 0) return 1f;
            float a = Math.Max(b.GateSign * flexL, b.GateSign * flexR);
            float u = (a - b.Gate0) / (b.Gate1 - b.Gate0);
            u = u < 0 ? 0 : u > 1 ? 1 : u;
            return 1f - u * u * (3 - 2 * u);
        }
    }
}

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 하이라이트 재생용 가속도 소스 (062 FR-21): 현재 포즈의 클립 골반 가속도 커브(클립 시각을 과거로 되돌려 읽음, 가중 합)
    /// + 리플레이 궤적 가속도(30 Hz 보간 위치의 2차 차분, 현재 정면 각으로 몸 틀 변환). 둘 다 재생 시각의 순수 함수.
    /// 클립 커브의 스파이크(골키퍼 다이빙 등 최대 ≈ 550 m/s²)는 크기 상한으로 자른다. 할당 없는 값 타입.
    /// </summary>
    public struct PoseAccelSource : IKitAccelSource
    {
        /// <summary>합친 가속도 크기 상한 m/s² (T15 진단: gk_smother_r 551 스파이크).</summary>
        public const float MaxAccel = 25f;
        /// <summary>궤적 가속도 차분 간격 (s) — 10 Hz 판단을 30 Hz로 보간한 꺾임을 고르게 한다.</summary>
        public const float TrajStep = 0.1f;

        readonly ClipLibrary _lib;
        readonly HighlightData _d;
        readonly int _e;
        readonly float _t0, _cos, _sin;
        readonly PoseSample _s;

        public PoseAccelSource(ClipLibrary lib, HighlightData d, int entity, float t, in PoseSample s)
        {
            _lib = lib; _d = d; _e = entity; _t0 = t; _s = s;
            _cos = (float)Math.Cos(s.Yaw); _sin = (float)Math.Sin(s.Yaw);
        }

        /// <summary>창(0.5 s) 안에서 가장 최근의 불연속(배치·기록 건너뜀·교체 투입) 프레임 시각, 없으면 하이라이트 시작.</summary>
        public float SegmentStart(float t)
        {
            if (_d == null) return float.NegativeInfinity;
            _d.FrameAt(t, out int f, out float a);
            if (a > 0) f++;
            int lo = Math.Max(1, f - (int)Math.Ceiling(KitSecondaryMotion.Window * HighlightData.FrameRate) - 1);
            for (int i = Math.Min(f, _d.Frames - 1); i >= lo; i--)
            {
                int k = i * HighlightData.Entities + _e;
                if (_d.Break[k] != 0 || _d.Swap[k] || !_d.Present[k - HighlightData.Entities]) return _d.StartTime + i / HighlightData.FrameRate;
            }
            return _d.StartTime;
        }

        public Vec3 At(float t)
        {
            float back = _t0 - t;
            float x = 0, y = 0, z = 0;
            if (_lib != null)
                for (int i = 0; i < _s.Count; i++)
                {
                    var c = _lib.Get(_s.Clip(i));
                    var pa = c.PelvisAccel;
                    if (pa == null) continue;
                    var v = Sample(pa, c, _s.Time(i) - back);
                    float w = _s.Weight(i);
                    x += w * v.X; y += w * v.Y; z += w * v.Z;
                }
            if (_d != null)
            {
                var p0 = _d.Pos(_e, t - TrajStep);
                var p1 = _d.Pos(_e, t);
                var p2 = _d.Pos(_e, t + TrajStep);
                float inv = 1f / (TrajStep * TrajStep);
                float ax = (p2.X - 2 * p1.X + p0.X) * inv, az = (p2.Z - 2 * p1.Z + p0.Z) * inv;
                x += ax * _cos - az * _sin;   // Unity 정면 = (sin yaw, 0, cos yaw), 오른쪽 = (cos yaw, 0, −sin yaw)
                z += ax * _sin + az * _cos;
            }
            var r = new Vec3(x, y, z);
            float len = r.Length();
            return len > MaxAccel ? r * (MaxAccel / len) : r;
        }

        static Vec3 Sample(Vec3[] pa, ClipInfo c, float clipTime)
        {
            float u = clipTime * ClipInfo.Fps;
            int n = pa.Length - 1;
            if (c.Loop && c.Frames > 0)
            {
                u %= c.Frames;
                if (u < 0) u += c.Frames;
            }
            else
            {
                if (u <= 0) return pa[0];
                if (u >= n) return pa[n];
            }
            int f = Math.Min((int)u, n - 1);
            float k = u - f;
            return pa[f] + (pa[f + 1] - pa[f]) * k;
        }
    }
}
