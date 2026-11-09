using System;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>공간·수 조건 하나 (FR-10).</summary>
    public struct SpaceCondition
    {
        public SpaceField Field;
        public CompareOp Op;
        public float Value;
    }

    /// <summary>컴파일된 트리거. 문자열 없이 비트와 숫자만 — 틱 경로에서 평가한다.</summary>
    public sealed class ScenarioTrigger
    {
        /// <summary>18존 사전 필터 비트 (보수적: 세부 구역과 겹치는 칸 전부). 0이면 제한 없음.</summary>
        public uint CellMask;
        /// <summary>세부 구역 묶음. 묶음 안은 OR, 묶음끼리는 AND.</summary>
        public int[][] RegionGroups = Array.Empty<int[]>();
        public ushort PhaseMask = PhaseBits.All;
        public bool RequirePress;
        public BallStateMask BallState = BallStateMask.Any;
        public PositionMask CarrierRole = PositionMask.None;
        public PositionMask ReceiverRole = PositionMask.None;
        public OppBlockMask OppBlock = OppBlockMask.None;
        public TriggerEventMask Event = TriggerEventMask.None;
        public bool ReceiverFacingSideline;
        public SpaceCondition[] Space = Array.Empty<SpaceCondition>();
        public int ScoreDiffMin = int.MinValue, ScoreDiffMax = int.MaxValue;
        public float MinuteMin = float.NegativeInfinity, MinuteMax = float.PositiveInfinity;
        /// <summary>0이면 제한 없음. 비트 = <see cref="Tactics.MarkingScheme"/> 값.</summary>
        public byte OppMarkingMask;
    }

    public enum StepTargetKind : byte { None = 0, Region = 1, Role = 2, Receiver = 3, OppPosition = 4 }

    /// <summary>배역의 단계 하나 (FR-02: 순서 + 선택적 상대 시각).</summary>
    public sealed class ScenarioStep
    {
        public ScenarioVerb Verb;
        public VerbClass Class;
        public bool HasAlt;
        public ScenarioVerb Alt;
        /// <summary>상대 시작 시각(초). NaN이면 앞 단계가 끝나는 대로.</summary>
        public float T = float.NaN;
        public StepTargetKind TargetKind;
        /// <summary>구역 번호 또는 배역 번호.</summary>
        public int TargetRef = -1;
        public PositionMask OppPositions;
        /// <summary>시작 전 대기 구역 (세트피스 배치, 없으면 −1).</summary>
        public int FromRegion = -1;
        public float DistanceM;
        /// <summary>-1 안쪽, +1 바깥쪽, 0 전방.</summary>
        public sbyte Direction;
        public int LaneRegion = -1;
    }

    public sealed class ScenarioRole
    {
        public string Name;
        public bool Required = true;
        public bool Opponent;
        public bool IsCarrier;
        public bool IsTaker;
        public PositionMask Positions = PositionMask.AnyField;
        public RoleSideFilter Side;
        public ScenarioStep[] Steps = Array.Empty<ScenarioStep>();
    }

    public enum BranchTargetKind : byte { Next = 0, Fallback = 1, Loss = 2, Chain = 3, JumpStep = 4 }

    /// <summary>분기 목적지.</summary>
    public sealed class BranchTarget
    {
        public BranchTargetKind Kind;
        public string[] Ids = Array.Empty<string>();
        public float[] Weights = Array.Empty<float>();
        /// <summary>해석된 정의 번호 (로드되지 않은 대상은 −1 → 폴백).</summary>
        public int[] Ordinals = Array.Empty<int>();
        public int JumpRole = -1, JumpStep = -1;
    }

    public struct BranchAlt
    {
        public BranchCondition Condition;
        public BranchTarget Target;
    }

    /// <summary>분기 규칙: 단계 종료(`at`) 또는 상황(`when`) (FR-42).</summary>
    public sealed class ScenarioBranch
    {
        /// <summary>단계 종료 분기의 배역·단계 (0부터). 상황 분기는 −1.</summary>
        public int Role = -1, Step = -1;
        public bool HasSituation;
        public BranchCondition Situation;
        public BranchTarget Success, Fail, SituationTarget;
        public BranchAlt[] Alts = Array.Empty<BranchAlt>();
    }

    /// <summary>시나리오 정의 (FR-01). 로딩 뒤 불변.</summary>
    public sealed class ScenarioDefinition
    {
        public string Id, NameKo, NameEn, Group, BasePattern, Status, SourceFile;
        public int Line;
        public ScenarioCategory Category;
        public ScenarioSide Side;
        public bool Mirror;
        public bool Finishing;
        public ScenarioTrigger Trigger = new ScenarioTrigger();
        public bool HasGate;
        public float Lambda = 1f, CooldownS;
        public int MaxPerMatch = int.MaxValue;
        public ScenarioRole[] Cast = Array.Empty<ScenarioRole>();
        public ScenarioBranch[] Branches = Array.Empty<ScenarioBranch>();
        public string[] Kpi = Array.Empty<string>();
        public string[] VariationAxes = Array.Empty<string>();
        /// <summary>선호 특성: 배역 이름 → 특성 목록 (FR-21). 평행 배열.</summary>
        public string[] FavoredRoles = Array.Empty<string>();
        public string[][] FavoredTraits = Array.Empty<string[]>();
        public string[] CarrierTraitsBonus = Array.Empty<string>();
        public bool HasSuccessRate, HasXg, HasDuration;
        public float SuccessRate, Xg, DurationS;
        /// <summary>시나리오 지속 상한(초). 0이면 튜닝 기본값.</summary>
        public float TimeLimitS;

        /// <summary>id 정렬 뒤 번호 (<see cref="ScenarioLibrary"/>가 채움).</summary>
        public int Ordinal = -1;
        public int BasePatternIndex = -1;
        public bool IsSetPiece => Category == ScenarioCategory.SP;
    }

    /// <summary>포지셔널 플레이 수식자 (FR-05).</summary>
    public sealed class ScenarioModifier
    {
        public string Id;
        public uint CategoryMask;
        public ushort PhaseMask = PhaseBits.All;
        public float Weight = 1f;
    }
}
