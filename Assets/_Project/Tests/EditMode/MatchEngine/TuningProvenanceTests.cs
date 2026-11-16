using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Tuning;

namespace RoadToPro.Tests.EditMode.MatchEngine
{
    /// <summary>
    /// specs/081 AC-03·12 (FR-11·12): 보정은 [대체] 계수만 바꾸고, 바꾼 계수는 출처 칸에 081 기록(이전 → 새·근거)이 있다. [설계] 값은 main과 같다.
    /// 기준 = `specs/081-match-feel-calibration/evidence/tuning-main.json` (main 60f53c7 `Data/tuning/*.json` 합본). 081에서 새로 만든 계수는 비교 대상이 아니다.
    /// </summary>
    public class TuningProvenanceTests
    {
        static string Root => Directory.GetCurrentDirectory();

        static TuningRegistry Baseline() =>
            TuningLoader.LoadDefinitionsFile(Path.Combine(Root, "specs", "081-match-feel-calibration", "evidence", "tuning-main.json"));

        static string[] Files() => Directory.GetFiles(Path.Combine(Root, "Data", "tuning"), "*.json");

        [Test]
        public void TuningProvenance_ChangedIdsAreStandIn_WithSource()
        {
            var main = Baseline();
            var changed = 0;
            var seen = 0;
            foreach (var f in Files())
                foreach (var d in TuningLoader.LoadDefinitionsFile(f).Definitions)
                {
                    if (!main.TryGetIndex(d.Id, out var i)) continue; // 081 신규 계수
                    seen++;
                    var m = main.Definitions[i];
                    Assert.AreEqual(m.Tag, d.Tag, $"{d.Id}: 태그가 바뀌면 안 된다");
                    if (System.Math.Abs(m.Default - d.Default) <= 1e-6f) continue;
                    changed++;
                    Assert.AreEqual("[대체]", d.Tag, $"{d.Id}: [대체]만 보정할 수 있다 (FR-11) — main {m.Default} → {d.Default}");
                    StringAssert.Contains("081", d.Source, $"{d.Id}: 출처 칸에 081 보정 기록(이전 → 새·근거)이 있어야 한다 (FR-12)");
                }
            Assert.AreEqual(main.Count, seen, "main의 계수가 사라지지 않았다");
            TestContext.WriteLine($"main 대비 바뀐 계수 {changed}개");
        }

        [Test]
        public void ShotAimDesignCoefficients_Unchanged()
        {
            // 071 FR-14 σ_aim·act.cross.*·act.takeon.*·act.aerial.* 등 [설계] 값 전부 = main (AC-12, FR-21)
            var main = Baseline();
            var n = 0;
            foreach (var f in Files())
                foreach (var d in TuningLoader.LoadDefinitionsFile(f).Definitions)
                {
                    if (!main.TryGetIndex(d.Id, out var i) || main.Definitions[i].Tag != "[설계]") continue;
                    n++;
                    Assert.AreEqual(main.Definitions[i].Default, d.Default, 1e-6f, $"{d.Id}: [설계] 값은 바꾸지 않는다");
                }
            Assert.Greater(n, 0);
        }
    }
}
