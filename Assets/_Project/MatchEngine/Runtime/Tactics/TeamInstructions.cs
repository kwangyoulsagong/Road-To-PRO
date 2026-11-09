namespace RoadToPro.MatchEngine.Tactics
{
    public enum PressLineLevel : byte { Low = 0, Mid = 1, High = 2 }
    public enum OnLossMode : byte { CounterPress = 0, Regroup = 1, Mixed = 2 }
    public enum MarkingScheme : byte { Zonal = 0, Man = 1, Hybrid = 2 }

    /// <summary>지시 항목 번호 (FR-70·71). 전술 변경 입력과 경고 이벤트가 쓴다.</summary>
    public enum InstructionId : byte
    {
        LineHeight = 0, WidthIn = 1, WidthOut = 2, Compactness = 3, Tempo = 4, Directness = 5,
        PressIntensity = 6, OffsideTrap = 7, TimeWasting = 8, Risk = 9,
        PressLine = 10, OnLoss = 11, Marking = 12,
    }

    /// <summary>팀 지시 (FR-70, 리서치 03 A6). 연속값은 0~1이다. Tempo·Directness·Risk·TimeWasting은 010이 소비한다.</summary>
    public struct TeamInstructions
    {
        public const int Count = 13;

        public float LineHeight, WidthIn, WidthOut, Compactness, Tempo, Directness, PressIntensity, OffsideTrap, TimeWasting, Risk;
        public PressLineLevel PressLine;
        public OnLossMode OnLoss;
        public MarkingScheme Marking;

        public static TeamInstructions Default => new TeamInstructions
        {
            LineHeight = 0.5f, WidthIn = 0.5f, WidthOut = 0.5f, Compactness = 0.5f, Tempo = 0.5f, Directness = 0.5f,
            PressIntensity = 0.5f, OffsideTrap = 0.5f, TimeWasting = 0f, Risk = 0.5f,
            PressLine = PressLineLevel.Mid, OnLoss = OnLossMode.CounterPress, Marking = MarkingScheme.Zonal,
        };

        public float Get(InstructionId id) => id switch
        {
            InstructionId.LineHeight => LineHeight,
            InstructionId.WidthIn => WidthIn,
            InstructionId.WidthOut => WidthOut,
            InstructionId.Compactness => Compactness,
            InstructionId.Tempo => Tempo,
            InstructionId.Directness => Directness,
            InstructionId.PressIntensity => PressIntensity,
            InstructionId.OffsideTrap => OffsideTrap,
            InstructionId.TimeWasting => TimeWasting,
            InstructionId.Risk => Risk,
            InstructionId.PressLine => (float)PressLine,
            InstructionId.OnLoss => (float)OnLoss,
            _ => (float)Marking,
        };

        public void Set(InstructionId id, float v)
        {
            switch (id)
            {
                case InstructionId.LineHeight: LineHeight = v; break;
                case InstructionId.WidthIn: WidthIn = v; break;
                case InstructionId.WidthOut: WidthOut = v; break;
                case InstructionId.Compactness: Compactness = v; break;
                case InstructionId.Tempo: Tempo = v; break;
                case InstructionId.Directness: Directness = v; break;
                case InstructionId.PressIntensity: PressIntensity = v; break;
                case InstructionId.OffsideTrap: OffsideTrap = v; break;
                case InstructionId.TimeWasting: TimeWasting = v; break;
                case InstructionId.Risk: Risk = v; break;
                case InstructionId.PressLine: PressLine = (PressLineLevel)ToByte(v); break;
                case InstructionId.OnLoss: OnLoss = (OnLossMode)ToByte(v); break;
                case InstructionId.Marking: Marking = (MarkingScheme)ToByte(v); break;
            }
        }

        static byte ToByte(float v) => v >= 0f && v <= 200f ? (byte)v : (byte)255; // 범위 밖은 Sanitize가 고친다

        /// <summary>범위 밖 값을 고치고, 고친 항목의 비트 묶음을 돌려준다 (FR-70, AC-14). NaN은 경계가 없어 0.5(중립)로 둔다.</summary>
        public int Sanitize()
        {
            int mask = 0;
            for (int i = 0; i < (int)InstructionId.PressLine; i++)
            {
                var id = (InstructionId)i;
                var v = Get(id);
                if (v >= 0f && v <= 1f) continue;
                Set(id, float.IsNaN(v) ? 0.5f : (v < 0f ? 0f : 1f));
                mask |= 1 << i;
            }
            if ((byte)PressLine > (byte)PressLineLevel.High) { PressLine = PressLineLevel.Mid; mask |= 1 << (int)InstructionId.PressLine; }
            if ((byte)OnLoss > (byte)OnLossMode.Mixed) { OnLoss = OnLossMode.CounterPress; mask |= 1 << (int)InstructionId.OnLoss; }
            if ((byte)Marking > (byte)MarkingScheme.Hybrid) { Marking = MarkingScheme.Zonal; mask |= 1 << (int)InstructionId.Marking; }
            return mask;
        }
    }

    /// <summary>팀 전술 입력 (FR-02). <see cref="ManMarkTargets"/>는 포메이션 슬롯 번호 → 상대 팀 내 슬롯(0~10), −1이면 지역 방어.</summary>
    public sealed class TeamTactics
    {
        public string FormationId { get; set; } = "4-4-2";
        public TeamInstructions Instructions { get; set; } = TeamInstructions.Default;
        public int[] ManMarkTargets { get; set; }

        public TeamTactics Clone() => new TeamTactics
        {
            FormationId = FormationId, Instructions = Instructions, ManMarkTargets = (int[])ManMarkTargets?.Clone(),
        };
    }
}
