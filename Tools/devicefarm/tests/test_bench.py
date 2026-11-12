"""시뮬 벤치 판정·설정 복원 테스트 (specs/006 T21)."""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import bench, perf  # noqa: E402

CFG = {"profile": "mac-m1-1600p", "il2cpp": {"budgets": [
    {"ac": "AC-21", "label": "뼈대", "metric": "skeleton_match_ms", "max": 100, "unit": "ms"},
    {"ac": "AC-21", "label": "물리", "metric": "physics_us_per_tick", "max": 67, "unit": "µs"},
]}}


class JudgeTests(unittest.TestCase):
    def test_il2cpp_reference_ac_within_budget_passes(self):
        j = bench.judge({"skeleton_match_ms": 80, "physics_us_per_tick": 40, "stage_us.Physics": 40}, CFG, "il2cpp", None, True, "ac")
        self.assertTrue(j["judged"])
        self.assertTrue(j["ok"])
        self.assertEqual(j["stages"], {"Physics": 40})

    def test_over_budget_fails(self):
        j = bench.judge({"skeleton_match_ms": 120, "physics_us_per_tick": 40}, CFG, "il2cpp", None, True, "ac")
        self.assertFalse(j["ok"])
        self.assertFalse([r for r in j["budget"] if r["metric"] == "skeleton_match_ms"][0]["ok"])

    def test_regression_over_10pct_fails(self):
        j = bench.judge({"skeleton_match_ms": 90, "physics_us_per_tick": 40}, CFG, "il2cpp", {"skeleton_match_ms": 80, "physics_us_per_tick": 40}, True, "ac")
        self.assertFalse(j["ok"])
        self.assertEqual(len(j["regression"]), 2)

    def test_mono_backend_records_only(self):
        j = bench.judge({"skeleton_match_ms": 500, "physics_us_per_tick": 400}, CFG, "mono", None, True, "ac")
        self.assertFalse(j["judged"])
        self.assertTrue(j["ok"])
        self.assertIn("IL2CPP", j["note"])

    def test_missing_metric_fails(self):
        j = bench.judge({"skeleton_match_ms": 50}, CFG, "il2cpp", None, True, "ac")
        self.assertFalse(j["ok"])

    def test_battery_not_judged(self):
        # specs/000 개정 2026-11-12: 저전력 모드 배터리만 판정 제외, 그냥 배터리는 판정
        j = bench.judge({"skeleton_match_ms": 500, "physics_us_per_tick": 40}, CFG, "il2cpp", None, True, "battery-lowpower")
        self.assertFalse(j["judged"])
        j = bench.judge({"skeleton_match_ms": 500, "physics_us_per_tick": 40}, CFG, "il2cpp", None, True, "battery")
        self.assertTrue(j["judged"])


class RestoreSettingsTests(unittest.TestCase):
    def test_restores_changed_bytes_only(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "ProjectSettings.asset")
            with open(p, "wb") as f:
                f.write(b"before")
            self.assertFalse(bench.restore_settings(b"before", log=lambda m: None, path=p))
            with open(p, "wb") as f:
                f.write(b"after")
            self.assertTrue(bench.restore_settings(b"before", log=lambda m: None, path=p))
            with open(p, "rb") as f:
                self.assertEqual(f.read(), b"before")


class EditModeNullBudgetTests(unittest.TestCase):
    def test_null_max_is_regression_only(self):
        cfg = {"profile": "mac-m1-1600p", "budgets": [
            {"ac": "AC-21", "label": "회귀만", "test": "T.A", "group": "ms", "stat": "median", "max": None, "unit": "ms"}]}
        rows = [{"name": "T.A", "groups": {"ms": {"median": 999.0}}}]
        j = perf.judge_editmode(rows, cfg, None, True, "ac")
        self.assertTrue(j["ok"])
        self.assertEqual(j["budget"][0]["budget"], "—")
        j2 = perf.judge_editmode(rows, cfg, {"T.A/ms/median": 500.0}, True, "ac")
        self.assertFalse(j2["ok"], "회귀 +10% 초과는 실패")


class RegressionOptOutTests(unittest.TestCase):
    def test_regression_false_records_only(self):
        cfg = {"profile": "mac-m1-1600p", "budgets": [
            {"ac": "AC-21", "label": "기록만", "test": "T.A", "group": "ms", "stat": "median", "max": None, "unit": "ms", "regression": False}]}
        rows = [{"name": "T.A", "groups": {"ms": {"median": 999.0}}}]
        j = perf.judge_editmode(rows, cfg, {"T.A/ms/median": 100.0}, True, "ac")
        self.assertTrue(j["ok"])
        self.assertEqual(j["regression"], [])


class DeferredEvidenceTests(unittest.TestCase):
    def test_deferred_logic_ac_not_auto_passed(self):
        from farm import commands
        src = open(commands.__file__, encoding="utf-8").read()
        self.assertIn("if k not in deferred", src)


if __name__ == "__main__":
    unittest.main()
