using System;
using RoadToPro.Core.Math;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>사용자 카메라 모드 (specs/042 FR-30).</summary>
    public enum CameraMode
    {
        Broadcast = 0,
        TopView = 1,
        MyPlayer = 2,
    }

    /// <summary>샷 종류. 흔들림·화각 범위·리버스 표시가 종류에 따라 정해진다.</summary>
    public enum ShotKind
    {
        Main = 0,
        TopView = 1,
        MyPlayer = 2,
        Closeup = 3,
        Celebration = 4,
        ReplayHigh = 5,
        ReplayShooter = 6,
        ReplayReverse = 7,
        ReactionWide = 8,
        KickoffWide = 9,
        FallbackHigh = 10,
    }

    /// <summary>카메라 한 프레임. 화각은 화면비와 무관한 반각 tan으로 저장하고 재생 때 FOV로 바꾼다.</summary>
    public readonly struct CameraPose
    {
        public readonly Vec3 Position;
        /// <summary>단위 시선 벡터.</summary>
        public readonly Vec3 Forward;
        public readonly float TanV, TanH;
        /// <summary>흔들림 오프셋 (FR-40). 끄면 0이다.</summary>
        public readonly Vec3 Shake;

        public CameraPose(Vec3 position, Vec3 forward, float tanV, float tanH, Vec3 shake = default)
        {
            Position = position;
            Forward = forward;
            TanV = tanV;
            TanH = tanH;
            Shake = shake;
        }

        public CameraPose WithShake(Vec3 shake) => new CameraPose(Position, Forward, TanV, TanH, shake);

        public static CameraPose Lerp(in CameraPose a, in CameraPose b, float t) => new CameraPose(
            a.Position + (b.Position - a.Position) * t,
            (a.Forward + (b.Forward - a.Forward) * t).Normalized(),
            a.TanV + (b.TanV - a.TanV) * t,
            a.TanH + (b.TanH - a.TanH) * t,
            a.Shake + (b.Shake - a.Shake) * t);

        /// <summary>수직 FOV(도). 화면비를 반영해 수평으로 필요한 범위까지 담는다 (FR-55).</summary>
        public float FovDeg(float aspect, float minDeg, float maxDeg)
        {
            float half = MathF.Max(TanV, TanH / MathF.Max(0.01f, aspect));
            float deg = 2f * MathF.Atan(half) * CameraMath.Rad2Deg;
            return deg < minDeg ? minDeg : deg > maxDeg ? maxDeg : deg;
        }
    }

    /// <summary>시뮬 시각 구간 하나를 차지하는 샷.</summary>
    public readonly struct ShotPlan
    {
        public readonly double Start, End;
        public readonly ShotKind Kind;
        /// <summary>샷이 따라가는 엔티티, 없으면 −1.</summary>
        public readonly int Focus;

        public ShotPlan(double start, double end, ShotKind kind, int focus = -1)
        {
            Start = start;
            End = end;
            Kind = kind;
            Focus = focus;
        }
    }

    public static class CameraMath
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Smoothstep(float a, float b, float x)
        {
            if (b <= a) return x >= b ? 1f : 0f;
            float t = (x - a) / (b - a);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        /// <summary>고정 스텝 지수 감쇠 계수. 렌더 프레임과 무관하게 30 Hz 프레임마다 쓴다.</summary>
        public static float DampFactor(float dt, float tau) => tau <= 0f ? 1f : 1f - MathF.Exp(-dt / tau);

        /// <summary>뷰포트 좌표(x, y는 0~1, z는 깊이). 카메라 뒤면 z ≤ 0.</summary>
        public static Vec3 Viewport(in CameraPose pose, float aspect, float minDeg, float maxDeg, Vec3 point)
        {
            Basis(pose.Forward, out var right, out var up);
            var v = point - (pose.Position + pose.Shake);
            float z = Vec3.Dot(v, pose.Forward);
            if (z <= 1e-4f) return new Vec3(0.5f, 0.5f, z);
            float tan = MathF.Tan(pose.FovDeg(aspect, minDeg, maxDeg) * 0.5f * Deg2Rad);
            float x = Vec3.Dot(v, right) / z / (tan * aspect);
            float y = Vec3.Dot(v, up) / z / tan;
            return new Vec3(0.5f + x * 0.5f, 0.5f + y * 0.5f, z);
        }

        /// <summary>시선 기준 오른쪽·위 벡터. 시선이 수직에 가까우면 +Z를 위로 쓴다.</summary>
        public static void Basis(Vec3 forward, out Vec3 right, out Vec3 up)
        {
            var world = MathF.Abs(forward.Y) > 0.999f ? new Vec3(0f, 0f, 1f) : new Vec3(0f, 1f, 0f);
            right = Vec3.Cross(world, forward).Normalized();
            up = Vec3.Cross(forward, right);
        }
    }
}
