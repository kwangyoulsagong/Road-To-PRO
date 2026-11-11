using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>
    /// 장면 데이터 → 카메라 트랙 (specs/042). 입력은 장면·연출·설정·경기장뿐인 순수 계산이라, 재생 프레임률이나 배속이 달라도 같은 경로가 나온다 (AC-09·10).
    /// </summary>
    public sealed class CameraTrackBuilder
    {
        const float CloseupDistance = 5f, CloseupHeight = 1.7f, CelebrationDistance = 11f, CelebrationHeight = 3f;
        const float CloseupFovDeg = 26f, CelebrationFovDeg = 30f, NearSideLimit = -36.5f;
        const float ReplayFovDeg = 22f, ReplayMargin = 1.25f, HeadHeight = 1.5f;
        const float KickoffFovDeg = 30f, ReactionFovDeg = 24f;

        readonly CameraRigData _rig;
        readonly CameraTuning _t;
        readonly CameraSettings _settings;
        readonly FramingTarget[] _targets = new FramingTarget[FramingTargets.Capacity];

        public CameraTrackBuilder(CameraRigData rig, CameraTuning tuning, CameraSettings settings)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _t = tuning ?? throw new ArgumentNullException(nameof(tuning));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <param name="blendFrom">리플레이 진입 때 이어받을 이전 장면 마지막 포즈 (FR-14).</param>
        /// <param name="portrait">세로 화면이면 라이브 구간은 탑뷰 (FR-55).</param>
        public CameraTrack Build(HighlightData d, SceneBrief brief, CameraPose? blendFrom, bool portrait)
        {
            var shots = new List<ShotPlan>();
            double start = d.StartTime, end = d.EndTime;
            if (brief.Role == SceneRole.GoalMain && double.IsNaN(brief.NetContactTime))
                brief.NetContactTime = Highlight.Logic.NetContact.Find(d, brief.EventTime, _rig.HalfLength); // 058 FR-22
            GoalSequenceDirector.Plan(brief, start, end, BaseKind(brief, portrait), _t, shots);

            var track = new CameraTrack(start, d.Frames, _t.FovMinDeg, _t.FovMaxDeg);
            var solver = new FramingSolver(_rig, _t);
            var top = new TopViewCamera(_rig);
            var mine = new MyPlayerCamera();
            var shake = _settings.ShakeOff ? null : new CameraShake(brief.SceneId, start, end, CameraShake.FromEvents(d.Events, _t.HardShotSpeed, brief.Shakes));
            bool longPath = brief.LongPath && brief.Role == SceneRole.ReplayAngle && brief.AngleIndex == 0;
            if (longPath) track.SlowMoMark = brief.EventTime;

            var placements = new Dictionary<ShotKind, AnglePlacement>();
            int shotIndex = 0;
            CameraPose last = default;
            bool hasLast = false;

            for (int f = 0; f < d.Frames; f++)
            {
                double t = start + f / (double)HighlightData.FrameRate;
                while (shotIndex < shots.Count - 1 && t >= shots[shotIndex].End - 1e-6d) shotIndex++;
                var shot = shots[shotIndex];

                int count = FramingTargets.Collect(d, f, _targets);
                var mainPose = solver.Step(_targets, count);
                var ball = d.Ball[f];
                var pose = hasLast && count == 0 ? last : Pose(d, f, t, shot, brief, mainPose, ball, top, mine, placements);

                if (shake != null && CameraShake.Applies(shot.Kind)) pose = pose.WithShake(shake.At(t));
                track.Poses[f] = pose;
                track.Shots[f] = shot.Kind;
                track.CutAt[f] = f > 0 && track.Shots[f - 1] != shot.Kind;
                last = pose;
                hasLast = true;
            }

            LimitWithinShots(track, d);
            ApplyEntryBlend(track, brief, blendFrom);
            return track;
        }

        ShotKind BaseKind(SceneBrief b, bool portrait)
        {
            if (portrait) return ShotKind.TopView;
            switch (_settings.Mode)
            {
                case CameraMode.TopView: return ShotKind.TopView;
                case CameraMode.MyPlayer: return b.MyPlayerInvolved && b.MyPlayer >= 0 ? ShotKind.MyPlayer : ShotKind.Main;
                default: return ShotKind.Main;
            }
        }

        CameraPose Pose(HighlightData d, int f, double t, in ShotPlan shot, SceneBrief b, in CameraPose main, Vec3 ball,
            TopViewCamera top, MyPlayerCamera mine, Dictionary<ShotKind, AnglePlacement> placements)
        {
            switch (shot.Kind)
            {
                case ShotKind.TopView:
                    return top.Step(ball);
                case ShotKind.MyPlayer:
                    return Present(d, f, b.MyPlayer) ? mine.Step(FramingTargets.PosAt(d, f, b.MyPlayer), ball) : main;
                case ShotKind.Closeup:
                    return Follow(d, f, shot.Focus, ball, CloseupDistance, CloseupHeight, CloseupFovDeg);
                case ShotKind.Celebration:
                    return Follow(d, f, shot.Focus, ball, CelebrationDistance, CelebrationHeight, CelebrationFovDeg);
                case ShotKind.ReplayHigh:
                case ShotKind.ReplayShooter:
                case ShotKind.ReplayReverse:
                    return Replay(d, f, t, shot, b, ball, placements);
                case ShotKind.FallbackHigh:
                    return HighPath(b, ball);
                case ShotKind.ReactionWide:
                    return Wide(TeamCenter(d, f, shot.Focus, ball), ReactionFovDeg);
                case ShotKind.KickoffWide:
                    return Wide(new Vec3(0f, 0f, 0f), KickoffFovDeg);
                default:
                    return main;
            }
        }

        static bool Present(HighlightData d, int f, int e) =>
            e >= 0 && e < HighlightData.Players && d.Present[f * HighlightData.Entities + e];

        /// <summary>대상 앞쪽(메인 스탠드 방향)에서 따라간다. 180° 규칙을 지킨다 (FR-13).</summary>
        CameraPose Follow(HighlightData d, int f, int focus, Vec3 ball, float distance, float height, float fovDeg)
        {
            var target = Present(d, f, focus) ? FramingTargets.PosAt(d, f, focus) : new Vec3(ball.X, 0f, ball.Z);
            var pos = new Vec3(target.X, height, MathF.Max(target.Z - distance, NearSideLimit));
            var look = target + new Vec3(0f, HeadHeight * 0.8f, 0f);
            float tan = MathF.Tan(fovDeg * 0.5f * CameraMath.Deg2Rad);
            return new CameraPose(pos, (look - pos).Normalized(), tan, 0f);
        }

        CameraPose Replay(HighlightData d, int f, double t, in ShotPlan shot, SceneBrief b, Vec3 ball, Dictionary<ShotKind, AnglePlacement> placements)
        {
            if (!placements.TryGetValue(shot.Kind, out var place))
            {
                place = ReplayAnglePlacer.Place(shot.Kind, b.EventPos, _rig);
                placements[shot.Kind] = place;
            }

            var look = ball;
            if (shot.Kind == ShotKind.ReplayShooter && Present(d, f, shot.Focus))
            {
                // 슛 전에는 슈터를, 슛 뒤에는 공을 본다
                float w = CameraMath.Smoothstep((float)(b.EventTime - 0.2d), (float)(b.EventTime + 0.3d), (float)t);
                var shooter = FramingTargets.PosAt(d, f, shot.Focus) + new Vec3(0f, HeadHeight * 0.7f, 0f);
                look = shooter + (ball - shooter) * w;
            }

            var forward = (look - place.Position).Normalized();
            float tanV = MathF.Tan(ReplayFovDeg * 0.5f * CameraMath.Deg2Rad);
            float tanH = 0f;
            CameraMath.Basis(forward, out var right, out var up);
            var v = ball - place.Position;
            float z = Vec3.Dot(v, forward);
            if (z > 0.01f)
            {
                tanH = MathF.Abs(Vec3.Dot(v, right) / z) * ReplayMargin;
                tanV = MathF.Max(tanV, MathF.Abs(Vec3.Dot(v, up) / z) * ReplayMargin);
            }
            return new CameraPose(place.Position, forward, tanV, tanH);
        }

        CameraPose HighPath(SceneBrief b, Vec3 ball)
        {
            var pos = ReplayAnglePlacer.Fallback(b.EventPos, b.EventPos.X >= 0f ? 1f : -1f) + new Vec3(0f, 8f, 0f);
            var forward = (ball - pos).Normalized();
            float tan = MathF.Tan(40f * 0.5f * CameraMath.Deg2Rad);
            return new CameraPose(pos, forward, tan, 0f);
        }

        CameraPose Wide(Vec3 look, float fovDeg)
        {
            var pos = _rig.MainPosition(_t);
            float tan = MathF.Tan(fovDeg * 0.5f * CameraMath.Deg2Rad);
            return new CameraPose(pos, (look - pos).Normalized(), tan, 0f);
        }

        static Vec3 TeamCenter(HighlightData d, int f, int scorer, Vec3 ball)
        {
            if (scorer < 0 || scorer >= HighlightData.Players) return new Vec3(ball.X, 0f, ball.Z);
            int from = scorer < 11 ? 0 : 11;
            var sum = Vec3.Zero;
            int n = 0;
            for (int e = from; e < from + 11; e++)
            {
                if (e % 11 == 0 || !Present(d, f, e)) continue;
                sum += FramingTargets.PosAt(d, f, e);
                n++;
            }
            return n == 0 ? new Vec3(ball.X, 0f, ball.Z) : sum / n;
        }

        static bool IsReplay(ShotKind k) => k == ShotKind.ReplayHigh || k == ShotKind.ReplayShooter || k == ShotKind.ReplayReverse || k == ShotKind.FallbackHigh;

        /// <summary>
        /// 058 FR-30·33: 한 샷 안 시선 회전 각속도·각가속도·화각 변화를 재생 1x 기준 상한 안으로 묶는다 (리플레이는 0.5x로 재생되므로 시뮬 시간 상한을 그만큼 넓힌다).
        /// 남은 각도에 대한 제동 곡선(√(2·a·θ))으로 속도를 정해 멈출 때도 각가속도를 지킨다. 그 결과 공이 [제안] 0.5 s 넘게 화면 밖이면 진단 횟수를 센다.
        /// </summary>
        void LimitWithinShots(CameraTrack track, HighlightData d)
        {
            const float dt = 1f / HighlightData.FrameRate;
            float prevSpeed = 0f;
            int offscreen = 0, offscreenLimit = (int)Math.Ceiling(_t.BallOffscreenDiagSeconds * HighlightData.FrameRate);
            for (int f = 1; f < track.Frames; f++)
            {
                if (track.CutAt[f]) { prevSpeed = 0f; offscreen = 0; continue; }
                float rate = IsReplay(track.Shots[f]) ? PlaybackSettings.ReplayRate : 1f;
                float maxSpeed = _t.MaxAngularSpeed / rate, maxAccel = _t.MaxAngularAccel / (rate * rate), maxFov = _t.MaxFovSpeed / rate;
                var prev = track.Poses[f - 1];
                var cur = track.Poses[f];
                float dot = Vec3.Dot(prev.Forward, cur.Forward);
                dot = dot > 1f ? 1f : dot < -1f ? -1f : dot;
                float theta = MathF.Acos(dot) * CameraMath.Rad2Deg;
                // 이산 제동 곡선: 마지막 한 걸음의 감속도 a·dt 안에 들게 (v = √(2aθ + (a·dt/2)²) − a·dt/2), 여유 10%
                float a = maxAccel * 0.9f, half = a * dt * 0.5f;
                float target = MathF.Min(maxSpeed, MathF.Sqrt(2f * a * theta + half * half) - half);
                float speed = MathF.Max(prevSpeed - a * dt, MathF.Min(prevSpeed + a * dt, target));
                if (speed < 0f) speed = 0f;
                // 감속 한도를 지키려면 목표를 아주 조금(≤ a·dt²) 지나칠 수 있다 — 다음 프레임에 되돌아온다
                float step = speed * dt;
                var fwd = theta < 1e-4f ? cur.Forward : Slerp(prev.Forward, cur.Forward, theta, step);
                prevSpeed = step / dt;
                float fovPrev = 2f * MathF.Atan(prev.TanV) * CameraMath.Rad2Deg, fovCur = 2f * MathF.Atan(cur.TanV) * CameraMath.Rad2Deg;
                float df = fovCur - fovPrev, maxDf = maxFov * dt;
                float tanV = cur.TanV, tanH = cur.TanH;
                if (MathF.Abs(df) > maxDf)
                {
                    float fov = fovPrev + MathF.Sign(df) * maxDf;
                    tanV = MathF.Tan(fov * 0.5f / CameraMath.Rad2Deg);
                    tanH = cur.TanV > 1e-6f ? cur.TanH * tanV / cur.TanV : cur.TanH;
                }
                track.Poses[f] = new CameraPose(cur.Position, fwd, tanV, tanH, cur.Shake);
                var toBall = (d.Ball[f] - cur.Position).Normalized();
                bool inView = Vec3.Dot(toBall, fwd) > MathF.Cos(MathF.Atan(MathF.Max(tanV, tanH)));
                offscreen = inView ? 0 : offscreen + 1;
                if (offscreen == offscreenLimit) track.BallOffscreenDiagnostics++;
            }
        }

        static Vec3 Slerp(Vec3 a, Vec3 b, float thetaDeg, float stepDeg)
        {
            float th = thetaDeg / CameraMath.Rad2Deg, st = stepDeg / CameraMath.Rad2Deg;
            float s = MathF.Sin(th);
            if (s < 1e-5f) return b;
            return (a * (MathF.Sin(th - st) / s) + b * (MathF.Sin(st) / s)).Normalized();
        }

        /// <summary>리플레이 진입 0.3 s(재생 시간) 전환 (FR-14). 첫 앵글만 이전 장면 포즈에서 이어받는다.</summary>
        void ApplyEntryBlend(CameraTrack track, SceneBrief b, CameraPose? from)
        {
            if (from == null || b.Role != SceneRole.ReplayAngle || b.AngleIndex != 0 || _t.ReplayBlendSeconds <= 0d) return;
            double simSeconds = _t.ReplayBlendSeconds * PlaybackSettings.ReplayRate;
            int frames = Math.Min(track.Frames, (int)Math.Ceiling(simSeconds * HighlightData.FrameRate));
            var start = from.Value;
            for (int f = 0; f < frames; f++)
            {
                float t = CameraMath.Smoothstep(0f, 1f, (f + 1f) / (frames + 1f));
                track.Poses[f] = CameraPose.Lerp(start, track.Poses[f], t);
            }
        }
    }
}
