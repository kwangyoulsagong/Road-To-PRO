"""farm CLI 골격 테스트 (specs/000 T04, FR-80·FR-81)."""
import io
import os
import sys
import unittest
from contextlib import redirect_stderr, redirect_stdout

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import cli  # noqa: E402
from farm.errors import (  # noqa: E402
    EXIT_OK,
    EXIT_RUNNER_ERROR,
    EXIT_USAGE,
    EXIT_VERIFY_FAILED,
    RunnerError,
    VerificationFailed,
)


class SubcommandParseTests(unittest.TestCase):
    def test_subcommands_parse(self):
        p = cli.build_parser()
        for cmd in ["logic", "shot", "build", "approve-perf"]:
            self.assertEqual(p.parse_args([cmd]).command, cmd)

        a = p.parse_args(["shot", "--spec", "012", "--scenario", "000-sample-cards"])
        self.assertEqual((a.spec, a.scenario), ("012", "000-sample-cards"))

        a = p.parse_args(["shot", "--profile", "mac-m1-1600p", "--profile", "compact-1366"])
        self.assertEqual(a.profile, ["mac-m1-1600p", "compact-1366"])

        a = p.parse_args(["self-test", "diff"])
        self.assertEqual((a.command, a.case), ("self-test", "diff"))

    def test_unknown_command_is_usage_error(self):
        with redirect_stderr(io.StringIO()):
            self.assertEqual(cli.main(["nope"]), EXIT_USAGE)

    def test_unknown_selftest_case_is_usage_error(self):
        with redirect_stderr(io.StringIO()):
            self.assertEqual(cli.main(["self-test", "nope"]), EXIT_USAGE)


class ExitCodeTests(unittest.TestCase):
    def _run(self, handler):
        out, err = io.StringIO(), io.StringIO()
        with redirect_stdout(out), redirect_stderr(err):
            code = cli.main(["logic"], handlers={"logic": handler})
        return code, out.getvalue() + err.getvalue()

    def test_pass_is_zero(self):
        code, _ = self._run(lambda args: None)
        self.assertEqual(code, EXIT_OK)

    def test_runner_error_exit_code_distinct(self):
        def boom(args):
            raise RunnerError("Unity를 찾을 수 없다")

        def fail(args):
            raise VerificationFailed("테스트 실패 1건")

        rc, rmsg = self._run(boom)
        vc, vmsg = self._run(fail)
        self.assertEqual(rc, EXIT_RUNNER_ERROR)
        self.assertEqual(vc, EXIT_VERIFY_FAILED)
        self.assertNotEqual(rc, vc)
        self.assertIn("실행기 오류", rmsg)
        self.assertIn("Unity를 찾을 수 없다", rmsg)
        self.assertIn("검증 실패", vmsg)

    def test_unimplemented_command_is_runner_error(self):
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(cli.main(["shot"], handlers={}), EXIT_RUNNER_ERROR)


if __name__ == "__main__":
    unittest.main()
