using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match.Flow;

namespace RoadToPro.MatchEngine.Match
{
    /// <summary>읽기 전용 경기 조회 (FR-35·70). 구독자·프레젠테이션·테스트가 쓴다 — 바꾸는 멤버가 없다.</summary>
    public interface IMatchView
    {
        long Tick { get; }
        int Period { get; }
        FlowState State { get; }
        MatchTime Time { get; }
        int Score(int team);
        bool IsSlotActive(int slot);
        int PlayerAtSlot(int slot);
        PlayerMotionState SlotState(int slot);
        BallState Ball { get; }
        int AttackDir(int team);
    }

    /// <summary><see cref="IMatchView"/> 구현. 컨텍스트를 감싸 값 복사본만 돌려준다.</summary>
    internal sealed class MatchView : IMatchView
    {
        readonly MatchContext _c;
        internal MatchView(MatchContext c) => _c = c;
        public long Tick => _c.Tick;
        public int Period => _c.Flow.Period;
        public FlowState State => _c.Flow.State;
        public MatchTime Time => new MatchTime((MatchPeriod)_c.Flow.Period, _c.Tick < _c.Flow.PeriodStartTick ? 0 : _c.Tick - _c.Flow.PeriodStartTick);
        public int Score(int team) => _c.Flow.Goals(team);
        public bool IsSlotActive(int slot) => _c.IsSlotActive(slot);
        public int PlayerAtSlot(int slot) => _c.Squad.PlayerAt(slot);
        public PlayerMotionState SlotState(int slot) => _c.Kinematics.State(slot);
        public BallState Ball => _c.Ball;
        public int AttackDir(int team) => _c.AttackDir(team);
    }
}
