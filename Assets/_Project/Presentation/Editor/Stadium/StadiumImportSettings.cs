namespace RoadToPro.Presentation.Editor.Stadium
{
    /// <summary>
    /// 경기장 glb 임포트 규칙 (specs/044 FR-20·23·24). 생성 산출물 경로와 축 변환 결과를 한곳에 둔다.
    /// 축(T03 실측): <see cref="AxisNote"/>.
    /// </summary>
    public static class StadiumImportSettings
    {
        public const string GeneratedDir = "Assets/_Project/Presentation/Stadium/Generated";
        public const string AxisTestPath = GeneratedDir + "/axis-test.glb";
        public const string AxisNote = "Blender (x,y,z) → Unity (−x, z, −y). 생성기는 경기장 (x,y,z)를 Blender (−x,−y,z)로 넣어 Unity (X 길이, Y 위, Z 폭)";
    }
}
