"""로그 무시 목록·로그 판정 테스트 (specs/000 T08·T14, FR-60, AC-12)."""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import logs, paths  # noqa: E402
from farm.errors import RunnerError  # noqa: E402


def _write(tmp, name, data):
    p = os.path.join(tmp, name)
    with open(p, "w", encoding="utf-8") as f:
        if isinstance(data, str):
            f.write(data)
        else:
            json.dump(data, f, ensure_ascii=False)
    return p


class IgnoreListTests(unittest.TestCase):
    def test_repo_ignore_list_loads(self):
        self.assertIsInstance(logs.load_ignore(paths.LOG_IGNORE_JSON), list)

    def test_ignore_without_reason_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            for entry in ({"pattern": "foo"}, {"pattern": "foo", "reason": "  "}):
                p = _write(tmp, "ig.json", {"schemaVersion": 1, "ignore": [entry]})
                with self.assertRaises(RunnerError) as cm:
                    logs.load_ignore(p)
                self.assertIn("사유", str(cm.exception))

    def test_bad_regex_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = _write(tmp, "ig.json", {"schemaVersion": 1, "ignore": [{"pattern": "([", "reason": "x"}]})
            with self.assertRaises(RunnerError):
                logs.load_ignore(p)


def _jsonl(entries):
    return "\n".join(json.dumps(e, ensure_ascii=False) for e in entries) + "\n"


class JudgeTests(unittest.TestCase):
    def test_clean_log_passes(self):
        s = logs.judge([{"type": "Log", "message": "hello"}], [])
        self.assertTrue(s.ok)
        self.assertEqual((len(s.errors), len(s.warnings)), (0, 0))

    def test_warning_fails(self):
        s = logs.judge([{"type": "Log", "message": "a"}, {"type": "Warning", "message": "조심"}], [])
        self.assertFalse(s.ok)
        self.assertEqual([e["message"] for e in s.warnings], ["조심"])

    def test_exception_and_assert_classified(self):
        entries = [
            {"type": "Error", "message": "e1"},
            {"type": "Exception", "message": "NullReferenceException: x", "stack": "at A.B()"},
            {"type": "Assert", "message": "assert!"},
        ]
        s = logs.judge(entries, [])
        self.assertFalse(s.ok)
        self.assertEqual(sorted(e["type"] for e in s.errors), ["Assert", "Error", "Exception"])

    def test_ignored_listed_with_reason(self):
        ignore = [{"pattern": "^알려진 경고", "reason": "Unity 6.3 내부 경고 (이슈 링크)", "regex": None}]
        ignore = logs.compile_ignore(ignore)
        s = logs.judge([{"type": "Warning", "message": "알려진 경고: 셰이더"}], ignore)
        self.assertTrue(s.ok)
        self.assertEqual(len(s.ignored), 1)
        self.assertEqual(s.ignored[0]["reason"], "Unity 6.3 내부 경고 (이슈 링크)")

    def test_read_jsonl_tolerates_truncated_last_line(self):
        with tempfile.TemporaryDirectory() as tmp:
            text = _jsonl([{"type": "Log", "message": "a"}]) + '{"type": "Warn'
            p = _write(tmp, "logs.jsonl", text)
            entries = logs.read_jsonl(p)
        self.assertEqual(len(entries), 1)

    def test_summary_dict_lists_lines(self):
        s = logs.judge([{"type": "Warning", "message": "w1"}], [])
        d = s.to_dict()
        self.assertEqual(d["ok"], False)
        self.assertEqual(d["warnings"][0]["message"], "w1")


if __name__ == "__main__":
    unittest.main()
