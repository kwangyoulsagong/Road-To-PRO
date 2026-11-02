"""E2E 시나리오 형식 검증 테스트 (specs/000 T07, FR-13·20·24·31)."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import scenario  # noqa: E402
from farm.errors import RunnerError  # noqa: E402


def _base(**over):
    d = {
        "schemaVersion": 1,
        "id": "t-sample",
        "spec": "000",
        "scene": "FarmSampleCards",
        "fixture": {"save": None, "seed": 42},
        "profiles": "smoke",
        "steps": [
            {"type": "goto", "screen": "sample"},
            {"type": "capture", "name": "home"},
        ],
    }
    d.update(over)
    return d


class ScenarioTests(unittest.TestCase):
    def test_minimal_ok_and_defaults(self):
        s = scenario.parse(_base())
        self.assertEqual(s.id, "t-sample")
        self.assertEqual(s.defaults["timeoutSec"], 10)
        self.assertEqual(s.defaults["pixelThreshold"], 8)
        self.assertEqual(s.defaults["maxDiffRatio"], 0.001)
        self.assertEqual(s.capture_names(), ["home"])

    def test_capture_inside_perf_rejected(self):
        steps = [
            {"type": "perfBegin", "name": "idle"},
            {"type": "capture", "name": "x"},
            {"type": "perfEnd", "name": "idle"},
        ]
        with self.assertRaises(RunnerError) as cm:
            scenario.parse(_base(steps=steps))
        self.assertIn("성능 구간", str(cm.exception))

    def test_perf_section_must_close_and_match(self):
        for steps in (
            [{"type": "perfBegin", "name": "a"}],
            [{"type": "perfEnd", "name": "a"}],
            [{"type": "perfBegin", "name": "a"}, {"type": "perfEnd", "name": "b"}],
        ):
            with self.assertRaises(RunnerError):
                scenario.parse(_base(steps=steps))

    def test_inject_outside_selftest_rejected(self):
        steps = [{"type": "inject", "what": "warning"}]
        with self.assertRaises(RunnerError) as cm:
            scenario.parse(_base(steps=steps))
        self.assertIn("셀프테스트", str(cm.exception))
        s = scenario.parse(_base(steps=steps, selfTest=True))
        self.assertEqual(s.steps[0]["what"], "warning")

    def test_profiles_all_smoke_list(self):
        self.assertEqual(scenario.parse(_base(profiles="all")).profiles, "all")
        self.assertEqual(scenario.parse(_base(profiles="smoke")).profiles, "smoke")
        self.assertEqual(scenario.parse(_base(profiles=["compact-1366"])).profiles, ["compact-1366"])
        for bad in ("some", [], [1]):
            with self.assertRaises(RunnerError):
                scenario.parse(_base(profiles=bad))

    def test_3d_capture_default_ratio(self):
        steps = [
            {"type": "capture", "name": "ui"},
            {"type": "capture", "name": "pitch", "kind": "3d"},
            {"type": "capture", "name": "custom", "kind": "3d", "maxDiffRatio": 0.02, "pixelThreshold": 12},
        ]
        s = scenario.parse(_base(steps=steps))
        self.assertEqual(s.capture_params("ui"), {"pixelThreshold": 8, "maxDiffRatio": 0.001, "masks": []})
        self.assertEqual(s.capture_params("pitch")["maxDiffRatio"], 0.005)
        self.assertEqual(s.capture_params("custom")["maxDiffRatio"], 0.02)
        self.assertEqual(s.capture_params("custom")["pixelThreshold"], 12)

    def test_required_fields_per_step(self):
        for step in (
            {"type": "tap"},
            {"type": "type", "element": "x"},
            {"type": "waitFor"},
            {"type": "assertNumberRange", "element": "x", "min": 1},
            {"type": "capture"},
            {"type": "freeze", "simTime": 10, "speed": 1.25},
            {"type": "teleport"},
        ):
            with self.assertRaises(RunnerError, msg=step):
                scenario.parse(_base(steps=[step]))

    def test_duplicate_capture_name_rejected(self):
        steps = [{"type": "capture", "name": "a"}, {"type": "capture", "name": "a"}]
        with self.assertRaises(RunnerError):
            scenario.parse(_base(steps=steps))

    def test_mask_rect_ratio_range(self):
        ok = [{"type": "capture", "name": "a", "masks": [{"rect": [0, 0, 0.5, 0.1]}, {"element": "clock"}]}]
        scenario.parse(_base(steps=ok))
        bad = [{"type": "capture", "name": "a", "masks": [{"rect": [0, 0, 1.5, 0.1]}]}]
        with self.assertRaises(RunnerError):
            scenario.parse(_base(steps=bad))

    def test_step_timeout_override(self):
        steps = [{"type": "waitFor", "element": "x", "timeoutSec": 30}]
        s = scenario.parse(_base(steps=steps))
        self.assertEqual(s.step_timeout(0), 30)


if __name__ == "__main__":
    unittest.main()
