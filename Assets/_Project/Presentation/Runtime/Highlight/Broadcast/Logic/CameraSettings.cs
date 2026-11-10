namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>사용자 카메라 설정 (specs/042 FR-30·42). 저장은 022·038이 연결한다.</summary>
    public sealed class CameraSettings
    {
        public CameraMode Mode = CameraMode.Broadcast;
        /// <summary>흔들림 끄기. 기본값은 꺼짐(흔들림 있음).</summary>
        public bool ShakeOff;
    }
}
