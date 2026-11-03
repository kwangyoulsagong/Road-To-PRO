namespace RoadToPro.MatchEngine.Match.Events
{
    /// <summary>
    /// 이벤트 구독자 (FR-35). 읽기 전용: 이벤트는 in 복사본, 시뮬은 <see cref="IMatchView"/>로만 본다.
    /// 예외를 던져도 시뮬은 계속된다 (발행기가 잡아 센다, AC-18).
    /// </summary>
    public interface ISimEventSubscriber
    {
        void OnEvent(in SimEvent e, IMatchView view);
    }
}
