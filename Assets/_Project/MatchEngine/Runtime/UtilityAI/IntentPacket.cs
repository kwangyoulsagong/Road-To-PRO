using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 시나리오 의도 패킷 (specs/010 FR-91): 배역 슬롯·행동 종류·목표 지점·타이밍 창·다음 패스 대상 힌트·단계.
    /// 013 개정 전에는 <see cref="ScenarioIntentAdapter"/>가 시나리오 런타임 상태로 만든다.
    /// </summary>
    public struct IntentPacket
    {
        public bool Has;
        /// <summary>단계 식별 (배역·단계가 바뀌면 값이 바뀐다 — 준수 굴림 1회 기준, FR-93).</summary>
        public int StepKey;
        public int Role;
        public Consideration Kind;
        public Vec2 Point;
        public bool InWindow;
        /// <summary>추적·압박 대상 슬롯 (없으면 −1).</summary>
        public int TargetSlot;
        /// <summary>다음 패스 대상 힌트 (없으면 −1).</summary>
        public int NextPassTarget;

        public static IntentPacket None => new IntentPacket { TargetSlot = -1, NextPassTarget = -1, StepKey = -1, Role = -1 };
    }
}
