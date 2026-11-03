using RoadToPro.MatchEngine.Match.Replay;

namespace RoadToPro.MatchEngine.Match.Diagnostics
{
    /// <summary>
    /// FR-83 100 Hz 원본 진단 출력 (054 FR-52 요청). 검증 모드에서만 설정한다. null이면 호출 0.
    /// 서브스텝마다 직전(pre)·직후(post) 상태를 준다 — 30 Hz 기록은 이 둘을 보간한 값이라 그대로 재현할 수 있다 (AC-24).
    /// 받은 프레임은 재사용 버퍼다 — 보관하려면 복사한다. 켜고 꺼도 시뮬 결과는 같다.
    /// </summary>
    public interface IRawTrajectorySink
    {
        void OnRawSubstep(long substep, ReplayFrame pre, ReplayFrame post);
    }
}
