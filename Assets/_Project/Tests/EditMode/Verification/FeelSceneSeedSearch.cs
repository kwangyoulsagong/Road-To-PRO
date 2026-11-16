using System.IO;
using NUnit.Framework;
using RoadToPro.Verification.Samples.Highlight;

namespace RoadToPro.Tests.EditMode.Verification
{
    /// <summary>
    /// specs/081 T19 도구: 058 실경기 픽스처로 선정 장면 안에 AC-07 장면 7종이 있는 시드를 찾는다 (리포트용, Explicit). 판정은 `FeelSceneCoverage` —
    /// E2E는 플레이어 안에서 같은 탐색을 다시 한다(에디터·플레이어 빌드 시뮬 차이). N = `.farm/feel-seed-n.txt`(기본 128).
    /// </summary>
    public class FeelSceneSeedSearch
    {
        [Test, Explicit("시드 탐색 도구"), Category("Slow"), Timeout(3_600_000)]
        public void Search_Seeds()
        {
            var n = 128;
            var np = Path.Combine(Directory.GetCurrentDirectory(), ".farm", "feel-seed-n.txt");
            if (File.Exists(np)) n = int.Parse(File.ReadAllText(np).Trim());
            var masks = FeelSceneCoverage.Search(Path.Combine(Directory.GetCurrentDirectory(), "Data"), n);
            FeelSceneCoverage.Choose(masks, out var best, out var slide);
            for (int i = 0; i < n; i++)
                TestContext.WriteLine($"{FeelSceneCoverage.FirstSeed + (ulong)i}: [{FeelSceneCoverage.Describe(masks[i])}] ({FeelSceneCoverage.Bits(masks[i])}/7)");
            TestContext.WriteLine($"A {FeelSceneCoverage.FirstSeed + (ulong)best} [{FeelSceneCoverage.Describe(masks[best])}], 늦은 슬라이딩 {(slide >= 0 ? (FeelSceneCoverage.FirstSeed + (ulong)slide).ToString() : "없음")}");
        }
    }
}
