using System;
using RoadToPro.Core.Logging;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>로코모션 블렌드 결과: 최대 5개 (idle + 보법 2 × 방향 2). 할당 없는 값 타입.</summary>
    public struct LocoSample
    {
        public const int Capacity = 5;
        int _n;
        int _c0, _c1, _c2, _c3, _c4;
        float _w0, _w1, _w2, _w3, _w4;
        float _t0, _t1, _t2, _t3, _t4;

        public int Count => _n;

        public int Clip(int i) => i switch { 0 => _c0, 1 => _c1, 2 => _c2, 3 => _c3, _ => _c4 };
        public float Weight(int i) => i switch { 0 => _w0, 1 => _w1, 2 => _w2, 3 => _w3, _ => _w4 };
        /// <summary>클립 안 시각(초).</summary>
        public float Time(int i) => i switch { 0 => _t0, 1 => _t1, 2 => _t2, 3 => _t3, _ => _t4 };

        internal void Add(int clip, float w, float t)
        {
            if (w <= 1e-6f) return;
            for (int i = 0; i < _n; i++)
                if (Clip(i) == clip) { Set(i, clip, Weight(i) + w, t); return; }
            if (_n >= Capacity) return;
            Set(_n++, clip, w, t);
        }

        void Set(int i, int c, float w, float t)
        {
            switch (i)
            {
                case 0: _c0 = c; _w0 = w; _t0 = t; break;
                case 1: _c1 = c; _w1 = w; _t1 = t; break;
                case 2: _c2 = c; _w2 = w; _t2 = t; break;
                case 3: _c3 = c; _w3 = w; _t3 = t; break;
                default: _c4 = c; _w4 = w; _t4 = t; break;
            }
        }
    }

    /// <summary>
    /// 속도·방향 → 로코모션 클립 가중치 (specs/043 FR-41·62, specs/055 FR-01·50~52). Blend Tree와 같은 역할(2D: 속도 × 8방향).
    /// 모든 로코모션 클립은 정규화 위상 φ(0~1)를 공유한다. 위상 속도 = 속도 / 블렌드 보폭(한 주기 이동 거리) → 접지 발이 시뮬 속도와 맞는다.
    /// 방향은 선수 정면 기준 각(°, 0 정면, 90 오른쪽).
    /// 055: 보법 단계 walk·jog·run·sprint. run 클립은 정면에만 있다 — 없는 방향의 run 단계는 같은 방향 jog·sprint를 정면과 같은 비율(RunMix)로
    /// 섞은 가상 단계다. 선수 원형(LocoStyle)이 있으면 정면 run은 원형 run, 정면 sprint는 원형 극단 lo·hi를 Jitter로 섞는다.
    /// 슬롯: 방향 2 × 단계 2 × (가상·극단 2) 이지만 같은 클립은 합쳐져 최대 5 (LocoSample.Capacity).
    /// </summary>
    public static class LocomotionBlend
    {
        /// <summary>블렌드 입력 속도 변화 상한 (m/s²) — 시뮬 운동학(005) 가속 상한보다 넉넉하게.</summary>
        public const float MaxBlendAccel = 14f;
        /// <summary>이보다 큰 속도 점프는 시뮬 오류로 진단 로그 (FR-62).</summary>
        public const float SimErrorAccel = 25f;

        static readonly Gait[] Levels = { Gait.Walk, Gait.Jog, Gait.Run, Gait.Sprint };

        /// <summary>한 방향·한 단계의 클립 1~2개(같은 위상 공유) + 단계 속도·보폭.</summary>
        readonly struct Level
        {
            public readonly int C0, C1;
            public readonly float W1, Speed, Stride;

            public Level(int c0, int c1, float w1, float speed, float stride)
            {
                C0 = c0;
                C1 = c1;
                W1 = w1;
                Speed = speed;
                Stride = stride;
            }
        }

        static void Dirs(float dirDeg, out int i0, out int i1, out float f)
        {
            float d = ((dirDeg % 360f) + 360f) % 360f;
            i0 = (int)(d / 45f) % 8;
            i1 = (i0 + 1) % 8;
            f = (d - i0 * 45f) / 45f;
            if (f < 0) f = 0;
            if (f > 1) f = 1;
        }

        static Level Single(ClipLibrary lib, int c)
        {
            var ci = lib.Get(c);
            return new Level(c, -1, 0f, ci.Speed, ci.Speed * ci.Length);
        }

        static Level Mix(ClipLibrary lib, int a, int b, float w)
        {
            if (a == b || w <= 0f) return Single(lib, a);
            var ca = lib.Get(a);
            var cb = lib.Get(b);
            return new Level(a, b, w, ca.Speed + (cb.Speed - ca.Speed) * w,
                ca.Speed * ca.Length + (cb.Speed * cb.Length - ca.Speed * ca.Length) * w);
        }

        /// <summary>run 클립이 없는 방향의 run 단계 비율 = 정면 (run − jog) / (sprint − jog) 속도 비 (공유 클립 기준).</summary>
        static float RunMix(ClipLibrary lib)
        {
            float vj = lib.Get(lib.Locomotion(Gait.Jog, 0)).Speed;
            float vr = lib.Get(lib.Locomotion(Gait.Run, 0)).Speed;
            float vs = lib.Get(lib.Locomotion(Gait.Sprint, 0)).Speed;
            return vs > vj ? (vr - vj) / (vs - vj) : 0.5f;
        }

        static Level LevelAt(ClipLibrary lib, int level, int dir, in LocoStyle st)
        {
            var g = Levels[level];
            if (g == Gait.Run)
            {
                if (dir == 0 && st.Valid) return Single(lib, st.Run);
                int r = lib.Locomotion(Gait.Run, dir);
                if (r >= 0) return Single(lib, r);
                return Mix(lib, lib.Locomotion(Gait.Jog, dir), lib.Locomotion(Gait.Sprint, dir), RunMix(lib));
            }
            if (g == Gait.Sprint && dir == 0 && st.Valid) return Mix(lib, st.SprintLo, st.SprintHi, st.Jitter);
            return Single(lib, lib.Locomotion(g, dir));
        }

        static float LevelSpeed(ClipLibrary lib, int level, float dirDeg, in LocoStyle st)
        {
            Dirs(dirDeg, out var i0, out var i1, out var f);
            return LevelAt(lib, level, i0, st).Speed * (1 - f) + LevelAt(lib, level, i1, st).Speed * f;
        }

        public static float GaitSpeed(ClipLibrary lib, Gait g, float dirDeg) => GaitSpeed(lib, g, dirDeg, default);

        public static float GaitSpeed(ClipLibrary lib, Gait g, float dirDeg, in LocoStyle st)
        {
            for (int i = 0; i < Levels.Length; i++)
                if (Levels[i] == g) return LevelSpeed(lib, i, dirDeg, st);
            return 0f;
        }

        /// <summary>속도 → (단계 a, 단계 b, b 비율). a = −1이면 idle↔걷기. 기준 속도는 방향 보간값(두 방향 클립에 같은 단계 가중치 = 2D Blend Tree).</summary>
        static void GaitPair(ClipLibrary lib, float dirDeg, float v, in LocoStyle st, out int a, out int b, out float t)
        {
            float vw = LevelSpeed(lib, 0, dirDeg, st);
            if (v <= vw)
            {
                a = -1;
                b = 0;
                t = vw > 0 ? Math.Max(0f, v) / vw : 0f;
                return;
            }
            float v0 = vw;
            for (int i = 0; i < Levels.Length - 1; i++)
            {
                float v1 = LevelSpeed(lib, i + 1, dirDeg, st);
                if (v <= v1)
                {
                    a = i;
                    b = i + 1;
                    t = v1 > v0 ? (v - v0) / (v1 - v0) : 1f;
                    return;
                }
                v0 = v1;
            }
            a = Levels.Length - 1;
            b = a;
            t = 0;
        }

        /// <summary>블렌드 보폭(m/주기) — Evaluate와 같은 가중치 규칙.</summary>
        static float Stride(ClipLibrary lib, float v, float dirDeg, in LocoStyle st)
        {
            Dirs(dirDeg, out var i0, out var i1, out var f);
            GaitPair(lib, dirDeg, v, st, out var a, out var b, out var t);
            float s = 0, w = 0;
            for (int k = 0; k < 2; k++)
            {
                int dir = k == 0 ? i0 : i1;
                float wd = k == 0 ? 1 - f : f;
                if (wd <= 0) continue;
                if (a >= 0)
                {
                    s += wd * (1 - t) * LevelAt(lib, a, dir, st).Stride;
                    w += wd * (1 - t);
                }
                float wb = a >= 0 ? t : 1f; // idle↔walk 구간에서는 걷기 보폭으로 위상을 진행
                s += wd * wb * LevelAt(lib, b, dir, st).Stride;
                w += wd * wb;
            }
            return w > 0 ? s / w : 0f;
        }

        /// <summary>위상 속도(주기/초).</summary>
        public static float PhaseRate(ClipLibrary lib, float speed, float dirDeg) => PhaseRate(lib, speed, dirDeg, default);

        public static float PhaseRate(ClipLibrary lib, float speed, float dirDeg, in LocoStyle st)
        {
            if (speed <= 1e-4f) return 0f;
            float L = Stride(lib, speed, dirDeg, st);
            return L > 0 ? speed / L : 0f;
        }

        public static LocoSample Evaluate(ClipLibrary lib, float speed, float dirDeg, float phase, float idleTime = 0f) =>
            Evaluate(lib, speed, dirDeg, phase, idleTime, default);

        static void AddLevel(ref LocoSample s, ClipLibrary lib, in Level l, float w, float phase)
        {
            var c0 = lib.Get(l.C0);
            s.Add(l.C0, w * (1 - l.W1), phase * c0.Length);
            if (l.C1 >= 0)
            {
                var c1 = lib.Get(l.C1);
                s.Add(l.C1, w * l.W1, phase * c1.Length);
            }
        }

        public static LocoSample Evaluate(ClipLibrary lib, float speed, float dirDeg, float phase, float idleTime, in LocoStyle st)
        {
            var s = new LocoSample();
            phase -= (float)Math.Floor(phase);
            Dirs(dirDeg, out var i0, out var i1, out var f);
            GaitPair(lib, dirDeg, speed, st, out var a, out var b, out var t);
            for (int k = 0; k < 2; k++)
            {
                int dir = k == 0 ? i0 : i1;
                float wd = k == 0 ? 1 - f : f;
                if (wd <= 0) continue;
                if (a < 0)
                {
                    var idle = lib.Get(lib.IdleIndex);
                    float it = idleTime - (float)Math.Floor(idleTime / idle.Length) * idle.Length;
                    s.Add(lib.IdleIndex, wd * (1 - t), it);
                    AddLevel(ref s, lib, LevelAt(lib, 0, dir, st), wd * t, phase);
                    continue;
                }
                AddLevel(ref s, lib, LevelAt(lib, a, dir, st), wd * (1 - t), phase);
                if (b != a) AddLevel(ref s, lib, LevelAt(lib, b, dir, st), wd * t, phase);
            }
            return s;
        }

        /// <summary>블렌드 입력 속도 변화 제한 (FR-62). 큰 점프는 시뮬 오류로 진단 로그.</summary>
        public static float LimitSpeed(float prev, float target, float dt, ILogger log) => LimitSpeed(prev, target, dt, log, "판단");

        /// <summary>원인 표시가 붙는 판(specs/058 FR-12): 로그 끝에 "원인=배치|기록|판단".</summary>
        public static float LimitSpeed(float prev, float target, float dt, ILogger log, string cause)
        {
            float d = target - prev;
            if (Math.Abs(d) > SimErrorAccel * dt)
                log.Warning("[anim] 속도 점프 {0:0.00} → {1:0.00} m/s ({2:0.000} s) — 시뮬 오류 의심, 블렌드 제한 적용 원인=" + cause, prev, target, dt);
            float max = MaxBlendAccel * dt;
            if (d > max) return prev + max;
            if (d < -max) return prev - max;
            return target;
        }
    }
}
