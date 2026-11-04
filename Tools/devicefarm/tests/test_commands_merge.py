"""specs/043: merge_previous는 로직·아트 증거만 합치고, 앞선 매트릭스의 시나리오 판정은 쌓지 않는다."""
import json
import os
import tempfile
import unittest

from farm import commands


class MergePrevious(unittest.TestCase):
    def test_scenario_labels_not_accumulated(self):
        with tempfile.TemporaryDirectory() as d:
            prev = {"spec": {"id": "043"}, "logic": {"ok": True},
                    "acEvidence": {"AC-01": [{"label": "EditMode 테스트", "ok": True}],
                                   "AC-13": [{"label": "043-kit-lineup (full) fail", "ok": False}]}}
            with open(os.path.join(d, "run.json"), "w", encoding="utf-8") as f:
                json.dump(prev, f)
            ev = {"acEvidence": {"AC-13": [{"label": "043-kit-lineup (full) pass", "ok": True}]}}
            commands.merge_previous(ev, d, "043")
            self.assertEqual(ev["acEvidence"]["AC-13"], [{"label": "043-kit-lineup (full) pass", "ok": True}])
            self.assertEqual(ev["acEvidence"]["AC-01"], [{"label": "EditMode 테스트", "ok": True}])


class ManualAcs(unittest.TestCase):
    """specs/056 T19a: 수동 승인 기록 파일 → AC 증거."""

    def _info(self, d):
        return {"id": "056", "acs": [{"id": "AC-09"}], "dir": d}

    def test_file_with_ac_passes(self):
        with tempfile.TemporaryDirectory() as d:
            os.makedirs(os.path.join(d, "evidence"))
            with open(os.path.join(d, "evidence", "approval.md"), "w", encoding="utf-8") as f:
                f.write("- AC-09 육안 승인: 통과\n")
            out = commands.manual_acs({"manual": {"AC-09": "evidence/approval.md"}}, self._info(d))
            self.assertTrue(out["AC-09"][0]["ok"])

    def test_missing_file_or_ac_fails(self):
        with tempfile.TemporaryDirectory() as d:
            out = commands.manual_acs({"manual": {"AC-09": "evidence/approval.md"}}, self._info(d))
            self.assertFalse(out["AC-09"][0]["ok"])
            os.makedirs(os.path.join(d, "evidence"))
            with open(os.path.join(d, "evidence", "approval.md"), "w", encoding="utf-8") as f:
                f.write("다른 내용\n")
            out = commands.manual_acs({"manual": {"AC-09": "evidence/approval.md"}}, self._info(d))
            self.assertFalse(out["AC-09"][0]["ok"])

    def test_unknown_ac_is_config_error(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(commands.RunnerError):
                commands.manual_acs({"manual": {"AC-99": "x.md"}}, self._info(d))


if __name__ == "__main__":
    unittest.main()
