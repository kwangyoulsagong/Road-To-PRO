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
