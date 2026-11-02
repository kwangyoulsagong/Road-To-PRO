"""셀프테스트 임시 파일 정리 테스트 (specs/000 T36, plan '버그 방지: 임시 파일 잔류')."""
import os
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import commands, selftest  # noqa: E402
from farm.errors import RunnerError  # noqa: E402


class _Ctx:
    run_dir = ""

    def __init__(self):
        self.rows = []

    def row(self, *a, **k):
        self.rows.append(a)


class CleanupTests(unittest.TestCase):
    def test_tmp_cleanup_on_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = os.path.join(tmp, "Assets", "FarmSelfTest_Tmp")
            os.makedirs(os.path.dirname(target))
            with open(target + ".meta", "w") as f:  # Unity가 만든 폴더 .meta 흉내
                f.write("x")

            def boom(*a, **k):
                self.assertTrue(os.path.exists(os.path.join(target, "CompileError.cs")))
                raise RunnerError("Unity 실행 실패")

            with mock.patch.object(selftest, "TMP_ASSETS", target), mock.patch.object(commands, "logic_section", boom):
                with self.assertRaises(RunnerError):
                    selftest.case_compile_error(_Ctx())
            self.assertFalse(os.path.exists(target))
            self.assertFalse(os.path.exists(target + ".meta"))

    def test_leftover_tmp_removed_before_run(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = os.path.join(tmp, "FarmSelfTest_Tmp")
            os.makedirs(target)
            with open(os.path.join(target, "Old.cs"), "w") as f:
                f.write("x")

            def check(*a, **k):
                self.assertFalse(os.path.exists(os.path.join(target, "Old.cs")))
                raise RunnerError("stop")

            with mock.patch.object(selftest, "TMP_ASSETS", target), mock.patch.object(commands, "logic_section", check):
                with self.assertRaises(RunnerError):
                    selftest.case_compile_error(_Ctx())


if __name__ == "__main__":
    unittest.main()
