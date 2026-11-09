using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>트리거 평가 입력 (FR-10). 좌표는 시나리오를 고르는 팀 관점 정규화.</summary>
    public struct TriggerContext
    {
        public Vec2 Ball;
        public ushort PhaseBit;
        public bool PressActive;
        public BallStateMask BallState;
        public PositionMask CarrierRole, ReceiverRole;
        public OppBlockMask OppBlock;
        public TriggerEventMask Events;
        public bool ReceiverFacingSideline;
        /// <summary><see cref="SpaceField"/> 순서의 값.</summary>
        public float[] Space;
        public int ScoreDiff;
        public float Minute;
        /// <summary>상대 마킹 방식 비트 (1 &lt;&lt; MarkingScheme).</summary>
        public byte OppMarkingBit;
    }

    /// <summary>트리거 술어 (FR-10). 틱 경로라 할당이 없다.</summary>
    public static class TriggerEvaluator
    {
        public static bool Passes(ScenarioDefinition def, in TriggerContext c, ZoneMap zones)
        {
            var t = def.Trigger;
            if (t.CellMask != 0 && (t.CellMask & (1u << ZoneMap.CellOf(c.Ball, false))) == 0) return false;
            if ((t.PhaseMask & c.PhaseBit) == 0) return false;
            if (t.RequirePress && !c.PressActive) return false;
            if ((t.BallState & c.BallState) == 0) return false;
            if (t.CarrierRole != PositionMask.None && (t.CarrierRole & c.CarrierRole) == 0) return false;
            if (t.ReceiverRole != PositionMask.None && (t.ReceiverRole & c.ReceiverRole) == 0) return false;
            if (t.OppBlock != OppBlockMask.None && (t.OppBlock & c.OppBlock) == 0) return false;
            if (t.Event != TriggerEventMask.None && (t.Event & c.Events) == 0) return false;
            if (t.ReceiverFacingSideline && !c.ReceiverFacingSideline) return false;
            if (t.OppMarkingMask != 0 && (t.OppMarkingMask & c.OppMarkingBit) == 0) return false;
            if (c.ScoreDiff < t.ScoreDiffMin || c.ScoreDiff > t.ScoreDiffMax) return false;
            if (c.Minute < t.MinuteMin || c.Minute > t.MinuteMax) return false;

            var groups = t.RegionGroups;
            for (int g = 0; g < groups.Length; g++)
            {
                var any = false;
                var rs = groups[g];
                for (int i = 0; i < rs.Length && !any; i++) any = zones.Contains(rs[i], c.Ball, def.Mirror);
                if (!any) return false;
            }

            var space = t.Space;
            for (int i = 0; i < space.Length; i++)
                if (!Compare(c.Space[(int)space[i].Field], space[i].Op, space[i].Value)) return false;
            return true;
        }

        static bool Compare(float a, CompareOp op, float b)
        {
            switch (op)
            {
                case CompareOp.Ge: return a >= b;
                case CompareOp.Le: return a <= b;
                case CompareOp.Gt: return a > b;
                case CompareOp.Lt: return a < b;
                default: return a == b;
            }
        }
    }
}
