"""실행 전 점검 테스트 (specs/000 T16, FR-81, 경계 · 예외 상황)."""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import preflight  # noqa: E402
from farm.errors import RunnerError  # noqa: E402


class LockTests(unittest.TestCase):
    def test_lock_blocks_second_run(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "farm.lock")
            with preflight.RunLock(p):
                self.assertTrue(os.path.exists(p))
                with self.assertRaises(RunnerError) as cm:
                    with preflight.RunLock(p):
                        pass
                self.assertIn("이미", str(cm.exception))
            self.assertFalse(os.path.exists(p))

    def test_lock_released_on_exception(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "farm.lock")
            with self.assertRaises(ValueError):
                with preflight.RunLock(p):
                    raise ValueError("x")
            self.assertFalse(os.path.exists(p))

    def test_stale_lock_removed(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "farm.lock")
            with open(p, "w") as f:
                f.write("999999999\n")  # 존재하지 않는 PID
            with preflight.RunLock(p):
                with open(p) as f:
                    self.assertEqual(int(f.read().split()[0]), os.getpid())


class CheckTests(unittest.TestCase):
    def test_editor_open_is_runner_error(self):
        with self.assertRaises(RunnerError) as cm:
            preflight.check_editor_closed(is_locked=lambda: True)
        self.assertIn("에디터", str(cm.exception))
        preflight.check_editor_closed(is_locked=lambda: False)

    def test_low_disk_is_runner_error(self):
        with self.assertRaises(RunnerError) as cm:
            preflight.check_disk(free_bytes=lambda: 1 * 1024**3, min_gb=5)
        self.assertIn("디스크", str(cm.exception))
        preflight.check_disk(free_bytes=lambda: 50 * 1024**3, min_gb=5)

    def test_missing_tools_is_runner_error(self):
        with self.assertRaises(RunnerError) as cm:
            preflight.check_tools(unity="/nope/Unity", chrome=None, need_chrome=False)
        self.assertIn("Unity", str(cm.exception))
        with self.assertRaises(RunnerError):
            preflight.check_tools(unity=sys.executable, chrome="/nope/chrome", need_chrome=True)
        preflight.check_tools(unity=sys.executable, chrome=sys.executable, need_chrome=True)

    def test_dirty_worktree_flag(self):
        self.assertTrue(preflight.worktree_dirty(status=lambda: " M a.cs\n"))
        self.assertFalse(preflight.worktree_dirty(status=lambda: ""))


if __name__ == "__main__":
    unittest.main()
