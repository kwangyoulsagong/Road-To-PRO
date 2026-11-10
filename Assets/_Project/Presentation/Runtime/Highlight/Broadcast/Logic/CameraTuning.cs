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
            MainBehindMin = b.Get("camera.mainBehindMin"),
            MainBehindMax = b.Get("camera.mainBehindMax"),
            MainHeightMin = b.Get("camera.mainHeightMin"),
            MainHeightMax = b.Get("camera.mainHeightMax"),
        };
    }
}
