using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Random;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>키 분포에 쓰는 역할 (포지션별 키 차이).</summary>
    public enum BodyRole : byte
    {
        Unknown = 0,
        Goalkeeper = 1,
        Defender = 2,
        Midfielder = 3,
        Forward = 4,
        Referee = 5,
    }

    /// <summary>외형 입력 (specs/043 FR-01). 키·체중은 002 도메인 값 — 없으면(null) 선수 id 시드와 역할에서 유도.</summary>
    public readonly struct BodyInput
    {
        public readonly int Id;
        public readonly float? HeightCm;
        public readonly float? WeightKg;
        public readonly BodyRole Role;

        public BodyInput(int id, float? heightCm = null, float? weightKg = null, BodyRole role = BodyRole.Unknown)
        {
            Id = id;
            HeightCm = heightCm;
            WeightKg = weightKg;
            Role = role;
        }
    }

    /// <summary>
    /// 선수 외형 (FR-01·02). 체형 셰이프키 가중치 Thin·Muscular·Heavy(0~1)와 외형 조합(피부·얼굴·헤어·헤어 색).
    /// 루트 균등 스케일 = HeightCm / 180 (메시 기준 키 1.80 m, specs/043 player.layout.json skeleton.refHeight).
    /// </summary>
    public readonly struct PlayerAppearance : IEquatable<PlayerAppearance>
    {
        public readonly float HeightCm, WeightKg;
        public readonly float Thin, Muscular, Heavy;
        /// <summary>062 FR-01: 어깨·가슴 폭(Broad)·팔다리 근육 둘레(Limb) 0~1 — 같은 키·체중에서도 실루엣이 달라진다.</summary>
        public readonly float Broad, Limb;
        public readonly int SkinTone, Face, HairStyle, HairColor;
        /// <summary>062 FR-54: 컬러 포인트 0 = 없음, 1~3 = PlayerLook.ColorPointColors[n − 1] (위·끝 염색, 뿌리·옆은 HairColor).</summary>
        public readonly int ColorPoint;

        public PlayerAppearance(float heightCm, float weightKg, float thin, float muscular, float heavy,
            int skinTone, int face, int hairStyle, int hairColor, float broad = 0.5f, float limb = 0.5f, int colorPoint = 0)
        {
            ColorPoint = colorPoint;
            Broad = broad;
            Limb = limb;
            HeightCm = heightCm;
            WeightKg = weightKg;
            Thin = thin;
            Muscular = muscular;
            Heavy = heavy;
            SkinTone = skinTone;
            Face = face;
            HairStyle = hairStyle;
            HairColor = hairColor;
        }

        /// <summary>외형 조합 번호 (중복 판정, FR-03). 체형은 포함하지 않는다. 062: 컬러 포인트 포함.</summary>
        public int ComboKey => (((SkinTone * AppearanceGenerator.Faces + Face) * AppearanceGenerator.HairStyles + HairStyle)
                                * AppearanceGenerator.HairColors + HairColor) * AppearanceGenerator.ColorPoints + ColorPoint;

        public float Scale => HeightCm / AppearanceGenerator.RefHeightCm;

        public PlayerAppearance WithCombo(int combo)
        {
            int cp = combo % AppearanceGenerator.ColorPoints; combo /= AppearanceGenerator.ColorPoints;
            int hc = combo % AppearanceGenerator.HairColors; combo /= AppearanceGenerator.HairColors;
            int hs = combo % AppearanceGenerator.HairStyles; combo /= AppearanceGenerator.HairStyles;
            int f = combo % AppearanceGenerator.Faces; combo /= AppearanceGenerator.Faces;
            int s = combo % AppearanceGenerator.SkinTones;
            return new PlayerAppearance(HeightCm, WeightKg, Thin, Muscular, Heavy, s, f, hs, hc, Broad, Limb, cp);
        }

        /// <summary>헤어(스타일·색·컬러 포인트)만 바꾼 외형 — 검증 장면 일람용. 나머지 조합은 그대로.</summary>
        public PlayerAppearance WithHair(int hairStyle, int hairColor, int colorPoint) =>
            new PlayerAppearance(HeightCm, WeightKg, Thin, Muscular, Heavy, SkinTone, Face, hairStyle, hairColor, Broad, Limb, colorPoint);

        public bool Equals(PlayerAppearance o) =>
            HeightCm.Equals(o.HeightCm) && WeightKg.Equals(o.WeightKg) && Thin.Equals(o.Thin) && Muscular.Equals(o.Muscular) &&
            Heavy.Equals(o.Heavy) && Broad.Equals(o.Broad) && Limb.Equals(o.Limb) && SkinTone == o.SkinTone && Face == o.Face && HairStyle == o.HairStyle && HairColor == o.HairColor &&
            ColorPoint == o.ColorPoint;

        public override bool Equals(object obj) => obj is PlayerAppearance o && Equals(o);
        public override int GetHashCode() => ComboKey * 397 ^ HeightCm.GetHashCode();
        public override string ToString() => $"{HeightCm:0.#}cm {WeightKg:0.#}kg T{Thin:0.##} M{Muscular:0.##} H{Heavy:0.##} B{Broad:0.##} L{Limb:0.##} skin{SkinTone} face{Face} hair{HairStyle}/{HairColor}/cp{ColorPoint}";
    }

    /// <summary>
    /// 선수 id → 외형 (FR-01~03). 시드 = StableHasher(id, 태그) → SplitMix64. 같은 id → 같은 외형.
    /// 키(값이 없을 때): 역할별 정규분포 — 공개 연구의 엘리트 선수 평균(GK ≈ 188, 수비 ≈ 183, 미드 ≈ 179, 공격 ≈ 181 cm, 표준편차 ≈ 5.5~6.3;
    /// World Cup 736명·EURO 2008 분석, 기획서 리서치 섹션에 출처) → 160~200 cm로 자름.
    /// 체중(값이 없을 때): BMI 정규분포 평균 23.2 · 표준편차 1.1 (엘리트 선수 연구 범위 22~24).
    /// 체형: BMI 18.5 이하 Thin 1 → 22 에서 0 → 27 이상 Heavy 1. Muscular는 시드(0.25~1, 선수는 근육형이 많다).
    /// </summary>
    /// <summary>헤어스타일 계열 (specs/057 FR-31).</summary>
    public enum HairFamily { Short, Medium, Long }

    public static class AppearanceGenerator
    {
        public const float RefHeightCm = 180f;
        public const float MinHeightCm = 160f, MaxHeightCm = 200f;   // FR-01 [제안] 승인 (2026-11-04)
        public const float MinWeightKg = 45f, MaxWeightKg = 110f;
        /// <summary>062 FR-52·53·54: 헤어 35종(0~29 = 057, 30~34 신규), 8색(자연 6 + 탈색 금발·백금), 컬러 포인트 4(없음 + 3색).</summary>
        public const int SkinTones = 8, Faces = 8, HairStyles = 35, HairColors = 8, ColorPoints = 4;
        public const int Combos = SkinTones * Faces * HairStyles * HairColors * ColorPoints;
        /// <summary>헤어 색 인덱스 (PlayerLook.HairColors): 0 검정 · 1 진갈색 · 2 갈색 · 3 금발 · 4 적갈색 · 5 회색 · 6 탈색 금발 · 7 백금.</summary>
        public const int HairColorGrey = 5, HairColorBleach = 6, HairColorPlatinum = 7;

        /// <summary>장면 묶음(GenerateGroup)에서 같은 헤어스타일 최대 인원 (specs/057 FR-32). 선수 수 &gt; 상한 × 35이면 ⌈n/35⌉.</summary>
        public const int HairCapPerGroup = 3;
        /// <summary>상한에 닿은 스타일을 미는 보폭 — HairStyles와 서로소 (gcd(8, 35) = 1).</summary>
        const int HairStep = 8;

        // specs/057 FR-30·31 + 062 FR-52 헤어 35종 가중치(합 1000)와 계열. 인덱스 0~9는 056 순서, 9 = 삭발.
        // 신규 5종 = 같은 계열 평균(T02 정리, 2026-11-20 "ㄱㄱ") 후 1000으로 다시 나눔(최대 나머지). 짧은 63.8 % · 중간 24.8 % · 긴·묶음 11.4 %.
        // 표는 Art/Blender/scripts/player/hair.py STYLE_SPEC과 같은 순서.
        static readonly short[] HairWeights =
        {
            36, 36, 15, 28, 15, 36, 27, 27, 14, 36,
            36, 36, 36, 28, 35, 28, 35, 35, 27, 35,
            28, 14, 14, 27, 35, 35, 35, 36, 14, 14,
            35, 28, 14, 35, 35,
        };
        // 062 FR-53·54 헤어 색 가중치(천분율): 자연 6색 균등(043처럼) 155씩, 탈색 금발 40 · 백금 30 → 탈색 7 % (≤ 10 %, T02 정리)
        static readonly short[] HairColorWeights = { 155, 155, 155, 155, 155, 155, 40, 30 };
        /// <summary>FR-54: 컬러 포인트 대상 스타일(hair.py cp 마스크가 있는 스타일)에서 컬러 포인트를 칠할 확률(천분율) — 회색 제외.
        /// 대상 가중치 18.4 % × 비회색 84.5 % × 30 % ≈ 4.7 % (≤ 8 %).</summary>
        public const int ColorPointPermille = 300;
        // FR-54 컬러 포인트 대상: 페이드 5 · 모히칸 7 · 언더컷 12 · 곱슬 탑 27 · 박스 브레이드 28 · 가르마 투블록 30
        static readonly bool[] ColorPointStyles = BuildColorPointStyles();

        static bool[] BuildColorPointStyles()
        {
            var t = new bool[HairStyles];
            foreach (int i in new[] { 5, 7, 12, 27, 28, 30 }) t[i] = true;
            return t;
        }

        public static bool ColorPointTarget(int style) => ColorPointStyles[style];
        public static bool IsBleached(int color) => color == HairColorBleach || color == HairColorPlatinum;

        /// <summary>어색한 조합 제외(plan): 컬러 포인트는 대상 스타일에만, 회색 머리에는 없다.</summary>
        public static bool ValidColorPoint(int style, int color, int colorPoint) =>
            colorPoint == 0 || (ColorPointStyles[style] && color != HairColorGrey);
        static readonly HairFamily[] HairFamilies =
        {
            HairFamily.Short, HairFamily.Short, HairFamily.Long, HairFamily.Medium, HairFamily.Long,
            HairFamily.Short, HairFamily.Medium, HairFamily.Medium, HairFamily.Long, HairFamily.Short,
            HairFamily.Short, HairFamily.Short, HairFamily.Short, HairFamily.Medium, HairFamily.Short,
            HairFamily.Medium, HairFamily.Short, HairFamily.Short, HairFamily.Medium, HairFamily.Short,
            HairFamily.Medium, HairFamily.Long, HairFamily.Long, HairFamily.Medium, HairFamily.Short,
            HairFamily.Short, HairFamily.Short, HairFamily.Short, HairFamily.Long, HairFamily.Long,
            HairFamily.Short, HairFamily.Medium, HairFamily.Long, HairFamily.Short, HairFamily.Short,
        };

        public static int HairWeight(int style) => HairWeights[style];
        public static HairFamily HairFamilyOf(int style) => HairFamilies[style];

        /// <summary>정수 누적표 가중 선택 (부동소수 없음, 결정론).</summary>
        static int PickHair(ulong r) => Pick(HairWeights, r);

        static int Pick(short[] weights, ulong r)
        {
            int x = (int)(r % 1000UL);
            for (int i = 0; i < weights.Length; i++)
            {
                x -= weights[i];
                if (x < 0) return i;
            }
            return weights.Length - 1;
        }
        const uint Tag = 0x0043A99Eu;

        static float U01(ref ulong s) => (float)(SplitMix64.Next(ref s) >> 40) / (1 << 24);

        /// <summary>표준정규 (Box–Muller, 결정론).</summary>
        static float Normal(ref ulong s)
        {
            double u1 = Math.Max(1e-9, U01(ref s)), u2 = U01(ref s);
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        public static void RoleHeight(BodyRole r, out float mean, out float sd)
        {
            switch (r)
            {
                case BodyRole.Goalkeeper: mean = 188f; sd = 5.2f; break;
                case BodyRole.Defender: mean = 183f; sd = 6.0f; break;
                case BodyRole.Midfielder: mean = 179f; sd = 6.0f; break;
                case BodyRole.Forward: mean = 181f; sd = 6.4f; break;
                case BodyRole.Referee: mean = 180f; sd = 6.0f; break;
                default: mean = 181f; sd = 6.5f; break;
            }
        }

        static float RoleBroad(BodyRole r) => r switch
        {
            BodyRole.Goalkeeper => 0.08f,
            BodyRole.Defender => 0.08f,
            BodyRole.Midfielder => -0.06f,
            _ => 0f,
        };

        public static PlayerAppearance Generate(in BodyInput input)
        {
            var h = StableHasher.Create();
            h.Add(Tag);
            h.Add((uint)input.Id);
            ulong s = h.Value;
            // 키: 역할별 정규분포(Box–Muller), 범위 밖은 자른다
            RoleHeight(input.Role, out float mu, out float sd);
            float height = input.HeightCm ?? mu + sd * Normal(ref s);
            height = Math.Clamp(height, MinHeightCm, MaxHeightCm);
            float bmiSeed = Math.Clamp(23.2f + 1.1f * Normal(ref s), 19.5f, 27.5f);
            float m = height / 100f;
            float weight = input.WeightKg ?? bmiSeed * m * m;
            weight = Math.Clamp(weight, MinWeightKg, MaxWeightKg);
            float bmi = weight / (m * m);
            float thin = Math.Clamp((22f - bmi) / (22f - 18.5f), 0f, 1f);
            float heavy = Math.Clamp((bmi - 22f) / (27f - 22f), 0f, 1f);
            float muscular = 0.25f + 0.25f * (SplitMix64.Next(ref s) % 4);
            int skin = (int)(SplitMix64.Next(ref s) % SkinTones);
            int face = (int)(SplitMix64.Next(ref s) % Faces);
            int hs = PickHair(SplitMix64.Next(ref s));
            int hc = Pick(HairColorWeights, SplitMix64.Next(ref s));
            // 062 FR-01·04: 새 축은 기존 뽑기 뒤에서 뽑는다(키·체중·피부 값 유지). Broad는 넓게 퍼뜨리고(팀 안 어깨 너비 표준편차,
            // FR-04 개정 ≥ 0.010) 역할로 살짝 민다(골키퍼·수비 +, 미드 −). Limb는 근육량과 반반.
            float broad = Math.Clamp(U01(ref s) * 1.1f - 0.05f + RoleBroad(input.Role), 0f, 1f);
            float limb = Math.Clamp(0.5f * ((muscular - 0.25f) / 0.75f) + 0.5f * U01(ref s), 0f, 1f);
            // 062 FR-54: 컬러 포인트는 체형 축 뒤에서 뽑는다 (대상 스타일·비회색일 때만 칠한다)
            ulong rc = SplitMix64.Next(ref s);
            int cp = 0;
            if (ValidColorPoint(hs, hc, 1) && (int)(rc % 1000UL) < ColorPointPermille) cp = 1 + (int)((rc / 1000UL) % (ColorPoints - 1));
            return new PlayerAppearance(height, weight, thin, muscular, heavy, skin, face, hs, hc, broad, limb, cp);
        }

        static int HairOf(int combo) => (combo / (HairColors * ColorPoints)) % HairStyles;
        static int WithHair(int combo, int hs) => combo + (hs - HairOf(combo)) * HairColors * ColorPoints;
        static bool ValidCombo(int combo) =>
            ValidColorPoint(HairOf(combo), (combo / ColorPoints) % HairColors, combo % ColorPoints);

        /// <summary>한 경기(또는 한 장면)의 선수들. 입력 순서대로 처리하고, 이미 쓴 조합이면 다음 조합으로 민다 (FR-03).</summary>
        public static void GenerateGroup(ReadOnlySpan<BodyInput> inputs, Span<PlayerAppearance> output)
        {
            if (output.Length < inputs.Length) throw new ArgumentException("output이 작다");
            if (inputs.Length > Combos) throw new ArgumentException("외형 조합 수보다 선수가 많다");
            // 062: 조합 수가 커져(컬러 포인트 × 4) 스택 배열 대신 사용한 조합만 담는다 — 로딩 단계 전용 할당 (plan '데이터')
            var used = new System.Collections.Generic.HashSet<int>();
            Span<int> hairCount = stackalloc int[HairStyles];
            int cap = Math.Max(HairCapPerGroup, (inputs.Length + HairStyles - 1) / HairStyles);
            for (int i = 0; i < inputs.Length; i++)
            {
                var a = Generate(inputs[i]);
                int c = a.ComboKey;
                // 다음 조합: 큰 소수 보폭으로 건너뛰어 이웃 조합(헤어 색만 다름)에 몰리지 않게 한다.
                // 057 FR-32: 같은 헤어스타일이 상한에 닿았으면 헤어스타일을 HairStep칸씩 민다(같은 계열에 몰리지 않게). 상한 × 35 ≥ n이라 끝난다.
                // 062: 보폭은 스타일 수와 서로소여야 모든 스타일을 돈다 — 7은 35의 약수라 5종만 맴돌다 무한 루프(T24).
                // 062: 밀린 조합도 컬러 포인트 규칙(대상 스타일·회색 제외)을 지킨다 — 어긴 조합은 건너뛴다.
                while (used.Contains(c) || hairCount[HairOf(c)] >= cap || !ValidCombo(c))
                {
                    if (hairCount[HairOf(c)] >= cap) c = WithHair(c, (HairOf(c) + HairStep) % HairStyles);
                    else c = (c + 389) % Combos;
                }
                used.Add(c);
                hairCount[HairOf(c)]++;
                output[i] = c == a.ComboKey ? a : a.WithCombo(c);
            }
        }
    }
}
