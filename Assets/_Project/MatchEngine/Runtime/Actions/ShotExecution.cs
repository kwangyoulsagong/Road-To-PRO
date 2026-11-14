using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 슈팅 실행 (specs/071 FR-14): 속도 v_shot = lerp(vLo, vHi, power), 조준 오차 σ_aim(°). 오차는 가로(요)·세로(피치) 각 정규 1개 —
    /// 액션 RNG 정확히 균등 4개(FR-61, <see cref="ActionMath.StdNormal"/> 2회). 평가 함수(<see cref="SigmaAimDeg"/>)는 RNG 없음.
    /// </summary>
    public static class ShotExecution
    {
        public const int RngPerShot = 4;

        public static float SpeedFor(ActionParams p, float power) => ActionMath.Lerp(p.ShotVLo, p.ShotVHi, power);

        /// <summary>
        /// FR-14 σ_aim(°) = lerp(6.0, 1.5, a_Finishing) × (1 + 0.5·(v − 22)/10) × (1 + 0.8·pressure·(1 − a_Composure)) × kickMul(약발·몸 각도, 008 킥 기술)
        /// × (헤더 1.6) × habitMul(컷인 장인 0.9) × extraMul(074 승부차기 압박 배수 자리, 기본 1).
        /// </summary>
        public static float SigmaAimDeg(ActionParams p, float aFinishing, float speed, float pressure, float aComposure, float kickMul, bool header,
            float habitMul = 1f, float extraMul = 1f)
        {
            var baseS = ActionMath.Lerp(p.ShotSigmaMax, p.ShotSigmaMin, aFinishing);
            var sp = 1f + p.ShotSpeedK * (speed - p.ShotSpeedRef) / 10f;
            if (sp < 0.1f) sp = 0.1f;
            var pr = pressure < 0f ? 0f : (pressure > 1f ? 1f : pressure);
            var cm = aComposure < 0f ? 0f : (aComposure > 1f ? 1f : aComposure);
            var press = 1f + p.ShotPressK * pr * (1f - cm);
            var s = baseS * sp * press * (kickMul > 0f ? kickMul : 1f) * (header ? p.ShotHeaderMul : 1f) * habitMul * extraMul;
            return s > 0f ? s : 0f;
        }

        /// <summary>버릇 "컷인 장인" σ 배수 (008 FR-71 ②): 감아차기(cutIn)일 때만, 레벨 0이면 1.</summary>
        public static float CutInMul(ActionParams p, bool curled, byte level) => curled && level > 0 ? p.ShotCutInMul : 1f;

        /// <summary>정규 2개 표본 (균등 4개). 오차 적용과 분리해 헤더처럼 "결정 시점 표본 → 나중 적용"에도 쓴다.</summary>
        public static void SampleNormals(SeedRng rng, out float nYaw, out float nPitch)
        {
            nYaw = ActionMath.StdNormal(rng);
            nPitch = ActionMath.StdNormal(rng);
        }

        /// <summary>의도 속도 벡터에 요·피치 각 오차(σ·n)를 넣는다. 크기는 그대로.</summary>
        public static Vec3 ApplyNormals(Vec3 intended, float sigmaDeg, float nYaw, float nPitch, out float yawErrDeg, out float pitchErrDeg)
        {
            yawErrDeg = nYaw * sigmaDeg;
            pitchErrDeg = nPitch * sigmaDeg;
            var speed = intended.Length();
            if (speed < 1e-5f) return intended;
            var h = intended.XY;
            var hl = h.Length();
            var yaw = hl > 1e-6f ? DMath.Atan2(h.Y, h.X) : 0f;
            var pitch = DMath.Atan2(intended.Z, hl);
            yaw += yawErrDeg * ActionMath.DegToRad;
            pitch += pitchErrDeg * ActionMath.DegToRad;
            var cp = DMath.Cos(pitch);
            return new Vec3(speed * cp * DMath.Cos(yaw), speed * cp * DMath.Sin(yaw), speed * DMath.Sin(pitch));
        }

        /// <summary>표본 + 적용 (RNG 정확히 4).</summary>
        public static Vec3 Apply(Vec3 intended, float sigmaDeg, SeedRng rng, out float yawErrDeg, out float pitchErrDeg)
        {
            SampleNormals(rng, out var a, out var b);
            return ApplyNormals(intended, sigmaDeg, a, b, out yawErrDeg, out pitchErrDeg);
        }

        /// <summary>
        /// 조준점(골라인 위 셀 중심 y, 높이 z)으로 속도 speed의 슛 의도 속도: 수평 거리 d, 비행 시간 ≈ d / speed 동안 중력 낙하를 보정한 세로 성분(근사, 무회전).
        /// 공 출발 높이 z0. 011 <c>SaveModel.AimedShot</c>과 같은 근사.
        /// </summary>
        public static Vec3 Intended(Vec3 from, Vec2 aimXY, float aimZ, float speed, float gravity)
        {
            var d = aimXY - from.XY;
            var dist = d.Length();
            if (dist < 1e-4f) return new Vec3(0f, 0f, speed);
            var t = dist / speed;
            var vz = (aimZ - from.Z) / t + 0.5f * gravity * t;
            var vh = speed * speed - vz * vz;
            vh = vh > 1f ? DMath.Sqrt(vh) : 1f;
            var dir = d / dist;
            return new Vec3(dir.X * vh, dir.Y * vh, vz);
        }
    }
}
