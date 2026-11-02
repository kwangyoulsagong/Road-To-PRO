using System;
namespace RoadToPro.Core.Tuning
{
    /// <summary>튜닝 상수 정의 (specs/001 FR-80): id, 기본값, 허용 범위 [Min, Max], 단위, 출처 태그, 원문 위치.</summary>
    public sealed class TuningDefinition
    {
        public readonly string Id;
        public readonly float Default;
        public readonly float Min;
        public readonly float Max;
        public readonly string Unit;
        public readonly string Tag;
        public readonly string Source;

        public TuningDefinition(string id, float defaultValue, float min, float max, string unit, string tag, string source)
        {
            if (string.IsNullOrEmpty(id)) throw new TuningLoadException(null, "id가 비어 있다");
            if (float.IsNaN(min) || float.IsNaN(max) || min > max) throw new TuningLoadException(id, FormattableString.Invariant($"범위 오류 [{min}, {max}]"));
            if (!(defaultValue >= min && defaultValue <= max)) throw new TuningLoadException(id, FormattableString.Invariant($"기본값 {defaultValue}이 범위 [{min}, {max}] 밖"));
            if (!SourceTag.IsValid(tag)) throw new TuningLoadException(id, $"출처 태그 형식 오류 '{tag}' (예: [설계값], [제안])");
            Id = id;
            Default = defaultValue;
            Min = min;
            Max = max;
            Unit = unit ?? "";
            Tag = tag;
            Source = source ?? "";
        }

        public bool InRange(float v) => v >= Min && v <= Max;
    }
}
