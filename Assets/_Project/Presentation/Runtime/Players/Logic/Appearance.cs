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
        public readonly int SkinTone, Face, HairStyle, HairColor;

        public PlayerAppearance(float heightCm, float weightKg, float thin, float muscular, float heavy,
            int skinTone, int face, int hairStyle, int hairColor)
        {
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

        /// <summary>외형 조합 번호 (중복 판정, FR-03). 체형은 포함하지 않는다.</summary>
        public int ComboKey => ((SkinTone * AppearanceGenerator.Faces + Face) * AppearanceGenerator.HairStyles + HairStyle)
                               * AppearanceGenerator.HairColors + HairColor;

        public float Scale => HeightCm / AppearanceGenerator.RefHeightCm;

        public PlayerAppearance WithCombo(int combo)
        {
            int hc = combo % AppearanceGenerator.HairColors; combo /= AppearanceGenerator.HairColors;
            int hs = combo % AppearanceGenerator.HairStyles; combo /= AppearanceGenerator.HairStyles;
            int f = combo % AppearanceGenerator.Faces; combo /= AppearanceGenerator.Faces;
            int s = combo % AppearanceGenerator.SkinTones;
            return new PlayerAppearance(HeightCm, WeightKg, Thin, Muscular, Heavy, s, f, hs, hc);
        }

        public bool Equals(PlayerAppearance o) =>
            HeightCm.Equals(o.HeightCm) && WeightKg.Equals(o.WeightKg) && Thin.Equals(o.Thin) && Muscular.Equals(o.Muscular) &&
            Heavy.Equals(o.Heavy) && SkinTone == o.SkinTone && Face == o.Face && HairStyle == o.HairStyle && HairColor == o.HairColor;

        public override bool Equals(object obj) => obj is PlayerAppearance o && Equals(o);
        public override int GetHashCode() => ComboKey * 397 ^ HeightCm.GetHashCode();
        public override string ToString() => $"{HeightCm:0.#}cm {WeightKg:0.#}kg T{Thin:0.##} M{Muscular:0.##} H{Heavy:0.##} skin{SkinTone} face{Face} hair{HairStyle}/{HairColor}";
    }

    /// <summary>
    /// 선수 id → 외형 (FR-01~03). 시드 = StableHasher(id, 태그) → SplitMix64. 같은 id → 같은 외형.
    /// 키(값이 없을 때): 역할별 정규분포 — 공개 연구의 엘리트 선수 평균(GK ≈ 188, 수비 ≈ 183, 미드 ≈ 179, 공격 ≈ 181 cm, 표준편차 ≈ 5.5~6.3;
    /// World Cup 736명·EURO 2008 분석, 기획서 리서치 섹션에 출처) → 160~200 cm로 자름.
    /// 체중(값이 없을 때): BMI 정규분포 평균 23.2 · 표준편차 1.1 (엘리트 선수 연구 범위 22~24).
    /// 체형: BMI 18.5 이하 Thin 1 → 22 에서 0 → 27 이상 Heavy 1. Muscular는 시드(0.25~1, 선수는 근육형이 많다).
    /// </summary>
    public static class AppearanceGenerator
    {
        public const float RefHeightCm = 180f;
        public const float MinHeightCm = 160f, MaxHeightCm = 200f;   // FR-01 [제안] 승인 (2026-11-04)
        public const float MinWeightKg = 45f, MaxWeightKg = 110f;
        public const int SkinTones = 8, Faces = 8, HairStyles = 10, HairColors = 6;
        public const int Combos = SkinTones * Faces * HairStyles * HairColors;
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
            int hs = (int)(SplitMix64.Next(ref s) % HairStyles);
            int hc = (int)(SplitMix64.Next(ref s) % HairColors);
            return new PlayerAppearance(height, weight, thin, muscular, heavy, skin, face, hs, hc);
        }

        /// <summary>한 경기(또는 한 장면)의 선수들. 입력 순서대로 처리하고, 이미 쓴 조합이면 다음 조합으로 민다 (FR-03).</summary>
        public static void GenerateGroup(ReadOnlySpan<BodyInput> inputs, Span<PlayerAppearance> output)
        {
            if (output.Length < inputs.Length) throw new ArgumentException("output이 작다");
            if (inputs.Length > Combos) throw new ArgumentException("외형 조합 수보다 선수가 많다");
            Span<bool> used = stackalloc bool[Combos];
            for (int i = 0; i < inputs.Length; i++)
            {
                var a = Generate(inputs[i]);
                int c = a.ComboKey;
                // 다음 조합: 큰 소수 보폭으로 건너뛰어 이웃 조합(헤어 색만 다름)에 몰리지 않게 한다
                while (used[c]) c = (c + 389) % Combos;
                used[c] = true;
                output[i] = c == a.ComboKey ? a : a.WithCombo(c);
            }
        }
    }
}
