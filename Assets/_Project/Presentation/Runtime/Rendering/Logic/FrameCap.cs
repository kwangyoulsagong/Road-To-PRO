namespace RoadToPro.Presentation.Rendering.Logic
{
    /// <summary>렌더 관점의 앱 상태 (specs/048 FR-10~17).</summary>
    public enum RenderState
    {
        /// <summary>메뉴·매니지먼트 (P1은 30fps 상한만, 유휴 간격은 후속).</summary>
        Menu,
        /// <summary>하이라이트 장면 재생.</summary>
        Highlight,
        /// <summary>장면 사이 텍스트 타임라인 구간: 3D 렌더 정지, 마지막 프레임 고정.</summary>
        Timeline,
    }

    /// <summary>상태별 렌더 결정.</summary>
    public readonly struct FrameDecision
    {
        public readonly int TargetFps;
        public readonly bool Render3D;
        public readonly bool FreezeLastFrame;

        public FrameDecision(int targetFps, bool render3D, bool freezeLastFrame)
        {
            TargetFps = targetFps;
            Render3D = render3D;
            FreezeLastFrame = freezeLastFrame;
        }
    }

    /// <summary>
    /// 목표 fps·3D 렌더 여부 (specs/048 FR-12 하이라이트 60, FR-13 배터리 절약 30, FR-14 상한, FR-17 타임라인 정지).
    /// 배터리 절약은 사용자가 켤 때만 (자동 없음).
    /// </summary>
    public static class FrameCap
    {
        public const int MenuFps = 30;
        public const int HighlightFps = 60;
        public const int BatteryFps = 30;

        public static FrameDecision Decide(RenderState state, bool batterySaver)
        {
            int highlight = batterySaver ? BatteryFps : HighlightFps;
            switch (state)
            {
                case RenderState.Highlight: return new FrameDecision(highlight, true, false);
                // 타임라인 동안에도 상한은 하이라이트 값: 재개 1프레임 전 신호가 늦지 않게 (FR-17)
                case RenderState.Timeline: return new FrameDecision(highlight, false, true);
                default: return new FrameDecision(MenuFps, false, false);
            }
        }
    }
}
