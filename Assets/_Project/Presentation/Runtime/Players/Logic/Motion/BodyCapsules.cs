using System;
using RoadToPro.Core.Math;

namespace RoadToPro.Presentation.Players.Logic.Motion
{
    /// <summary>신체 부위 캡슐 8개 (specs/054 FR-40, plan: 몸통 1·머리 1·팔 2·허벅지 2·정강이 2).</summary>
    public enum BodyCapsule : byte
    {
        Torso = 0, Head = 1, ArmL = 2, ArmR = 3, ThighL = 4, ThighR = 5, ShinL = 6, ShinR = 7,
    }

    /// <summary>캡슐 끝점 집합. 값은 렌더 본 위치에서 측정기가 채운다.</summary>
    public struct BodyPose
    {
        public const int Count = 8;
        public Vec3 A0, A1, A2, A3, A4, A5, A6, A7;
        public Vec3 B0, B1, B2, B3, B4, B5, B6, B7;

        public Vec3 A(int i) => i switch { 0 => A0, 1 => A1, 2 => A2, 3 => A3, 4 => A4, 5 => A5, 6 => A6, _ => A7 };
        public Vec3 B(int i) => i switch { 0 => B0, 1 => B1, 2 => B2, 3 => B3, 4 => B4, 5 => B5, 6 => B6, _ => B7 };
    }

    public static class BodyCapsules
    {
        /// <summary>캡슐 반지름 (m, 기준 키 1.80 m): 몸통·머리·팔·허벅지·정강이 [제안 — 플랜 부위 캡슐].</summary>
        public static float Radius(int i) => i switch { 0 => 0.15f, 1 => 0.105f, 2 => 0.045f, 3 => 0.045f, 4 => 0.075f, 5 => 0.075f, _ => 0.05f };

        /// <summary>두 선분 사이 최단 거리 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        public static float SegmentDistance(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2)
        {
            Vec3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vec3.Dot(d1, d1), e = Vec3.Dot(d2, d2), f = Vec3.Dot(d2, r);
            float s, t;
            const float eps = 1e-9f;
            if (a <= eps && e <= eps) return r.Length();
            if (a <= eps)
            {
                s = 0f;
                t = Clamp01(f / e);
            }
            else
            {
                float c = Vec3.Dot(d1, r);
                if (e <= eps)
                {
                    t = 0f;
                    s = Clamp01(-c / a);
                }
                else
                {
                    float b = Vec3.Dot(d1, d2), den = a * e - b * b;
                    s = den > eps ? Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).Length();
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>겹침 깊이 (양수 = 겹침, m).</summary>
        public static float Overlap(in BodyPose a, int i, in BodyPose b, int j) =>
            Radius(i) + Radius(j) - SegmentDistance(a.A(i), a.B(i), b.A(j), b.B(j));

        /// <summary>몸통 끼리 겹침.</summary>
        public static float TorsoOverlap(in BodyPose a, in BodyPose b) => Overlap(a, 0, b, 0);

        /// <summary>팔다리(2~7)가 상대 몸통을 파고든 가장 깊은 값 (양방향).</summary>
        public static float LimbPenetration(in BodyPose a, in BodyPose b)
        {
            float m = float.NegativeInfinity;
            for (int i = 2; i < BodyPose.Count; i++)
            {
                m = Math.Max(m, Overlap(a, i, b, 0));
                m = Math.Max(m, Overlap(b, i, a, 0));
            }
            return m;
        }
    }

    /// <summary>접지 구간 미끄러짐 측정 (specs/054 FR-10). 접지 = 높이 ≤ 기준 + 수평 속도 ≤ 기준. 값은 시뮬 시각 기준.</summary>
    public sealed class SlideTracker
    {
        readonly float _h, _v;
        bool _on, _has;
        float _sx, _sz, _px, _pz, _pt, _run;

        public SlideTracker(float heightMax, float speedMax)
        {
            _h = heightMax;
            _v = speedMax;
        }

        public bool InStance => _on;
        public float Current => _run;

        /// <summary>새 샘플. 접지가 끝나면 그 접지의 최대 수평 이동(m)을 slide에 주고 true.</summary>
        public bool Add(float t, float x, float y, float z, out float slide)
        {
            slide = 0f;
            bool done = false;
            float speed = float.PositiveInfinity;
            if (_has && t > _pt)
            {
                float dx = x - _px, dz = z - _pz;
                speed = (float)Math.Sqrt(dx * dx + dz * dz) / (t - _pt);
            }
            bool g = y <= _h && speed <= _v;
            if (_on && !g)
            {
                slide = _run;
                done = true;
                _on = false;
            }
            else if (!_on && g)
            {
                _on = true;
                _sx = x;
                _sz = z;
                _run = 0f;
            }
            if (_on)
            {
                float dx = x - _sx, dz = z - _sz;
                float m = (float)Math.Sqrt(dx * dx + dz * dz);
                if (m > _run) _run = m;
            }
            _px = x;
            _pz = z;
            _pt = t;
            _has = true;
            return done;
        }

        /// <summary>장면 끝: 진행 중 접지 마무리.</summary>
        public bool Finish(out float slide)
        {
            slide = _run;
            bool r = _on;
            _on = false;
            return r;
        }
    }

    public static class MotionStats
    {
        /// <summary>정렬 복사본의 선형 보간 백분위 (p 0~100). 빈 입력이면 0.</summary>
        public static float Percentile(System.Collections.Generic.IList<float> values, float p)
        {
            if (values == null || values.Count == 0) return 0f;
            var a = new float[values.Count];
            values.CopyTo(a, 0);
            Array.Sort(a);
            float u = p / 100f * (a.Length - 1);
            int i = (int)Math.Floor(u);
            if (i >= a.Length - 1) return a[a.Length - 1];
            return a[i] + (a[i + 1] - a[i]) * (u - i);
        }
    }
}
