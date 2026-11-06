"""specs/054 AC-16: 움직임 지표 리포트 — 그래프는 항상, 위반 프레임 캡처는 위반이 있을 때만."""
import json
import os
import tempfile
import unittest

from farm import motion


def scene(violations=()):
    return {"scene": "tackle", "rate": 1.5, "violations": list(violations), "contacts": [],
            "stats": {"rootError": {"p50": 0.01, "p95": 0.02, "max": 0.03, "count": 5}},
            "series": {"rootError": [[0.0, 0.01], [0.1, 0.03]], "slide": [[0.0, 0.0]]}}


class FakeAssets:
    def copy(self, src, rel):
        return "assets/" + rel


class Load(unittest.TestCase):
    def test_missing_file_none(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertIsNone(motion.load(d))

    def test_reads_scenes_and_captures(self):
        with tempfile.TemporaryDirectory() as d:
            with open(os.path.join(d, "motion.json"), "w", encoding="utf-8") as f:
                json.dump({"scenes": [scene()]}, f)
            os.makedirs(os.path.join(d, "motion"))
            with open(os.path.join(d, "motion", "violation_a.png"), "wb") as f:
                f.write(b"x")
            with open(os.path.join(d, "motion", "note.txt"), "wb") as f:
                f.write(b"x")
            doc = motion.load(d)
            self.assertEqual(1, len(doc["scenes"]))
            self.assertEqual(["violation_a.png"], [c["file"] for c in doc["captures"]])


class Spark(unittest.TestCase):
    def test_empty(self):
        self.assertEqual("", motion.sparkline([]))

    def test_single_point_zero_values_no_crash(self):
        self.assertIn("<svg", motion.sparkline([[0.0, 0.0]]))

    def test_scaled_max_label(self):
        self.assertIn("최대 3.00", motion.sparkline([[0.0, 0.01], [1.0, 0.03]], 100.0))


class Section(unittest.TestCase):
    def combo(self, doc):
        return [{"combos": [{"key": "mac-m1-1600p_light_ko", "motion": doc}]}]

    def test_no_motion_empty(self):
        self.assertEqual("", motion.section([{"combos": [{"key": "a", "motion": None}]}], FakeAssets()))

    def test_no_violation_graph_only(self):
        html = motion.section(self.combo({"scenes": [scene()], "captures": []}), FakeAssets())
        self.assertIn("<svg", html)
        self.assertNotIn("<h3>위반 프레임", html)
        self.assertIn("위반 0건", html)

    def test_violation_attaches_capture(self):
        doc = {"scenes": [scene([{"metric": "slide", "time": 1.0, "entity": 0, "value": 0.1, "limit": 0.05}])],
               "captures": [{"file": "violation_slide.png", "path": "/x/violation_slide.png"}]}
        html = motion.section(self.combo(doc), FakeAssets())
        self.assertIn("<h3>위반 프레임", html)
        self.assertIn("violation_slide.png", html)
        self.assertIn("위반 1건", html)
        self.assertEqual(1, motion.violation_count(doc))


class Standards(unittest.TestCase):
    def test_previous_shown_with_current(self):
        html = motion.standards_html({"version": 2, "thresholds": {"slideMax": {"max": 0.05, "unit": "m"}}, "previous": {"slideMax": {"max": 0.08}}})
        self.assertIn("≤ 0.05", html)
        self.assertIn("≤ 0.08", html)

    def test_no_previous_only_version(self):
        html = motion.standards_html({"version": 1, "thresholds": {"a": {"max": 1}}, "previous": {}})
        self.assertIn("버전 1", html)
        self.assertNotIn("<table>", html)

    def test_real_file_loads(self):
        self.assertGreater(len(motion.standards()["thresholds"]), 10)


class Charts(unittest.TestCase):
    def test_contact_charts_and_unexpected_fault(self):
        s = scene()
        s["contacts"] = [{"time": 1.0, "timeError": 0.01, "distance": 0.02, "limit": 0.05, "missed": False, "isKick": False}]
        s["simFaults"] = [3]
        s["expectSimFault"] = False
        html = motion.section([{"combos": [{"key": "k", "motion": {"scenes": [s], "captures": []}}]}], FakeAssets())
        self.assertIn("접촉 시각 차", html)
        self.assertIn("접촉 거리", html)
        self.assertIn("시뮬 실패", html)


if __name__ == "__main__":
    unittest.main()
