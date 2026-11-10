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


class GpuMs044(unittest.TestCase):
    """specs/044: GPU 프레임 시간 집계, 측정 불가(null)는 예산 실패."""

    def _sec(self, gpu):
        return {"name": "s", "warmupFrames": 1, "frameMs": [16.0, 16.0, 16.0], "gpuMs": gpu}

    def test_gpu_stats(self):
        from farm import perf as P
        m = P.summarize(self._sec([9.0, 1.0, 2.0]))
        self.assertEqual(m["gpuMs"]["max"], 2.0)
        self.assertEqual(m["gpuMs"]["p95"], 2.0)

    def test_gpu_null_fails_budget(self):
        from farm import perf as P
        m = P.summarize(self._sec(None))
        self.assertIsNone(m["gpuMs"])
        rows = P.judge_budget(m, {"gpuMs.p95": 1.0})
        self.assertFalse(rows[0]["ok"])

    def test_gpu_in_regression_metrics(self):
        from farm import perf as P
        self.assertIn("gpuMs.p95", P.REGRESSION_METRICS)


class Derived044(unittest.TestCase):
    def test_set_get_metric(self):
        from farm import perf as P
        m = {}
        P.set_metric(m, "gpuMs.p95", 1.5)
        self.assertEqual(P.get_metric(m, "gpuMs.p95"), 1.5)
        self.assertIsNone(P.get_metric(m, "drawCalls.max"))
        rows = P.judge_budget(m, {"gpuMs.p95": 1.0, "drawCalls.max": 10})
        self.assertEqual([r["ok"] for r in rows], [False, False])


class CustomCounters(unittest.TestCase):
    """specs/043: 화면이 넘긴 사용자 정의 카운터 집계·예산."""

    def test_custom_stats_and_budget(self):
        from farm import perf as P
        sec = {"name": "anim", "warmupFrames": 2, "frameMs": [16.0] * 6,
               "custom": {"animCpuMs": [9.0, 9.0, 1.0, 1.2, 1.4, None], "animGcBytes": [0, 0, 0, 0, 0, 0]}}
        m = P.summarize(sec)
        self.assertAlmostEqual(m["custom"]["animCpuMs"]["max"], 1.4)
        self.assertEqual(m["custom"]["animGcBytes"]["max"], 0)
        rows = P.judge_budget(m, {"custom.animCpuMs.p95": 2.0, "custom.animGcBytes.max": 0})
        self.assertTrue(all(r["ok"] for r in rows), rows)
        rows = P.judge_budget(m, {"custom.missing.p95": 2.0})
        self.assertFalse(rows[0]["ok"], "없는 카운터는 측정 불가 = 실패")


class RenderPairs(unittest.TestCase):
    """specs/043: 렌더 카운터 '0 → 두 배' 쌍 보정."""

    def test_fold(self):
        from farm import perf as P
        self.assertEqual(P.fold_render_pairs([60, 0, 120, 60, 60]), [60, 60.0, 60.0, 60, 60])
        self.assertEqual(P.fold_render_pairs([60, 122, 0, 60]), [60, 61.0, 61.0, 60])
        self.assertEqual(P.fold_render_pairs([0, 0, 2, 2]), [0, 0, 2, 2], "진짜 0(빈 화면)은 그대로")
        self.assertEqual(P.fold_render_pairs([60, 65, 70]), [60, 65, 70])


class RenderPairsNear(unittest.TestCase):
    def test_non_adjacent(self):
        from farm import perf as P
        self.assertEqual(P.fold_render_pairs([61, 0, 61, 61, 122, 61]), [61, 61.0, 61, 61, 61.0, 61])


class RelativeBudgetTests(unittest.TestCase):
    """specs/048: 한도 = max(기준값, 비교 지표) — 기능이 유발한 몫만 판정."""

    def test_or_metric_same_section(self):
        m = {"custom": {"transitionFrameMs": {"max": 28.0}}, "outsideTransitionFrameMs": {"max": 31.0}}
        rows = perf.judge_budget(m, {"custom.transitionFrameMs.max": {"max": 25, "orMetric": "outsideTransitionFrameMs.max"}})
        self.assertTrue(rows[0]["ok"])
        self.assertEqual(rows[0]["budget"], 31.0)
        m["custom"]["transitionFrameMs"]["max"] = 32.0
        self.assertFalse(perf.judge_budget(m, {"custom.transitionFrameMs.max": {"max": 25, "orMetric": "outsideTransitionFrameMs.max"}})[0]["ok"])

    def test_or_section_and_floor(self):
        m = {"frameMs": {"max": 30.0}}
        sections = {"empty": {"frameMs": {"max": 20.0}}}
        rows = perf.judge_budget(m, {"frameMs.max": {"max": 33, "orSection": "empty", "orMetric": "frameMs.max"}}, sections)
        self.assertTrue(rows[0]["ok"])
        self.assertEqual(rows[0]["budget"], 33)
        m["frameMs"]["max"] = 34.0
        self.assertFalse(perf.judge_budget(m, {"frameMs.max": {"max": 33, "orSection": "empty", "orMetric": "frameMs.max"}}, sections)[0]["ok"])

    def test_outside_transition_metric(self):
        sec = {"name": "p", "warmupFrames": 1, "frameMs": [99, 16, 30, 17, 40],
               "custom": {"transitionFrameMs": [None, None, 30, None, None]}}
        m = perf.summarize(sec)
        self.assertEqual(m["outsideTransitionFrameMs"]["max"], 40)


class AdvisoryBudgetTests(unittest.TestCase):
    """specs/048 AC-16: 하위 시스템 몫은 기록 전용 — 초과를 표시하되 판정은 통과."""

    def test_over_budget_is_marked_but_passes(self):
        ev = perf.evaluate({"gpuMs": {"avg": 1.7}}, {"gpuMs.avg": 1.0}, None, True, "ac")
        self.assertFalse(ev["ok"])
        adv = perf.advisory(ev)
        self.assertTrue(adv["ok"])
        self.assertTrue(adv["advisoryOver"])
        self.assertFalse(adv["budget"][0]["ok"])

    def test_within_budget_not_marked(self):
        adv = perf.advisory(perf.evaluate({"gpuMs": {"avg": 0.5}}, {"gpuMs.avg": 1.0}, None, True, "ac"))
        self.assertTrue(adv["ok"])
        self.assertFalse(adv["advisoryOver"])


class OrMarginTests(unittest.TestCase):
    """specs/048 AC-18: 전환 최대 ≤ max(25, 전환 밖 최대 + 1 ms)."""

    def test_margin_added_to_compared_metric(self):
        b = {"t.max": {"max": 25, "orMetric": "o.max", "orMargin": 1}}
        m = {"t": {"max": 30.135}, "o": {"max": 30.103}}
        rows = perf.judge_budget(m, b)
        self.assertTrue(rows[0]["ok"])
        self.assertAlmostEqual(rows[0]["budget"], 31.103)
        self.assertEqual(rows[0]["rule"], "max(25, o.max + 1)")

    def test_margin_does_not_raise_floor(self):
        rows = perf.judge_budget({"t": {"max": 25.5}, "o": {"max": 20}}, {"t.max": {"max": 25, "orMetric": "o.max", "orMargin": 1}})
        self.assertFalse(rows[0]["ok"])
