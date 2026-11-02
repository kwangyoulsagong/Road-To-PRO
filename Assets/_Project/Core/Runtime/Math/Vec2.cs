using System.Runtime.CompilerServices;
using System;

namespace RoadToPro.Core.Math
{
    /// <summary>2D 벡터 값 타입 (specs/001 FR-10). 엔진 Vector2 대신 쓴다.</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        /// <summary>이 제곱길이 미만이면 정규화 결과는 0 벡터 (길이 1e−6 m 미만).</summary>
        public const float NormalizeEpsilonSq = 1e-12f;

        public readonly float X;
        public readonly float Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>2D 외적의 z 성분.</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float LengthSq() => X * X + Y * Y;

        /// <summary>큰 값에서도 오버플로하지 않도록 최대 성분으로 나눠 계산.</summary>
        public float Length()
        {
            var sq = X * X + Y * Y;
            if (sq < float.PositiveInfinity) return DMath.Sqrt(sq);
            if (float.IsNaN(sq)) return float.NaN; // NaN을 0으로 숨기지 않는다 (결정론 리뷰 #2)
            // 제곱이 넘치면 최대 성분으로 나눠 계산
            var ax = DMath.Abs(X);
            var ay = DMath.Abs(Y);
            var m = DMath.Max(ax, ay);
            var x = ax / m;
            var y = ay / m;
            return m * DMath.Sqrt(x * x + y * y);
        }

        /// <summary>길이 1. 0 벡터·아주 짧은 벡터는 0 벡터 (NaN 금지).</summary>
        public Vec2 Normalized()
        {
            var sq = LengthSq();
            if (sq < NormalizeEpsilonSq) return Zero;
            var len = sq < float.PositiveInfinity ? DMath.Sqrt(sq) : Length();
            var inv = 1f / len;
            return new Vec2(X * inv, Y * inv);
        }

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length();
        public static float DistanceSq(Vec2 a, Vec2 b) => (a - b).LengthSq();

        /// <summary>a를 onto 방향에 투영. onto가 0 벡터면 0 벡터.</summary>
        public static Vec2 Project(Vec2 a, Vec2 onto)
        {
            var d = onto.LengthSq();
            if (d < NormalizeEpsilonSq) return Zero;
            return onto * (Dot(a, onto) / d);
        }

        /// <summary>반시계 방향 회전 (라디안).</summary>
        public Vec2 Rotate(float radians)
        {
            var c = DMath.Cos(radians);
            var s = DMath.Sin(radians);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        public bool Equals(Vec2 other) => this == other;
        public override bool Equals(object obj) => obj is Vec2 v && this == v;
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => FormattableString.Invariant($"({X}, {Y})");
    }
}
