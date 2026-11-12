namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>버릇 15종 (기획서 4.2.1 버릇 표 순서). 번호는 데이터에 들어가므로 바꾸지 않는다.</summary>
    public enum Habit : byte
    {
        DribbleAddict = 0,
        CrossMachine = 1,
        LongShotSpammer = 2,
        CutInMaster = 3,
        LineBreaker = 4,
        TempoDictator = 5,
        OneTouchMaster = 6,
        KillerPass = 7,
        SlideMaster = 8,
        BoxCrasher = 9,
        Altruist = 10,
        Egoist = 11,
        Showman = 12,
        Diver = 13,
        BigMatch = 14,
    }

    /// <summary>버릇 하나가 고려 항목 하나에 거는 점수 배수.</summary>
    public readonly struct HabitEffect
    {
        public readonly Consideration Kind;
        public readonly float Multiplier;

        public HabitEffect(Consideration kind, float multiplier)
        {
            Kind = kind;
            Multiplier = multiplier;
        }
    }

    /// <summary>
    /// 버릇 → 점수 배수 (기획서 4.2.1 "버릇" 표 효과 열, [설계값]). 003 전 대체 — 003 FR-20이 소유하고 구현되면 003 데이터로 바뀐다.
    /// 점수 배수가 아닌 효과(정확도 +%, 오프사이드·파울 확률, 빅매치 능력치 +8%)는 008·012·002 경로라 여기 넣지 않는다(FR-88 ②).
    /// 다이버는 표에 수치가 없어 [대체] 1.5. 레벨 2 배수 = 1 + (m − 1)·1.5 [대체] (003 레벨 정의 전, 003 재확인).
    /// </summary>
    public static class HabitTable
    {
        public const int Count = 15;
        public const float Level2Scale = 1.5f;

        static readonly HabitEffect[][] Rows =
        {
            new[] { new HabitEffect(Consideration.Dribble, 1.8f), new HabitEffect(Consideration.Pass, 0.8f) },
            new[] { new HabitEffect(Consideration.Cross, 2.0f), new HabitEffect(Consideration.CutIn, 0.5f) },
            new[] { new HabitEffect(Consideration.LongShot, 2.0f) },
            new[] { new HabitEffect(Consideration.CutIn, 1.7f) },
            new[] { new HabitEffect(Consideration.RunInBehind, 2.0f) },
            new[] { new HabitEffect(Consideration.HoldBall, 1.6f), new HabitEffect(Consideration.OneTouchPass, 0.6f) },
            new[] { new HabitEffect(Consideration.OneTouchPass, 1.8f) },
            new[] { new HabitEffect(Consideration.ThroughBall, 2.0f) },
            new[] { new HabitEffect(Consideration.SlideTackle, 2.0f) },
            new[] { new HabitEffect(Consideration.BoxCrash, 1.8f) },
            new[] { new HabitEffect(Consideration.Shot, 0.7f), new HabitEffect(Consideration.AssistPass, 1.4f) },
            new[] { new HabitEffect(Consideration.Shot, 1.5f), new HabitEffect(Consideration.Pass, 0.8f) },
            new[] { new HabitEffect(Consideration.Trick, 2.0f) },
            new[] { new HabitEffect(Consideration.DiveForFoul, 1.5f) },
            System.Array.Empty<HabitEffect>(),
        };

        public static int EffectCount(Habit h) => Rows[(int)h].Length;
        public static HabitEffect Effect(Habit h, int i) => Rows[(int)h][i];

        /// <summary>버릇 h(레벨 0/1/2)가 항목 k에 거는 배수. 레벨 0 = 1.</summary>
        public static float Multiplier(Habit h, int level, Consideration k)
        {
            if (level <= 0) return 1f;
            var row = Rows[(int)h];
            for (int i = 0; i < row.Length; i++)
            {
                if (row[i].Kind != k) continue;
                var m = row[i].Multiplier;
                return level >= 2 ? 1f + (m - 1f) * Level2Scale : m;
            }
            return 1f;
        }

        /// <summary>버릇이 고려 항목 k를 줄이는가 (의도와 충돌하는 버릇, FR-93). 줄이는 배수 &lt; 1이 있거나, k와 경쟁하는 항목을 키우면 충돌.</summary>
        public static bool Conflicts(Habit h, Consideration intentKind)
        {
            var row = Rows[(int)h];
            for (int i = 0; i < row.Length; i++)
            {
                var e = row[i];
                if (e.Kind == intentKind) return e.Multiplier < 1f;
                if (e.Multiplier > 1f && Considerations.IsOffBall(e.Kind) && Considerations.IsOffBall(intentKind) && e.Kind != intentKind) return true;
            }
            return false;
        }
    }
}
