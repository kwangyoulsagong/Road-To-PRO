using System.Runtime.CompilerServices;
using System;

namespace RoadToPro.Core.Math
{
    /// <summary>3D 벡터 값 타입 (specs/001 FR-10). x·y = 피치 평면, z = 높이(미터).</summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vec3(Vec2 xy, float z) : this(xy.X, xy.Y, z) { }

        public static Vec3 Zero => default;
        public Vec2 XY => new Vec2(X, Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);
        public static bool operator ==(Vec3 a, Vec3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        public static bool operator !=(Vec3 a, Vec3 b) => !(a == b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float LengthSq() => X * X + Y * Y + Z * Z;

        public float Length()
        {
            var sq = LengthSq();
            if (sq < float.PositiveInfinity) return DMath.Sqrt(sq);
            if (float.IsNaN(sq)) return float.NaN;
            var m = DMath.Max(DMath.Abs(X), DMath.Max(DMath.Abs(Y), DMath.Abs(Z)));
            var x = X / m;
            var y = Y / m;
            var z = Z / m;
            return m * DMath.Sqrt(x * x + y * y + z * z);
        }

        public Vec3 Normalized()
        {
            var sq = LengthSq();
            if (sq < Vec2.NormalizeEpsilonSq) return Zero;
            var inv = 1f / (sq < float.PositiveInfinity ? DMath.Sqrt(sq) : Length());
            return new Vec3(X * inv, Y * inv, Z * inv);
        }

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length();
        public static float DistanceSq(Vec3 a, Vec3 b) => (a - b).LengthSq();

        public static Vec3 Project(Vec3 a, Vec3 onto)
        {
            var d = onto.LengthSq();
            if (d < Vec2.NormalizeEpsilonSq) return Zero;
            return onto * (Dot(a, onto) / d);
        }

        public bool Equals(Vec3 other) => this == other;
        public override bool Equals(object obj) => obj is Vec3 v && this == v;
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
        public override string ToString() => FormattableString.Invariant($"({X}, {Y}, {Z})");
    }
}
