namespace RoadToPro.Presentation.Players.Motion
{
    /// <summary>재생 진단 수신 (specs/054 FR-70). 포즈 적용이 끝난 뒤 호출된다. 설정 안 하면 호출 0 (예산: 꺼짐 ≤ 0.02 ms).</summary>
    public interface IMotionDiagnostics
    {
        void OnApplied(float simTime);
    }
}
