using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Tactics
{
    public enum SlotPosition : byte { GK = 0, CB = 1, FB = 2, DM = 3, CM = 4, AM = 5, W = 6, ST = 7 }

    /// <summary>포메이션 슬롯 (FR-10). 앵커는 블록 중심 기준 정규화 오프셋이고 <see cref="ShapeProfile"/> 순서로 6개다.</summary>
    public sealed class FormationSlot
    {
        public string Id;
        public SlotPosition Position;
        /// <summary>수비 시 최후방 라인에 속한다 (FR-40).</summary>
        public bool IsLine;
        public float ZoneWeight;
        public string RoleId;
        public Vec2[] Anchors;
    }

    /// <summary>인원 감소 변형 (FR-92): 필드 인원이 FieldPlayers일 때 뺄 슬롯.</summary>
    public sealed class ReducedVariant
    {
        public int FieldPlayers;
        public int[] RemovedSlots;
    }

    /// <summary>포메이션 정의. <see cref="Slots"/>[0]이 GK이고 슬롯 번호는 배열 인덱스다.</summary>
    public sealed class FormationDefinition
    {
        public const int SlotCount = 11;

        public string Id;
        public FormationSlot[] Slots;
        public ReducedVariant[] Reduced = System.Array.Empty<ReducedVariant>();
    }
}
