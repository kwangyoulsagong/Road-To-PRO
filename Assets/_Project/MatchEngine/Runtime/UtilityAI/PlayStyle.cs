using System;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 플레이 스타일 대체 입력 (specs/010 FR-80~85, 003 전). 역할 자유도·역할 오프볼 가중치·성향 4축(0~100)·버릇 레벨 15개.
    /// 003이 구현되면 003 역할·성향·버릇 데이터가 결합 배수 캐시를 채운다. 경기 입력이고 틱 경로에서 바꾸지 않는다.
    /// </summary>
    public sealed class PlayStyle
    {
        /// <summary>역할 자유도 0(앵커: CB 앞 고정) ~ 1(트레콸르티스타: 자유롭게 떠다님). 이탈 상한·형태 유지 상대 점수 (FR-85).</summary>
        public float Freedom = 0.5f;
        /// <summary>역할 오프볼 가중치 (고려 항목별, 기본 1).</summary>
        public readonly float[] RoleWeights = new float[Considerations.Count];
        /// <summary>성향 "침투" 발밑 선호 0 ↔ 라인 뒤 계속 침투 100.</summary>
        public float Runs = 50f;
        /// <summary>성향 "오버래핑" 뒤에 남음 0 ↔ 항상 올라감 100.</summary>
        public float Overlapping = 50f;
        /// <summary>성향 "방향" 측면 고수 0 ↔ 안쪽 컷인 100.</summary>
        public float Direction = 50f;
        /// <summary>성향 "압박 적극성" 자리 지킴 0 ↔ 끝까지 쫓아감 100.</summary>
        public float PressAggression = 50f;
        /// <summary>버릇 레벨 0/1/2 (<see cref="Habit"/> 순서).</summary>
        public readonly byte[] HabitLevels = new byte[HabitTable.Count];

        public PlayStyle()
        {
            for (int i = 0; i < RoleWeights.Length; i++) RoleWeights[i] = 1f;
        }

        public static PlayStyle Neutral() => new PlayStyle();

        public PlayStyle WithHabit(Habit h, int level)
        {
            if (level < 0 || level > 2) throw new ArgumentOutOfRangeException(nameof(level), "버릇 레벨은 0~2");
            HabitLevels[(int)h] = (byte)level;
            return this;
        }

        public PlayStyle WithRole(Consideration k, float weight)
        {
            if (!(weight >= 0f)) throw new ArgumentOutOfRangeException(nameof(weight));
            RoleWeights[(int)k] = weight;
            return this;
        }

        /// <summary>앵커 (기획서 4.2.1: CB 앞 고정, 패스 길목 차단 많이 / 전진·박스 침투 적게).</summary>
        public static PlayStyle Anchor()
        {
            var s = new PlayStyle { Freedom = 0f };
            s.RoleWeights[(int)Consideration.PassLaneBlock] = 1.4f;
            s.RoleWeights[(int)Consideration.RunInBehind] = 0.5f;
            s.RoleWeights[(int)Consideration.BoxCrash] = 0.5f;
            return s;
        }

        /// <summary>트레콸르티스타 (자유롭게 떠다님, 압박·수비 복귀 적게).</summary>
        public static PlayStyle Trequartista()
        {
            var s = new PlayStyle { Freedom = 1f };
            s.RoleWeights[(int)Consideration.Press] = 0.6f;
            s.RoleWeights[(int)Consideration.RecoveryRun] = 0.6f;
            s.RoleWeights[(int)Consideration.Support] = 1.2f;
            return s;
        }

        /// <summary>전통 윙어 (터치라인 고수).</summary>
        public static PlayStyle Winger()
        {
            var s = new PlayStyle { Freedom = 0.5f, Direction = 20f };
            s.RoleWeights[(int)Consideration.StayWide] = 1.4f;
            return s;
        }

        /// <summary>포처 (마지막 수비 라인 위, 침투 많이 / 내려오기·압박 적게).</summary>
        public static PlayStyle Poacher()
        {
            var s = new PlayStyle { Freedom = 0.5f, Runs = 80f };
            s.RoleWeights[(int)Consideration.DropToReceive] = 0.6f;
            s.RoleWeights[(int)Consideration.Press] = 0.7f;
            return s;
        }

        /// <summary>윙백 (측면 끝까지 높이, 오버래핑 많이).</summary>
        public static PlayStyle WingBack()
        {
            var s = new PlayStyle { Freedom = 0.4f, Overlapping = 80f, Direction = 25f };
            s.RoleWeights[(int)Consideration.Overlap] = 1.3f;
            return s;
        }
    }
}
