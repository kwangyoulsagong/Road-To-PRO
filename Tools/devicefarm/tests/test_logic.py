"""로직 테스트 결과 해석·리포트 테스트 (specs/000 T15, FR-01~03, AC-01·02)."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import logic  # noqa: E402

FIX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")


class ParseTests(unittest.TestCase):
    def test_nunit_counts_and_cases(self):
        r = logic.parse_results(os.path.join(FIX, "nunit-1fail-2pass.xml"))
        self.assertEqual((r.total, r.passed, r.failed, r.skipped), (3, 2, 1, 0))
        self.assertFalse(r.ok)
        f = r.failures()[0]
        self.assertEqual(f["name"], "SelfTestFixtures.Fail_Deliberate")
        self.assertIn("의도한 실패", f["message"])
        self.assertIn("SelfTestFixtures.cs:18", f["stack"])

    def test_failures_listed_first(self):
        r = logic.parse_results(os.path.join(FIX, "nunit-1fail-2pass.xml"))
        ordered = r.ordered_cases()
        self.assertEqual(ordered[0]["result"], "Failed")
        html = logic.render_html(r, {"commit": "abc1234", "branch": "feat/x", "unity": "6000.3.27f2", "time": "t"})
        i_fail = html.index("Fail_Deliberate")
        i_pass = html.index("Pass_One")
        self.assertLess(i_fail, i_pass)
        self.assertIn("의도한 실패", html)
        self.assertIn('class="verdict fail"', html)

    def test_compile_error_extracted(self):
        with open(os.path.join(FIX, "compile-error.log"), encoding="utf-8") as f:
            text = f.read()
        errs = logic.extract_compile_errors(text)
        self.assertEqual(len(errs), 2)
        self.assertTrue(all("error CS" in e for e in errs))
        warns = logic.extract_compile_warnings(text)
        self.assertEqual(len(warns), 1)
        r = logic.compile_failure_result(errs)
        self.assertFalse(r.ok)
        html = logic.render_html(r, {"commit": "c", "branch": "b", "unity": "u", "time": "t"})
        self.assertIn("error CS1002", html)
        self.assertIn("컴파일 에러", html)

    def test_metadata_commit_branch_unity(self):
        m = logic.metadata()
        for k in ("commit", "branch", "unity", "time", "dirty"):
            self.assertIn(k, m)
        self.assertEqual(m["unity"], "6000.3.27f2")
        self.assertRegex(m["commit"], r"^[0-9a-f]{7,40}$")
        html = logic.render_html(logic.parse_results(os.path.join(FIX, "nunit-1fail-2pass.xml")), m)
        self.assertIn(m["commit"][:7], html)
        self.assertIn(m["branch"], html)
        self.assertIn("6000.3.27f2", html)

    def test_code_dirty_ignores_evidence_and_docs(self):
        # 2026-11-12 검증 경량화: 증거·문서만 바뀌면 재실행이 필요 없다
        self.assertFalse(logic.code_dirty(" M specs/008-x/tasks.md\n?? specs/008-x/evidence/report.html\n M docs/index.html\n"))
        self.assertFalse(logic.code_dirty(""))
        self.assertTrue(logic.code_dirty(" M Assets/_Project/MatchEngine/Runtime/A.cs\n"))
        self.assertTrue(logic.code_dirty("?? Data/tuning/action.json\n M specs/a.md\n"))
        self.assertTrue(logic.code_dirty('R  specs/old.md -> Tools/devicefarm/farm/x.py\n'))

    def test_scope_filter(self):
        import re
        self.assertEqual(logic.scope_filter(None), "")
        self.assertEqual(logic.scope_filter("match,all"), "")
        rx = re.compile(logic.scope_filter("match"))
        self.assertTrue(rx.search("RoadToPro.Tests.EditMode.MatchEngine.Ball.BallStepperTests.X"))
        self.assertTrue(rx.search("RoadToPro.Tests.EditMode.Presentation.Highlight.LiveTests.Y"))
        self.assertTrue(rx.search("RoadToPro.Tests.EditMode.Architecture.AsmdefTests.Z"))
        self.assertFalse(rx.search("RoadToPro.Tests.EditMode.Presentation.Players.HairTests.W"))
        self.assertFalse(rx.search("RoadToPro.Tests.EditMode.Verification.DriverTests.V"))
        with self.assertRaises(ValueError):
            logic.scope_filter("nope")

    def test_auto_scope_follows_dependencies(self):
        a = logic.auto_scope
        self.assertEqual(a(["Assets/_Project/MatchEngine/Runtime/A.cs", "Data/tuning/action.json", "specs/008/plan.md"]), "match")
        self.assertEqual(a(["Assets/_Project/Presentation/X.cs", "Assets/_Project/Tests/EditMode/MatchEngine/T.cs"]), "presentation,match")
        self.assertEqual(a(["Assets/_Project/Core/Runtime/Math/Vec2.cs", "Assets/_Project/Presentation/X.cs"]), "all")
        self.assertEqual(a(["Assets/_Project/Career/Runtime/Y.cs"]), "all")  # 규칙 없는 새 모듈은 안전하게 전체
        self.assertEqual(a(["specs/008/tasks.md", "docs/index.html"]), "")
        self.assertEqual(a(["Tools/devicefarm/farm/logic.py"]), "")  # Unity 테스트와 무관 (Python 테스트로 검증)

    def test_html_escapes(self):
        r = logic.compile_failure_result(["A.cs(1,1): error CS0001: <script>x</script>"])
        html = logic.render_html(r, {"commit": "c", "branch": "b", "unity": "u", "time": "t"})
        self.assertNotIn("<script>x", html)

    def test_all_pass_verdict(self):
        r = logic.LogicResult(total=1, passed=1, failed=0, skipped=0, duration=0.1, cases=[
            {"name": "A.B", "fullname": "X.A.B", "result": "Passed", "duration": 0.1, "message": "", "stack": ""}
        ])
        self.assertTrue(r.ok)
        self.assertIn('class="verdict pass"', logic.render_html(r, {"commit": "c", "branch": "b", "unity": "u", "time": "t"}))

    def test_zero_tests_is_not_pass(self):
        r = logic.LogicResult(total=0, passed=0, failed=0, skipped=0, duration=0, cases=[])
        self.assertFalse(r.ok)


if __name__ == "__main__":
    unittest.main()
