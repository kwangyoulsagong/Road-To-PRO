"""EditMode 성능·결정론 AC 연결 (specs/001 T25 — farm logic 보강)."""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import commands, logic, perf  # noqa: E402

FIX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")

CONFIG = {
    "spec": "001",
    "profile": "mac-m1-1600p",
    "tests": ["RoadToPro.Tests.EditMode.Core", "RoadToPro.Tests.Performance.Core"],
    "budgets": [
        {"ac": "AC-18", "label": "RNG 균등 1회", "test": "CorePerfTests.Rng_Uniform_ns", "group": "ns_per_op", "stat": "median", "max": 5, "unit": "ns"},
        {"ac": "AC-18", "label": "해시 3만", "test": "CorePerfTests.Hash_30kEvents_ms", "group": "Time", "stat": "median", "max": 10, "unit": "ms"},
        {"ac": "AC-18", "label": "할당", "test": "CorePerfTests.ZeroAlloc_MathRngClockLog", "group": "gc_alloc_count", "stat": "max", "max": 0, "unit": ""},
    ],
}

INFO = {"id": "001", "acs": [
    {"id": "AC-03", "kind": "로직", "text": "x"},
    {"id": "AC-06", "kind": "결정론", "text": "x"},
    {"id": "AC-18", "kind": "성능", "text": "x"},
]}


def rows():
    return perf.read_perf_testing(os.path.join(FIX, "perf-editmode.json"))


class LogicSpecTests(unittest.TestCase):
    def test_categories_parsed(self):
        r = logic.parse_results(os.path.join(FIX, "nunit-categories.xml"))
        cats = {c["name"]: c["categories"] for c in r.cases}
        self.assertEqual(cats["SeedRngTests.SameSeed"], ["Determinism"])
        self.assertEqual(cats["PitchCoordsTests.Center"], [])

    def test_determinism_ac_linked(self):
        r = logic.parse_results(os.path.join(FIX, "nunit-categories.xml"))
        j = perf.judge_editmode(rows(), CONFIG, base=None, is_reference=True, power="ac")
        ev = commands.ac_evidence(INFO, r.to_dict() | {"ok": r.ok}, r, j)
        self.assertTrue(all(i["ok"] for i in ev["AC-06"]))
        self.assertIn("결정론", ev["AC-06"][0]["label"])
        # 결정론 테스트 하나가 실패하면 결정론 AC도 실패
        r.cases[0]["result"] = "Failed"
        ev2 = commands.ac_evidence(INFO, {"ok": False}, r, j)
        self.assertFalse(all(i["ok"] for i in ev2["AC-06"]))

    def test_determinism_ac_without_tests_has_no_evidence(self):
        r = logic.parse_results(os.path.join(FIX, "nunit-categories.xml"))
        for c in r.cases:
            c["categories"] = []
        ev = commands.ac_evidence(INFO, {"ok": True}, r, None)
        self.assertFalse(all(i["ok"] for i in ev["AC-06"]))

    def test_perf_results_written_and_judged(self):
        j = perf.judge_editmode(rows(), CONFIG, base=None, is_reference=True, power="ac")
        self.assertTrue(j["judged"])
        self.assertTrue(j["ok"], j)
        self.assertEqual(len(j["budget"]), 3)
        self.assertEqual(j["budget"][0]["value"], 4.0)
        r = logic.parse_results(os.path.join(FIX, "nunit-categories.xml"))
        ev = commands.ac_evidence(INFO, {"ok": True}, r, j)
        self.assertTrue(all(i["ok"] for i in ev["AC-18"]))
        with tempfile.TemporaryDirectory() as d:
            p = perf.write_editmode_perf(d, rows(), j)
            with open(p, encoding="utf-8") as f:
                data = json.load(f)
            self.assertIn("results", data)
            self.assertIn("judgement", data)

    def test_over_budget_fails(self):
        cfg = json.loads(json.dumps(CONFIG))
        cfg["budgets"][0]["max"] = 3.0
        j = perf.judge_editmode(rows(), cfg, base=None, is_reference=True, power="ac")
        self.assertFalse(j["ok"])
        bad = [b for b in j["budget"] if not b["ok"]]
        self.assertEqual(bad[0]["label"], "RNG 균등 1회")

    def test_missing_measurement_fails(self):
        cfg = json.loads(json.dumps(CONFIG))
        cfg["budgets"].append({"ac": "AC-18", "label": "없음", "test": "CorePerfTests.Nope", "group": "Time", "stat": "median", "max": 1, "unit": "ms"})
        j = perf.judge_editmode(rows(), cfg, base=None, is_reference=True, power="ac")
        self.assertFalse(j["ok"])

    def test_regression_against_baseline(self):
        base = perf.editmode_metrics(rows(), CONFIG)
        faster = perf.judge_editmode(rows(), CONFIG, base=base, is_reference=True, power="ac")
        self.assertTrue(faster["ok"])
        worse = {k: v / 1.2 if v else v for k, v in base.items()}  # 기준이 더 빨랐다 = 이번이 +20%
        j = perf.judge_editmode(rows(), CONFIG, base=worse, is_reference=True, power="ac")
        self.assertFalse(j["ok"])
        self.assertTrue(any(not r["ok"] for r in j["regression"]))

    def test_non_reference_host_record_only(self):
        cfg = json.loads(json.dumps(CONFIG))
        cfg["budgets"][0]["max"] = 1.0
        j = perf.judge_editmode(rows(), cfg, base=None, is_reference=False, power="ac")
        self.assertFalse(j["judged"])
        self.assertTrue(j["ok"])
        j2 = perf.judge_editmode(rows(), cfg, base=None, is_reference=True, power="battery")
        self.assertFalse(j2["judged"])

    def test_spec_filter_namespaces(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "001-core-foundation.json")
            with open(p, "w", encoding="utf-8") as f:
                json.dump(CONFIG, f)
            cfg = perf.load_spec_config("001", perf_dir=d)
            self.assertEqual(perf.test_filter(cfg), "RoadToPro.Tests.EditMode.Core;RoadToPro.Tests.Performance.Core")
            self.assertIsNone(perf.load_spec_config("999", perf_dir=d))

    def test_real_config_matches_spec_table(self):
        """Data/perf/001-core-foundation.json이 있으면 형식이 맞아야 한다."""
        cfg = perf.load_spec_config("001")
        if cfg is None:
            self.skipTest("Data/perf/001 설정 없음")
        for b in cfg["budgets"]:
            for k in ("ac", "label", "test", "group", "stat", "max"):
                self.assertIn(k, b)


class CmdLogicVerdictTests(unittest.TestCase):
    def test_unjudged_perf_makes_spec_verdict_fail(self):
        """배터리 등으로 성능 판정이 빠지면 AC-18 증거가 없으므로 완료 판정(requireAllAcs)은 실패 (T26a)."""
        from farm import report
        r = logic.parse_results(os.path.join(FIX, "nunit-categories.xml"))
        j = perf.judge_editmode(rows(), CONFIG, base=None, is_reference=True, power="battery")
        ev = {"spec": INFO, "logic": {"ok": True}, "scenarios": [], "requireAllAcs": True, "editmodePerf": j,
              "acEvidence": commands.ac_evidence(INFO, {"ok": True}, r, j)}
        self.assertEqual(report.verdict_of(ev), "fail")


if __name__ == "__main__":
    unittest.main()
