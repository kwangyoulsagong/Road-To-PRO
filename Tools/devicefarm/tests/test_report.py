"""증거 리포트 생성 테스트 (specs/000 T31, FR-70~73, AC-14)."""
import os
import sys
import tempfile
import unittest

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import report  # noqa: E402

META = {"commit": "0123456789abcdef", "branch": "feat/000-verification-harness", "time": "2026-11-02 15:00:00 +0900",
        "dirty": False, "unity": "6000.3.27f2", "durationSec": 42.0,
        "command": "python3 Tools/devicefarm/farm.py smoke --scenario 000-sample-cards", "profiles": ["a", "b"]}


def img(tmp, name, color, size=(800, 500)):
    p = os.path.join(tmp, name)
    a = np.zeros((size[1], size[0], 3), np.uint8)
    a[:] = color
    a[50:150, 50:300] = 255
    Image.fromarray(a).save(p)
    return p


def evidence(tmp, n_combos=2, noise=False):
    combos = []
    for i in range(n_combos):
        cur = img(tmp, f"cur{i}.png", (240, 240, 240))
        if noise:
            a = (np.random.RandomState(i).rand(1600, 2560, 3) * 255).astype(np.uint8)
            cur = os.path.join(tmp, f"noise{i}.png")
            Image.fromarray(a).save(cur)
        combos.append({
            "key": f"p{i}-light-ko", "profile": f"p{i}", "theme": "light", "lang": "ko", "status": "passed",
            "reason": "", "verdict": "pass", "durationSec": 5.0, "args": ["-farmProfile", f"p{i}"],
            "captures": [
                {"step": "home", "current": cur, "baseline": cur, "diff": None, "status": "pass", "diffRatio": 0.0, "reason": ""},
                {"step": "confirmed", "current": cur, "baseline": None, "diff": None, "status": "candidate", "diffRatio": 0.0, "reason": "기준 후보"},
            ],
            "failCaptures": [],
            "perf": [{"section": "idle", "judged": i == 0, "ok": True, "note": "" if i == 0 else "기준 기기 아님",
                      "metrics": {"frames": 120, "frameMs": {"avg": 16.7, "p50": 16.7, "p95": 17.0, "p99": 18.0, "max": 20.0},
                                  "mainThreadMs": {"p95": 3.0}, "gcAllocTotalBytes": 0, "drawCalls": {"max": 6},
                                  "triangles": {"max": 300}, "textureMemoryBytes": 70_000_000, "totalMemoryBytes": 400_000_000},
                      "budget": [], "regression": []}],
            "logs": {"ok": True, "errors": [], "warnings": [], "ignored": []},
            "checkpoints": [],
        })
    return {
        "title": "000 검증 하네스 · 증거",
        "spec": {"id": "000", "name": "검증 하네스", "acs": [
            {"id": "AC-01", "kind": "로직", "text": "실패 1건이 맨 위"},
            {"id": "AC-03", "kind": "E2E", "text": "캡처 3장"},
        ]},
        "meta": dict(META),
        "scenarios": [{"scenario": "000-sample-cards", "description": "샘플", "combos": combos, "determinism": None, "verdict": "candidate"}],
        "acEvidence": {"AC-03": [{"label": "smoke 캡처", "ok": True}]},
        "selftests": [],
        "budgets": [{"item": "smoke 매트릭스", "budget": "≤ 5분", "value": "42s", "ok": True}],
    }


class ReportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.out = os.path.join(self.tmp.name, "evidence")

    def tearDown(self):
        self.tmp.cleanup()

    def render(self, ev):
        path = report.write(ev, self.out)
        with open(path, encoding="utf-8") as f:
            return f.read()

    def test_header_has_commit_branch_time(self):
        html = self.render(evidence(self.tmp.name))
        self.assertIn("0123456789", html)
        self.assertIn("feat/000-verification-harness", html)
        self.assertIn("2026-11-02 15:00:00", html)
        self.assertIn("6000.3.27f2", html)

    def test_dirty_worktree_warning(self):
        ev = evidence(self.tmp.name)
        self.assertNotIn("커밋 안 된 변경", self.render(ev))
        ev["meta"]["dirty"] = True
        html = self.render(ev)
        self.assertIn("커밋 안 된 변경", html)
        self.assertIn("완료 판정 불가", html)

    def test_grid_rows_steps_cols_profiles(self):
        html = self.render(evidence(self.tmp.name))
        grid = html[html.index('class="grid"'):]
        grid = grid[: grid.index("</table>")]
        self.assertEqual(grid.count("<th scope=\"col\""), 2)  # 열 = 조합 2개
        self.assertEqual(grid.count("<th scope=\"row\""), 2)  # 행 = 단계 2개
        self.assertIn("기준 후보", grid)
        self.assertTrue(os.path.exists(os.path.join(self.out, "assets")))
        thumbs = [f for _, _, fs in os.walk(os.path.join(self.out, "assets")) for f in fs]
        self.assertTrue(any(f.endswith(".jpg") for f in thumbs))

    def test_ac_link_table(self):
        html = self.render(evidence(self.tmp.name))
        table = html[html.index('id="ac"'):]
        self.assertIn("AC-01", table)
        self.assertIn("증거 없음", table)  # AC-01에 연결된 증거가 없다
        self.assertIn("smoke 캡처", table)

    def test_size_cap_downscale(self):
        ev = evidence(self.tmp.name, n_combos=2, noise=True)
        full = report.write(ev, os.path.join(self.tmp.name, 'full'))
        full_size = sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(os.path.join(self.tmp.name, 'full', 'assets')) for f in fs)
        cap = full_size // 2
        report.write(ev, self.out, max_bytes=cap)
        size = sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(os.path.join(self.out, "assets")) for f in fs)
        self.assertLessEqual(size, cap)
        with open(os.path.join(self.out, "report.html"), encoding="utf-8") as f:
            self.assertIn("축소", f.read())

    def test_verdict_requires_all_acs_only_for_completion(self):
        ev = evidence(self.tmp.name)
        self.assertEqual(report.verdict_of(ev), "candidate")  # AC-01 증거 없음은 일반 실행에서 무시
        ev["requireAllAcs"] = True
        self.assertEqual(report.verdict_of(ev), "fail")

    def test_run_command_recorded(self):
        html = self.render(evidence(self.tmp.name))
        self.assertIn("farm.py smoke --scenario 000-sample-cards", html)
        self.assertTrue(os.path.exists(os.path.join(self.out, "run.json")))

    def test_layout_probe_script_present(self):
        html = self.render(evidence(self.tmp.name))
        self.assertIn("data-layout", html)
        self.assertIn('name="viewport"', html)


if __name__ == "__main__":
    unittest.main()
