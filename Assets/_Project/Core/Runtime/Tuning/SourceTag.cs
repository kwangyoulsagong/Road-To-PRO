namespace RoadToPro.Core.Tuning
{
    /// <summary>
    /// 튜닝 값 출처 태그 (specs/001 FR-80). 기획서 표기 그대로 문자열로 보관한다: <c>[설계값]</c> <c>[V]</c> <c>[U]</c> <c>[M]</c> <c>[E]</c> <c>[제안]</c> 등.
    /// 형식은 대괄호로 감싼 비어 있지 않은 텍스트.
    /// </summary>
    public static class SourceTag
    {
        public const string Design = "[설계값]";
        public const string Proposed = "[제안]";

        public static bool IsValid(string tag) =>
            !string.IsNullOrEmpty(tag) && tag.Length >= 3 && tag[0] == '[' && tag[tag.Length - 1] == ']' && tag.IndexOf(']') == tag.Length - 1;
    }
}
