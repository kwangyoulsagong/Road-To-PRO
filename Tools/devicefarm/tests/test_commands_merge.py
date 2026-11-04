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


if __name__ == "__main__":
    unittest.main()
