using System;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>
    /// GK 능력치 입력 (내부 스케일 0~100, specs/011 FR-70 · 002 이름). 002 전에는 호출자가 채운다 (005 <c>KinematicAttributes</c>와 같은 방식).
    /// 기본값(<c>default</c>)은 "지정 안 됨"(<see cref="IsSet"/> false) — 경기는 리그 평균 또는 필드 선수 대체값으로 읽는다.
    /// </summary>
    public readonly struct KeeperAttributes
    {
        public const float InternalMax = 100f;
        public const float MinHeight = 1.5f, MaxHeight = 2.2f;

        public readonly float Reflexes;
        public readonly float Diving;
        public readonly float Handling;
        public readonly float GKPositioning;
        public readonly float OneOnOne;
        public readonly float CommandArea;
        public readonly float Sweeping;
        public readonly float Distribution;
        public readonly float Jumping;
        public readonly float Composure;
        public readonly float Decisions;
        /// <summary>키 (m). 0이면 지정 안 됨.</summary>
        public readonly float HeightM;

        public KeeperAttributes(float reflexes, float diving, float handling, float gkPositioning, float oneOnOne, float commandArea, float sweeping,
            float distribution, float jumping, float composure, float decisions, float heightM)
        {
            Reflexes = Check(reflexes, nameof(reflexes));
            Diving = Check(diving, nameof(diving));
            Handling = Check(handling, nameof(handling));
            GKPositioning = Check(gkPositioning, nameof(gkPositioning));
            OneOnOne = Check(oneOnOne, nameof(oneOnOne));
            CommandArea = Check(commandArea, nameof(commandArea));
            Sweeping = Check(sweeping, nameof(sweeping));
            Distribution = Check(distribution, nameof(distribution));
            Jumping = Check(jumping, nameof(jumping));
            Composure = Check(composure, nameof(composure));
            Decisions = Check(decisions, nameof(decisions));
            if (!(heightM >= MinHeight && heightM <= MaxHeight)) throw new ArgumentOutOfRangeException(nameof(heightM), heightM, "키는 1.5~2.2 m");
            HeightM = heightM;
        }

        public bool IsSet => HeightM > 0f;

        /// <summary>표시값 N(1~20, 소수 허용 — 리그 평균 10.5)의 내부값 100·(N−1)/19 (002 FR-14).</summary>
        public static float InternalFromDisplay(float display)
        {
            if (!(display >= 1f && display <= 20f)) throw new ArgumentOutOfRangeException(nameof(display), display, "표시 능력치는 1~20");
            return InternalMax * (display - 1f) / 19f;
        }

        /// <summary>모든 GK 능력치가 표시값 N인 GK.</summary>
        public static KeeperAttributes Uniform(float display, float heightM = 1.88f)
        {
            var v = InternalFromDisplay(display);
            return new KeeperAttributes(v, v, v, v, v, v, v, v, v, v, v, heightM);
        }

        /// <summary>정규화 n = 내부/100 (002 FR-14).</summary>
        public static float N(float internalValue) => internalValue / InternalMax;

        static float Check(float v, string name)
        {
            if (!(v >= 0f && v <= InternalMax)) throw new ArgumentOutOfRangeException(name, v, "GK 능력치 내부값은 0~100");
            return v;
        }
    }
}
