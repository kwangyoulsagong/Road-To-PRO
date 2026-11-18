using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Tuning;

namespace RoadToPro.Tests.EditMode.MatchEngine
{
    /// <summary>
    /// specs/081 AC-03·12 (FR-11·12): 보정은 [대체] 계수만 바꾸고, 바꾼 계수는 출처 칸에 081 기록(이전 → 새·근거)이 있다. [설계] 값은 main과 같다.
    /// 084(2026-11-18): 출처에 "084:" 기록이 있는 계수는 081 비교에서 뺀다([설계] 개정 스펙 — `Tuning084_ChangedIds_HaveSource`).
    /// 087(2026-11-18): "087:" 기록이 있는 계수도 081·084 비교에서 뺀다(`Tuning087_ChangedIds_HaveSource`).
    /// 007(2026-11-18): "007:" 기록이 있는 계수도 081·084·087 비교에서 뺀다(`Tuning007_ChangedIds_HaveSource`).
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
                    if (Revised084(d.Source) || Revised087(d.Source) || Revised007(d.Source)) continue; // 084·087·007 개정 — 각 스펙 테스트가 본다
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

        /// <summary>specs/084 FR-06: 084가 개정한 계수는 출처 칸에 "084:" 기록(이전 → 새·근거)이 있다.</summary>
        static bool Revised084(string source) => source != null && source.Contains("084:");

        static TuningRegistry Baseline084() =>
            TuningLoader.LoadDefinitionsFile(Path.Combine(Root, "specs", "084-match-feel-calibration-2", "evidence", "tuning-before.json"));

        /// <summary>
        /// specs/084 FR-06 (플랜 해석 6): 084 시작 main 합본(`evidence/tuning-before.json`) 대비 바뀐 계수는 출처 칸에 "084:" 기록이 있고,
        /// 태그는 그대로([설계] 개정 허용)이며 값은 튜닝 min·max 안이다(경계 규칙: 밖이면 원 스펙 개정 제안). 계수가 사라지지 않는다.
        /// </summary>
        [Test]
        public void Tuning084_ChangedIds_HaveSource()
        {
            var before = Baseline084();
            var changed = 0;
            var seen = 0;
            foreach (var f in Files())
                foreach (var d in TuningLoader.LoadDefinitionsFile(f).Definitions)
                {
                    if (!before.TryGetIndex(d.Id, out var i)) continue;
                    seen++;
                    if (Revised087(d.Source) || Revised007(d.Source)) continue; // 087·007 개정 — 각 스펙 테스트가 본다
                    var b = before.Definitions[i];
                    Assert.AreEqual(b.Tag, d.Tag, $"{d.Id}: 태그가 바뀌면 안 된다");
                    if (System.Math.Abs(b.Default - d.Default) <= 1e-6f) continue;
                    changed++;
                    Assert.IsTrue(Revised084(d.Source), $"{d.Id}: 출처 칸에 '084:' 개정 기록이 있어야 한다 (FR-06) — {b.Default} → {d.Default}");
                    Assert.That(d.Default, Is.InRange(b.Min, b.Max), $"{d.Id}: 원 스펙 min·max 안이어야 한다");
                }
            Assert.AreEqual(before.Count, seen, "084 시작 계수가 사라지지 않았다");
            TestContext.WriteLine($"084 시작 대비 바뀐 계수 {changed}개");
        }

        /// <summary>specs/087 FR-08: 087이 개정한 계수는 출처 칸에 "087:" 기록(이전 → 새·근거)이 있다.</summary>
        static bool Revised087(string source) => source != null && source.Contains("087:");

        static TuningRegistry Baseline087() =>
            TuningLoader.LoadDefinitionsFile(Path.Combine(Root, "specs", "087-match-feel-calibration-3", "evidence", "tuning-before.json"));

        /// <summary>
        /// specs/087 FR-08 (플랜 해석 6): 087 시작 main 합본 대비 값 또는 범위(min·max)가 바뀐 계수는 출처 칸에 "087:" 기록이 있고, 태그는 그대로이며
        /// 값은 새 min·max 안이다. 범위를 넓힌 경우(FR-03 여유 상한 1.0 → 2.0 [제안])도 같은 기록을 요구한다. 계수가 사라지지 않는다. 087 신규 계수는 비교 대상이 아니다.
        /// </summary>
        [Test]
        public void Tuning087_ChangedIds_HaveSource()
        {
            var before = Baseline087();
            var changed = 0;
            var seen = 0;
            foreach (var f in Files())
                foreach (var d in TuningLoader.LoadDefinitionsFile(f).Definitions)
                {
                    if (!before.TryGetIndex(d.Id, out var i)) continue;
                    seen++;
                    var b = before.Definitions[i];
                    Assert.AreEqual(b.Tag, d.Tag, $"{d.Id}: 태그가 바뀌면 안 된다");
                    var same = System.Math.Abs(b.Default - d.Default) <= 1e-6f && System.Math.Abs(b.Min - d.Min) <= 1e-6f && System.Math.Abs(b.Max - d.Max) <= 1e-6f;
                    if (same) continue;
                    if (Revised007(d.Source)) continue; // 007 개정 — 007 테스트가 본다
                    changed++;
                    Assert.IsTrue(Revised087(d.Source), $"{d.Id}: 출처 칸에 '087:' 개정 기록이 있어야 한다 (FR-08) — {b.Default} [{b.Min}, {b.Max}] → {d.Default} [{d.Min}, {d.Max}]");
                    Assert.That(d.Default, Is.InRange(d.Min, d.Max), $"{d.Id}: min·max 안이어야 한다");
                }
            Assert.AreEqual(before.Count, seen, "087 시작 계수가 사라지지 않았다");
            TestContext.WriteLine($"087 시작 대비 바뀐 계수 {changed}개");
        }

        /// <summary>specs/007 FR-25: 007이 개정한 계수는 출처 칸에 "007:" 기록(이전 → 새·근거)이 있다.</summary>
        static bool Revised007(string source) => source != null && source.Contains("007:");

        static TuningRegistry Baseline007() =>
            TuningLoader.LoadDefinitionsFile(Path.Combine(Root, "specs", "007-pitch-control-xt", "evidence", "tuning-before.json"));

        /// <summary>
        /// specs/007 FR-25 (087 방식): 007 시작 main 합본 대비 값 또는 범위가 바뀐 계수는 출처 칸에 "007:" 기록이 있고, 태그는 그대로이며 값은 min·max 안이다.
        /// 계수가 사라지지 않는다. 007 신규 계수(`pcm.*`)는 비교 대상이 아니다.
        /// </summary>
        [Test]
        public void Tuning007_ChangedIds_HaveSource()
        {
            var before = Baseline007();
            var changed = 0;
            var seen = 0;
            foreach (var f in Files())
                foreach (var d in TuningLoader.LoadDefinitionsFile(f).Definitions)
                {
                    if (!before.TryGetIndex(d.Id, out var i)) continue;
                    seen++;
                    var b = before.Definitions[i];
                    Assert.AreEqual(b.Tag, d.Tag, $"{d.Id}: 태그가 바뀌면 안 된다");
                    var same = System.Math.Abs(b.Default - d.Default) <= 1e-6f && System.Math.Abs(b.Min - d.Min) <= 1e-6f && System.Math.Abs(b.Max - d.Max) <= 1e-6f;
                    if (same) continue;
                    changed++;
                    Assert.IsTrue(Revised007(d.Source), $"{d.Id}: 출처 칸에 '007:' 개정 기록이 있어야 한다 (FR-25) — {b.Default} [{b.Min}, {b.Max}] → {d.Default} [{d.Min}, {d.Max}]");
                    Assert.That(d.Default, Is.InRange(d.Min, d.Max), $"{d.Id}: min·max 안이어야 한다");
                }
            Assert.AreEqual(before.Count, seen, "007 시작 계수가 사라지지 않았다");
            TestContext.WriteLine($"007 시작 대비 바뀐 계수 {changed}개");
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
                    if (!main.TryGetIndex(d.Id, out var i) || main.Definitions[i].Tag != "[설계]" || Revised084(d.Source) || Revised087(d.Source)) continue;
                    n++;
                    Assert.AreEqual(main.Definitions[i].Default, d.Default, 1e-6f, $"{d.Id}: [설계] 값은 바꾸지 않는다");
                }
            Assert.Greater(n, 0);
        }
    }
}
