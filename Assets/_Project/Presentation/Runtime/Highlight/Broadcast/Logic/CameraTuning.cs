using RoadToPro.Core.Tuning;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>Data/tuning/camera.json 값 묶음 (specs/042).</summary>
    public sealed class CameraTuning
    {
        public float FovMinDeg = 8f, FovMaxDeg = 30f, PanLimitDeg = 75f, TiltMaxDeg = 50f;
        public float LookDamp = 0.7f, ZoomDamp = 1f, FramingMargin = 1.15f;
        public float WideFovDeg = 24f, BoxFovDeg = 14f, BoxZoomStartX = 20f, BoxZoomEndX = 36f;
        public double MinShotSeconds = 2.5d, GoalCloseupSeconds = 2.5d, ReactionSeconds = 2.5d, ReplayBlendSeconds = 0.3d;
        public float HardShotSpeed = 30f;
        /// <summary>골 메인 샷 유지 (059 FR-30·31, 058 FR-22).</summary>
        public double NetHoldSeconds = 1d, NoNetHoldSeconds = 0.5d, GoalHoldMaxSeconds = 1.5d;
        /// <summary>샷 안 카메라 변화 상한, 재생 1x 기준 (058 FR-30·33).</summary>
        public float MaxAngularSpeed = 60f, MaxAngularAccel = 240f, MaxFovSpeed = 10f;
        public double BallOffscreenDiagSeconds = 0.5d;
        public float MainBehindMin = 30f, MainBehindMax = 45f, MainHeightMin = 15f, MainHeightMax = 25f;

        public static CameraTuning From(TuningBundle b) => new CameraTuning
        {
            FovMinDeg = b.Get("camera.fovMinDeg"),
            FovMaxDeg = b.Get("camera.fovMaxDeg"),
            PanLimitDeg = b.Get("camera.panLimitDeg"),
            TiltMaxDeg = b.Get("camera.tiltMaxDeg"),
            LookDamp = b.Get("camera.lookDampSeconds"),
            ZoomDamp = b.Get("camera.zoomDampSeconds"),
            FramingMargin = b.Get("camera.framingMargin"),
            WideFovDeg = b.Get("camera.wideFovDeg"),
            BoxFovDeg = b.Get("camera.boxFovDeg"),
            BoxZoomStartX = b.Get("camera.boxZoomStartX"),
            BoxZoomEndX = b.Get("camera.boxZoomEndX"),
            MinShotSeconds = b.Get("camera.minShotSeconds"),
            GoalCloseupSeconds = b.Get("camera.goalCloseupSeconds"),
            ReactionSeconds = b.Get("camera.reactionSeconds"),
            ReplayBlendSeconds = b.Get("camera.replayBlendSeconds"),
            HardShotSpeed = b.Get("camera.hardShotSpeed"),
            NetHoldSeconds = b.Get("camera.netHoldSeconds"),
            NoNetHoldSeconds = b.Get("camera.noNetHoldSeconds"),
            GoalHoldMaxSeconds = b.Get("camera.goalHoldMaxSeconds"),
            MaxAngularSpeed = b.Get("camera.maxAngularSpeed"),
            MaxAngularAccel = b.Get("camera.maxAngularAccel"),
            MaxFovSpeed = b.Get("camera.maxFovSpeed"),
            BallOffscreenDiagSeconds = b.Get("camera.ballOffscreenDiagSeconds"),
            MainBehindMin = b.Get("camera.mainBehindMin"),
            MainBehindMax = b.Get("camera.mainBehindMax"),
            MainHeightMin = b.Get("camera.mainHeightMin"),
            MainHeightMax = b.Get("camera.mainHeightMax"),
        };
    }
}
