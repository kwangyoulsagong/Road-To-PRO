using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>카메라 연속성 측정 결과 (specs/058 FR-30·31).</summary>
    public sealed class ContinuityReport
    {
        public float MaxAngularSpeed, MaxAngularAccel, MaxFovSpeed;
        public int Violations, Frames;
        public readonly List<string> Samples = new List<string>();
    }

    /// <summary>
    /// 한 샷 안의 카메라 변화량 검사 (specs/058 FR-30·31, 검증·진단 전용 — 재생 경로에서 부르지 않는다).
    /// 재생 1x 기준 프레임 사이 시선 회전 각속도·각가속도·수직 화각 변화 속도를 잰다. 흔들림 오프셋(FR-40)은 위치에만 들어가 시선에는 없다.
    /// 컷 프레임(장면 경계·데드볼 컷·골 시퀀스/리플레이 진입·퇴장)은 불연속이 허용되고, 리플레이 진입 블렌드(042 FR-14) 구간은 진입 전환으로 본다.
    /// 리플레이 샷은 0.5x 슬로모로 재생되므로 재생 시간 기준으로 환산한다 (각속도·화각 × 0.5, 각가속도 × 0.25).
    /// </summary>
    public static class CameraContinuity
    {
        public static ContinuityReport Measure(CameraTrack track, double entryBlendSeconds, float maxAngSpeed, float maxAngAccel, float maxFovSpeed, ContinuityReport into = null)
        {
            var r = into ?? new ContinuityReport();
            float dt = 1f / HighlightData.FrameRate;
            int blendFrames = (int)Math.Ceiling(entryBlendSeconds * HighlightData.FrameRate);
            float prevSpeed = float.NaN;
            for (int f = 1; f < track.Frames; f++)
            {
                r.Frames++;
                if (track.CutAt[f] || f <= blendFrames && IsReplay(track.Shots[f])) { prevSpeed = float.NaN; continue; }
                var a = track.Poses[f - 1];
                var b = track.Poses[f];
                float dot = Vec3.Dot(a.Forward, b.Forward);
                dot = dot > 1f ? 1f : dot < -1f ? -1f : dot;
                float speed = MathF.Acos(dot) * CameraMath.Rad2Deg / dt;
                float fovA = 2f * MathF.Atan(a.TanV) * CameraMath.Rad2Deg, fovB = 2f * MathF.Atan(b.TanV) * CameraMath.Rad2Deg;
                float fovSpeed = MathF.Abs(fovB - fovA) / dt;
                if (IsReplay(track.Shots[f])) { speed *= ReplayRate; fovSpeed *= ReplayRate; }
                float accel = float.IsNaN(prevSpeed) ? 0f : MathF.Abs(speed - prevSpeed) / dt * (IsReplay(track.Shots[f]) ? ReplayRate : 1f);
                prevSpeed = speed;
                if (speed > r.MaxAngularSpeed) r.MaxAngularSpeed = speed;
                if (accel > r.MaxAngularAccel) r.MaxAngularAccel = accel;
                if (fovSpeed > r.MaxFovSpeed) r.MaxFovSpeed = fovSpeed;
                if (speed > maxAngSpeed || accel > maxAngAccel || fovSpeed > maxFovSpeed)
                {
                    r.Violations++;
                    if (r.Samples.Count < 10)
                        r.Samples.Add($"t={track.StartTime + f * dt:0.00} 샷 {track.Shots[f]}: {speed:0}°/s, {accel:0}°/s², 화각 {fovSpeed:0.0}°/s");
                }
            }
            return r;
        }

        /// <summary>리플레이 재생 속도 (확정: 리플레이 0.5x 슬로모).</summary>
        public static float ReplayRate => Highlight.Logic.PlaybackSettings.ReplayRate;

        static bool IsReplay(ShotKind k) => k == ShotKind.ReplayHigh || k == ShotKind.ReplayShooter || k == ShotKind.ReplayReverse || k == ShotKind.FallbackHigh;
    }
}
