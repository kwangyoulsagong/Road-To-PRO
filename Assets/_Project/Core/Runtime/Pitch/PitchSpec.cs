namespace RoadToPro.Core.Pitch
{
    /// <summary>피치 크기(미터). 경기 설정 값 (specs/001 FR-22). 기본 105×68 (research 03 A1).</summary>
    public readonly struct PitchSpec
    {
        public readonly float LengthM;
        public readonly float WidthM;

        public PitchSpec(float lengthM, float widthM)
        {
            LengthM = lengthM;
            WidthM = widthM;
        }

        public static PitchSpec Default => new PitchSpec(105f, 68f);
    }
}
