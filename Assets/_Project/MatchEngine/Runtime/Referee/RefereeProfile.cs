using System;
using System.Collections.Generic;
using System.Globalization;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// 심판 성향 프로필 (specs/012 FR-30·34). 변수 7개는 모두 0~1 (홈 편향은 0~1 → 0~<c>ref.profile.homeBiasMax</c>로 환산).
    /// 실명 없음 — <see cref="Id"/>만 있고 표시 이름은 051이 생성한다 (01-legal).
    /// </summary>
    public sealed class RefereeProfile
    {
        public string Id { get; }
        /// <summary>엄격함: 높을수록 카드 임계값 낮음.</summary>
        public float Strictness { get; }
        /// <summary>파울 임계값: 높을수록 휘슬 드묾.</summary>
        public float FoulThreshold { get; }
        /// <summary>어드밴티지 선호.</summary>
        public float Advantage { get; }
        /// <summary>일관성 (노이즈 역수).</summary>
        public float Consistency { get; }
        /// <summary>홈 편향 (0~1, 환산 전).</summary>
        public float HomeBias { get; }
        /// <summary>체력 → 위치 선정 속도.</summary>
        public float Fitness { get; }
        /// <summary>압박 저항.</summary>
        public float PressureResistance { get; }

        public RefereeProfile(string id, float strictness, float foulThreshold, float advantage, float consistency, float homeBias, float fitness, float pressureResistance)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Strictness = C01(strictness);
            FoulThreshold = C01(foulThreshold);
            Advantage = C01(advantage);
            Consistency = C01(consistency);
            HomeBias = C01(homeBias);
            Fitness = C01(fitness);
            PressureResistance = C01(pressureResistance);
        }

        static float C01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f; // NaN → 0

        /// <summary>리그 평균 심판 (FR-96 보정 기준). 데이터 파일 첫 프로필과 같은 값.</summary>
        public static readonly RefereeProfile LeagueAverage = new RefereeProfile("ref-league-average", 0.5f, 0.5f, 0.5f, 0.5f, 0.2f, 0.5f, 0.5f);

        /// <summary>
        /// 006 호환: 기존 <c>MatchSetup.RefereeLeniency</c>(정수, 0 = 보통)를 엄격함으로 옮긴다 — 관대함 +1당 엄격함 −0.1 (0~1로 자름).
        /// </summary>
        public static RefereeProfile FromLeniency(int leniency) =>
            leniency == 0 ? LeagueAverage : new RefereeProfile("ref-leniency-" + leniency.ToString(CultureInfo.InvariantCulture), 0.5f - 0.1f * leniency, 0.5f, 0.5f, 0.5f, 0.2f, 0.5f, 0.5f);

        public RefereeProfile With(float? strictness = null, float? consistency = null, float? homeBias = null, float? fitness = null, float? advantage = null, float? foulThreshold = null, float? pressureResistance = null) =>
            new RefereeProfile(Id, strictness ?? Strictness, foulThreshold ?? FoulThreshold, advantage ?? Advantage, consistency ?? Consistency, homeBias ?? HomeBias, fitness ?? Fitness, pressureResistance ?? PressureResistance);

        public void AddTo(ref StableHasher h)
        {
            h.Add(Strictness); h.Add(FoulThreshold); h.Add(Advantage); h.Add(Consistency); h.Add(HomeBias); h.Add(Fitness); h.Add(PressureResistance);
        }

        /// <summary>
        /// `Data/referees/profiles.json` 읽기: <c>{"version":1,"profiles":[{"id":"...","strictness":0.5,...}]}</c>. 이름 필드는 허용하지 않는다(실명 방지, FR-34).
        /// </summary>
        public static List<RefereeProfile> ParseList(string json)
        {
            if (!(JsonReader.Parse(json) is JsonObject root)) throw new FormatException("심판 프로필: 최상위는 객체");
            if (!root.TryGet("profiles", out var arr) || !(arr is List<object> list)) throw new FormatException("심판 프로필: profiles 배열이 없다");
            var result = new List<RefereeProfile>(list.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                if (!(item is JsonObject o)) throw new FormatException("심판 프로필: 항목은 객체");
                if (o.TryGet("name", out _)) throw new FormatException("심판 프로필: name 필드 금지 (실명 방지, specs/012 FR-34)");
                var id = o.TryGet("id", out var idv) && idv is string s ? s : throw new FormatException("심판 프로필: id 없음");
                if (!ids.Add(id)) throw new FormatException($"심판 프로필: id 중복 {id}");
                result.Add(new RefereeProfile(id, F(o, "strictness"), F(o, "foulThreshold"), F(o, "advantage"), F(o, "consistency"), F(o, "homeBias"), F(o, "fitness"), F(o, "pressureResistance")));
            }
            return result;
        }

        static float F(JsonObject o, string key)
        {
            if (!o.TryGet(key, out var v) || !(v is double d)) throw new FormatException($"심판 프로필: {key} 숫자 없음");
            if (!(d >= 0d && d <= 1d)) throw new FormatException($"심판 프로필: {key} = {d}는 0~1 밖");
            return (float)d;
        }
    }
}
