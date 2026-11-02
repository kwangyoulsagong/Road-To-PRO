"""결정론 체크포인트 비교 테스트 (specs/000 T13, FR-50·51, AC-11·16)."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import determinism  # noqa: E402


def cps(hashes):
    return [{"name": f"t{i * 100}", "tick": i * 100, "hash": h} for i, h in enumerate(hashes)]


class DeterminismTests(unittest.TestCase):
    def test_equal_lists_pass(self):
        r = determinism.compare_runs(cps(["a", "b", "c"]), cps(["a", "b", "c"]))
        self.assertTrue(r["ok"])
        self.assertIsNone(r["firstDivergence"])

    def test_first_divergence_reported(self):
        r = determinism.compare_runs(cps(["a", "b", "c", "d"]), cps(["a", "b", "X", "Y"]))
        self.assertFalse(r["ok"])
        fd = r["firstDivergence"]
        self.assertEqual((fd["index"], fd["name"], fd["tick"]), (2, "t200", 200))
        self.assertEqual((fd["a"], fd["b"]), ("c", "X"))

    def test_length_mismatch_reports_first_missing(self):
        r = determinism.compare_runs(cps(["a", "b", "c"]), cps(["a", "b"]))
        self.assertFalse(r["ok"])
        self.assertEqual(r["firstDivergence"]["index"], 2)
        self.assertIsNone(r["firstDivergence"]["b"])

    def test_cross_profile_equal(self):
        runs = {"p1-light-ko": cps(["a", "b"]), "p2-light-ko": cps(["a", "b"]), "p3-light-ko": cps(["a", "b"])}
        r = determinism.compare_profiles(runs)
        self.assertTrue(r["ok"])
        runs["p3-light-ko"] = cps(["a", "z"])
        r = determinism.compare_profiles(runs)
        self.assertFalse(r["ok"])
        self.assertEqual(list(r["mismatches"]), ["p3-light-ko"])
        self.assertEqual(r["reference"], "p1-light-ko")

    def test_empty_is_not_ok(self):
        self.assertFalse(determinism.compare_runs([], [])["ok"])
        self.assertFalse(determinism.compare_profiles({})["ok"])


if __name__ == "__main__":
    unittest.main()
