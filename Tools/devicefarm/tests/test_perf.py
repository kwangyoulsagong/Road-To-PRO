"""성능 집계·예산·회귀 판정 테스트 (specs/000 T11, FR-40~44, AC-09·10)."""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import perf  # noqa: E402


def section(frames, warmup=0, **over):
    n = len(frames)
    s = {
        "name": "idle",
        "warmupFrames": warmup,
        "frameMs": frames,
        "mainThreadMs": [f * 0.5 for f in frames],
        "gcAllocBytes": [0] * n,
        "drawCalls": [100] * n,
        "triangles": [5000] * n,
        "textureMemoryBytes": 10_000_000,
        "totalMemoryBytes": 400_000_000,
        "power": "ac",
    }
    s.update(over)
    return s


class SummaryTests(unittest.TestCase):
    def test_percentiles(self):
        frames = [float(i) for i in range(1, 101)]  # 1..100
        m = perf.summarize(section(frames))
        f = m["frameMs"]
        self.assertAlmostEqual(f["avg"], 50.5)
        self.assertEqual(f["p50"], 50.0)
        self.assertEqual(f["p95"], 95.0)
        self.assertEqual(f["p99"], 99.0)
        self.assertEqual(f["max"], 100.0)
        self.assertEqual(m["frames"], 100)

    def test_warmup_discarded(self):
        frames = [500.0] * 30 + [10.0] * 100
        m = perf.summarize(section(frames, warmup=30))
        self.assertEqual(m["frameMs"]["max"], 10.0)
        self.assertEqual(m["frames"], 100)

    def test_gc_and_counters(self):
        n = 10
        s = section([10.0] * n, gcAllocBytes=[0] * 9 + [1000], drawCalls=[90] * 9 + [110])
        m = perf.summarize(s)
        self.assertEqual(m["gcAllocTotalBytes"], 1000)
        self.assertEqual(m["gcAllocPerFrameBytes"]["max"], 1000)
        self.assertEqual(m["drawCalls"]["max"], 110)
        self.assertEqual(m["totalMemoryBytes"], 400_000_000)

    def test_empty_section_after_warmup_is_error(self):
        with self.assertRaises(ValueError):
            perf.summarize(section([10.0] * 5, warmup=5))


class JudgeTests(unittest.TestCase):
    def setUp(self):
        self.metrics = perf.summarize(section([10.0] * 95 + [20.0] * 5))

    def test_over_budget_fails(self):
        rows = perf.judge_budget(self.metrics, {"frameMs.p95": 16.6, "drawCalls.max": 150})
        by = {r["metric"]: r for r in rows}
        self.assertTrue(by["drawCalls.max"]["ok"])
        self.assertTrue(by["frameMs.p95"]["ok"])  # p95 = 10
        rows = perf.judge_budget(self.metrics, {"frameMs.max": 16.6})
        self.assertFalse(rows[0]["ok"])
        self.assertEqual(rows[0]["value"], 20.0)

    def test_regression_15pct_fails_5pct_passes(self):
        base = perf.summarize(section([10.0] * 100))
        slow15 = perf.summarize(section([11.5] * 100))
        slow5 = perf.summarize(section([10.5] * 100))
        r15 = {r["metric"]: r for r in perf.judge_regression(slow15, base)}
        r5 = {r["metric"]: r for r in perf.judge_regression(slow5, base)}
        self.assertFalse(r15["frameMs.p95"]["ok"])
        self.assertAlmostEqual(r15["frameMs.p95"]["changePct"], 15.0)
        self.assertTrue(r5["frameMs.p95"]["ok"])
        # 정확히 +10%는 실패 (스펙: +10% 이상)
        r10 = {r["metric"]: r for r in perf.judge_regression(perf.summarize(section([11.0] * 100)), base)}
        self.assertFalse(r10["frameMs.p95"]["ok"])

    def test_regression_zero_base_does_not_divide(self):
        base = perf.summarize(section([10.0] * 10))  # gc 0
        cur = perf.summarize(section([10.0] * 10, gcAllocBytes=[0] * 9 + [64]))
        r = {x["metric"]: x for x in perf.judge_regression(cur, base)}
        self.assertFalse(r["gcAllocTotalBytes"]["ok"])

    def test_non_reference_profile_not_judged(self):
        res = perf.evaluate(self.metrics, {"frameMs.max": 1}, None, is_reference=False, power="ac")
        self.assertFalse(res["judged"])
        self.assertTrue(res["ok"])
        self.assertIn("기준 기기 아님", res["note"])

    def test_battery_power_excluded(self):
        res = perf.evaluate(self.metrics, {"frameMs.max": 1}, None, is_reference=True, power="battery")
        self.assertFalse(res["judged"])
        self.assertTrue(res["ok"])
        res = perf.evaluate(self.metrics, {"frameMs.max": 1}, None, is_reference=True, power="ac")
        self.assertTrue(res["judged"])
        self.assertFalse(res["ok"])

    def test_baseline_store_roundtrip(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "pb.json")
            with open(p, "w") as f:
                json.dump({}, f)
            perf.store_baseline(p, "s/idle/mac-m1-1600p", self.metrics, commit="abc")
            got = perf.load_baseline(p, "s/idle/mac-m1-1600p")
            self.assertEqual(got["frameMs"]["p95"], self.metrics["frameMs"]["p95"])
            self.assertIsNone(perf.load_baseline(p, "nope"))


class PowerAndNullTests(unittest.TestCase):
    def test_power_state(self):
        self.assertEqual(perf.power_state("Charging"), "ac")
        self.assertEqual(perf.power_state("Discharging"), "battery")
        self.assertEqual(perf.power_state("Unknown"), "unknown")
        self.assertEqual(perf.power_state("Unknown", "Now drawing from 'AC Power'"), "ac")
        self.assertEqual(perf.power_state("Charging", "Now drawing from 'Battery Power'"), "battery")

    def test_null_counters_skipped(self):
        m = perf.summarize(section([10.0] * 10, drawCalls=None, triangles=None, textureMemoryBytes=None))
        base = perf.summarize(section([10.0] * 10, textureMemoryBytes=None))
        metrics = {r["metric"] for r in perf.judge_regression(m, base)}
        self.assertIn("frameMs.p95", metrics)
        rows = perf.judge_budget(m, {"textureMemoryBytes": 1})
        self.assertFalse(rows[0]["ok"])


class PerfTestingJsonTests(unittest.TestCase):
    def test_perf_testing_json_read(self):
        data = {
            "Results": [
                {
                    "Name": "RoadToPro.Tests.Performance.SampleSimPerfTests.Simulate_1000Ticks",
                    "SampleGroups": [
                        {"Name": "Time", "Unit": 2, "Median": 1.5, "Min": 1.2, "Max": 2.0, "Average": 1.6, "Samples": [1.2, 1.5, 2.0]},
                        {"Name": "Time.GC()", "Unit": 8, "Median": 0, "Min": 0, "Max": 0, "Average": 0, "Samples": [0]},
                    ],
                }
            ]
        }
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "perf.json")
            with open(p, "w") as f:
                json.dump(data, f)
            rows = perf.read_perf_testing(p)
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["name"], "SampleSimPerfTests.Simulate_1000Ticks")
        self.assertEqual(rows[0]["groups"]["Time"]["median"], 1.5)
        self.assertEqual(rows[0]["groups"]["Time"]["unit"], "ms")
        self.assertEqual(rows[0]["groups"]["Time.GC()"]["unit"], "")


if __name__ == "__main__":
    unittest.main()
