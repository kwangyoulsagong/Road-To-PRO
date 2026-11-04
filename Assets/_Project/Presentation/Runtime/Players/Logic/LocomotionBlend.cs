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
    /// 속도·방향 → 로코모션 클립 가중치 (specs/043 FR-41·62). Blend Tree와 같은 역할(2D: 속도 × 8방향).
    /// 모든 로코모션 클립은 정규화 위상 φ(0~1)를 공유한다. 위상 속도 = 속도 / 블렌드 보폭(한 주기 이동 거리) → 접지 발이 시뮬 속도와 맞는다.
    /// 방향은 선수 정면 기준 각(°, 0 정면, 90 오른쪽).
    /// </summary>
    public static class LocomotionBlend
    {
        /// <summary>블렌드 입력 속도 변화 상한 (m/s²) — 시뮬 운동학(005) 가속 상한보다 넉넉하게.</summary>
        public const float MaxBlendAccel = 14f;
        /// <summary>이보다 큰 속도 점프는 시뮬 오류로 진단 로그 (FR-62).</summary>
        public const float SimErrorAccel = 25f;

        static readonly Gait[] Levels = { Gait.Walk, Gait.Jog, Gait.Sprint };

        static void Dirs(float dirDeg, out int i0, out int i1, out float f)
        {
            float d = ((dirDeg % 360f) + 360f) % 360f;
            i0 = (int)(d / 45f) % 8;
            i1 = (i0 + 1) % 8;
            f = (d - i0 * 45f) / 45f;
            if (f < 0) f = 0;
            if (f > 1) f = 1;
        }

        static float SpeedOf(ClipLibrary lib, Gait g, int dir) => lib.Get(lib.Locomotion(g, dir)).Speed;

        public static float GaitSpeed(ClipLibrary lib, Gait g, float dirDeg)
        {
            Dirs(dirDeg, out var i0, out var i1, out var f);
            return SpeedOf(lib, g, i0) * (1 - f) + SpeedOf(lib, g, i1) * f;
        }

        /// <summary>속도 → (보법 a, 보법 b, b 비율). a = −1이면 idle↔걷기. 기준 속도는 방향 보간값(두 방향 클립에 같은 보법 가중치 = 2D Blend Tree).</summary>
        static void GaitPair(ClipLibrary lib, float dirDeg, float v, out int a, out int b, out float t)
        {
            float vw = GaitSpeed(lib, Gait.Walk, dirDeg);
            if (v <= vw)
            {
                a = -1;
                b = 0;
                t = vw > 0 ? Math.Max(0f, v) / vw : 0f;
                return;
            }
            for (int i = 0; i < Levels.Length - 1; i++)
            {
                float v0 = GaitSpeed(lib, Levels[i], dirDeg), v1 = GaitSpeed(lib, Levels[i + 1], dirDeg);
                if (v <= v1)
                {
                    a = i;
                    b = i + 1;
                    t = (v - v0) / (v1 - v0);
                    return;
                }
            }
            a = Levels.Length - 1;
            b = a;
            t = 0;
        }

        /// <summary>블렌드 보폭(m/주기)과 이동 가중치 합.</summary>
        static float Stride(ClipLibrary lib, float v, float dirDeg)
        {
            Dirs(dirDeg, out var i0, out var i1, out var f);
            float s = 0, w = 0;
            for (int k = 0; k < 2; k++)
            {
                int dir = k == 0 ? i0 : i1;
                float wd = k == 0 ? 1 - f : f;
                if (wd <= 0) continue;
                GaitPair(lib, dirDeg, v, out var a, out var b, out var t);
                if (a >= 0)
                {
                    var ca = lib.Get(lib.Locomotion(Levels[a], dir));
                    s += wd * (1 - t) * ca.Speed * ca.Length;
                    w += wd * (1 - t);
                }
                var cb = lib.Get(lib.Locomotion(Levels[b], dir));
                float wb = a >= 0 ? t : 1f; // idle↔walk 구간에서는 걷기 보폭으로 위상을 진행
                s += wd * wb * cb.Speed * cb.Length;
                w += wd * wb;
            }
            return w > 0 ? s / w : 0f;
        }

        /// <summary>위상 속도(주기/초).</summary>
        public static float PhaseRate(ClipLibrary lib, float speed, float dirDeg)
        {
            if (speed <= 1e-4f) return 0f;
            float L = Stride(lib, speed, dirDeg);
            return L > 0 ? speed / L : 0f;
        }

        public static LocoSample Evaluate(ClipLibrary lib, float speed, float dirDeg, float phase, float idleTime = 0f)
        {
            var s = new LocoSample();
            phase -= (float)Math.Floor(phase);
            Dirs(dirDeg, out var i0, out var i1, out var f);
            for (int k = 0; k < 2; k++)
            {
                int dir = k == 0 ? i0 : i1;
                float wd = k == 0 ? 1 - f : f;
                if (wd <= 0) continue;
                GaitPair(lib, dirDeg, speed, out var a, out var b, out var t);
                if (a < 0)
                {
                    var idle = lib.Get(lib.IdleIndex);
                    float it = idleTime - (float)Math.Floor(idleTime / idle.Length) * idle.Length;
                    s.Add(lib.IdleIndex, wd * (1 - t), it);
                    var w = lib.Get(lib.Locomotion(Gait.Walk, dir));
                    s.Add(w.Index, wd * t, phase * w.Length);
                    continue;
                }
                var ca = lib.Get(lib.Locomotion(Levels[a], dir));
                s.Add(ca.Index, wd * (1 - t), phase * ca.Length);
                if (b != a)
                {
                    var cb = lib.Get(lib.Locomotion(Levels[b], dir));
                    s.Add(cb.Index, wd * t, phase * cb.Length);
                }
            }
            return s;
        }

        /// <summary>블렌드 입력 속도 변화 제한 (FR-62). 큰 점프는 시뮬 오류로 진단 로그.</summary>
        public static float LimitSpeed(float prev, float target, float dt, ILogger log)
        {
            float d = target - prev;
            if (Math.Abs(d) > SimErrorAccel * dt)
                log.Warning("[anim] 속도 점프 {0:0.00} → {1:0.00} m/s ({2:0.000} s) — 시뮬 오류 의심, 블렌드 제한 적용", prev, target, dt);
            float max = MaxBlendAccel * dt;
            if (d > max) return prev + max;
            if (d < -max) return prev - max;
            return target;
        }
    }
}
