using System;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 운동 능력치 입력 (내부 스케일 0~100, specs/005 FR-01~06). 002 완료 전에는 호출자가 직접 채우고,
    /// 002가 나중에 선수 도메인에서 채운다 (2026-11-03 결정). 범위 밖·NaN은 생성 시 거부 (로딩 단계 거부).
    /// </summary>
    public readonly struct KinematicAttributes
    {
        public const float InternalMax = 100f;

        public readonly float Pace;
        public readonly float Acceleration;
        public readonly float Agility;
        public readonly float Balance;
        public readonly float Stamina;
        public readonly float Anticipation;
        public readonly float Concentration;

        public KinematicAttributes(float pace, float acceleration, float agility, float balance, float stamina, float anticipation, float concentration)
        {
            Pace = Check(pace, nameof(pace));
            Acceleration = Check(acceleration, nameof(acceleration));
            Agility = Check(agility, nameof(agility));
            Balance = Check(balance, nameof(balance));
            Stamina = Check(stamina, nameof(stamina));
            Anticipation = Check(anticipation, nameof(anticipation));
            Concentration = Check(concentration, nameof(concentration));
        }

        /// <summary>표시 능력치 N(1~20)의 대표 내부값: 100·(N−1)/19 (002 FR-14). 범위 밖은 거부.</summary>
        public static float InternalFromDisplay(int display)
        {
            if (display < 1 || display > 20) throw new ArgumentOutOfRangeException(nameof(display), display, "표시 능력치는 1~20");
            return InternalMax * (display - 1) / 19f;
        }

        /// <summary>모든 능력치가 표시값 N인 선수.</summary>
        public static KinematicAttributes Uniform(int display)
        {
            var v = InternalFromDisplay(display);
            return new KinematicAttributes(v, v, v, v, v, v, v);
        }

        /// <summary>정규화 s = 내부/100 (002 FR-14).</summary>
        public static float Normalized(float internalValue) => internalValue / InternalMax;

        static float Check(float v, string name)
        {
            if (!(v >= 0f && v <= InternalMax)) throw new ArgumentOutOfRangeException(name, v, "운동 능력치 내부값은 0~100");
            return v;
        }
    }
}
