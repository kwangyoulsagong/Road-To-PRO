namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 이동 강도 (specs/010 FR-70~72). 행동별 기본 강도 → 거리 기준(형태 유지 근거리는 조깅 이하, 아주 가까우면 걷기) → 질주 허용(피로·WorkRate)
    /// → 결정 틱당 한 단계(트리거 시 두 단계). 결과는 최고 속도 비로 바꿔 이동 명령의 속도 상한으로 넘긴다("최대 속도" 기본값에 기대지 않음).
    /// </summary>
    public static class IntensityPicker
    {
        public static Intensity Base(Consideration k, bool sprintPress)
        {
            switch (k)
            {
                case Consideration.RunInBehind:
                case Consideration.RecoveryRun:
                case Consideration.LooseBall: return Intensity.Sprint;
                case Consideration.Press:
                case Consideration.CoverShadow: return sprintPress ? Intensity.Sprint : Intensity.Run; // FR-100: 강한 압박이면 1·2·3순위 압박 질주
                case Consideration.Overlap:
                case Consideration.Underlap:
                case Consideration.BoxCrash:
                case Consideration.Support:
                case Consideration.DropToReceive: return Intensity.Run;
                default: return Intensity.Jog;
            }
        }

        static bool IsShapeLike(Consideration k) =>
            k == Consideration.HoldShape || k == Consideration.LineHold || k == Consideration.StayWide || k == Consideration.Invert
            || k == Consideration.Mark || k == Consideration.Cover || k == Consideration.PassLaneBlock;

        /// <summary>질주 허용 (FR-71·84): C·(1 + 2C) &lt; cap + capWorkRate·s_WR.</summary>
        public static bool SprintAllowed(DecisionParams p, float chronicC, float sWorkRate) =>
            chronicC * (1f + 2f * chronicC) < p.SprintCap + p.SprintCapWorkRate * sWorkRate;

        /// <param name="sprintPress">게겐프레싱 창 안이거나 팀 압박 강도 ≥ dec.press.sprintAt (FR-100·101)</param>
        public static Intensity Pick(DecisionParams p, Consideration k, float distance, float chronicC, float sWorkRate, bool sprintPress, Intensity previous, bool trigger, bool defending = false)
        {
            var want = Base(k, sprintPress);
            if (IsShapeLike(k))
            {
                if (distance < (defending ? p.JogDistDefend : p.JogDist)) want = Intensity.Jog;
                else if (want < Intensity.Run) want = Intensity.Run;
            }
            if (distance < p.WalkDist && k != Consideration.Press && k != Consideration.LooseBall) want = Intensity.Walk;
            if (want == Intensity.Sprint && !SprintAllowed(p, chronicC, sWorkRate)) want = Intensity.Run;

            var steps = trigger ? (int)p.TriggerSteps : 1;
            var d = (int)want - (int)previous;
            if (d > steps) d = steps;
            if (d < -steps) d = -steps;
            return (Intensity)((int)previous + d);
        }

        public static float SpeedRatio(DecisionParams p, Intensity i)
        {
            switch (i)
            {
                case Intensity.Walk: return p.WalkRatio;
                case Intensity.Jog: return p.JogRatio;
                case Intensity.Run: return p.RunRatio;
                default: return p.SprintRatio;
            }
        }
    }
}
