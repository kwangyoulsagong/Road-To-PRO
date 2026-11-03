using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Match
{
    /// <summary>조건 실행의 조건 (FR-70): 이벤트 종류·선수·구역(정규화 사각형). 값 타입 — 람다 할당 없음.</summary>
    public readonly struct EventCondition
    {
        public readonly SimEventKind Kind;
        /// <summary>−1이면 아무 선수.</summary>
        public readonly int PlayerId;
        public readonly bool HasZone;
        public readonly float MinX, MinY, MaxX, MaxY;

        public EventCondition(SimEventKind kind, int playerId = -1)
        {
            Kind = kind;
            PlayerId = playerId;
            HasZone = false;
            MinX = MinY = MaxX = MaxY = 0f;
        }

        public EventCondition(SimEventKind kind, int playerId, float minX, float minY, float maxX, float maxY)
        {
            Kind = kind;
            PlayerId = playerId;
            HasZone = true;
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        }

        public bool Matches(in SimEvent e)
        {
            if (e.Kind != Kind) return false;
            if (PlayerId >= 0 && e.PlayerId != PlayerId) return false;
            if (HasZone && (e.X < MinX || e.X > MaxX || e.Y < MinY || e.Y > MaxY)) return false;
            return true;
        }
    }

    /// <summary>
    /// 실행 제어 (specs/006 FR-70): 한 틱·N틱·지정 시각까지·조건 만족까지·끝까지. 일시정지는 부르지 않는 것이고, 그동안
    /// <see cref="MatchSim.View"/>로 상태를 조회한다. 어느 단위로 돌려도 결과는 같다 (AC-13).
    /// </summary>
    public static class MatchRunner
    {
        public static bool Step(MatchSim sim) => sim.StepTick();

        public static void StepTicks(MatchSim sim, long n) => sim.StepTicks(n);

        public static void RunUntilTick(MatchSim sim, long tick) => sim.RunUntilTick(tick);

        /// <summary>
        /// 조건에 맞는 이벤트가 로그에 들어간 틱까지 돈다. 맞은 이벤트의 로그 인덱스(없이 끝나면 −1).
        /// </summary>
        public static int RunUntil(MatchSim sim, in EventCondition cond)
        {
            var log = sim.Log;
            var seen = log.Count;
            while (sim.StepTick())
            {
                for (int i = seen; i < log.Count; i++)
                    if (cond.Matches(in log[i])) return i;
                seen = log.Count;
            }
            return -1;
        }

        public static Result.MatchResult RunToEnd(MatchSim sim) => sim.RunToEnd();
    }
}
