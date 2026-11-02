"""프로필별 플레이어 실행·격리 테스트 (specs/000 T29, FR-22·24·61, AC-13)."""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import profiles, runner  # noqa: E402

FAKE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures", "fake_player.py")


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.ps = profiles.load()
        self.combos = self.ps.expand("smoke")
        self.tmp = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.tmp.cleanup()

    def job(self, combo, mode, **kw):
        return runner.Job(
            combo=combo,
            scenario_path="/x/s.json",
            seed=7,
            env={"FAKE_MODE": mode},
            **kw,
        )

    def run_jobs(self, jobs, hang_sec=1.0, timeout_sec=20):
        return runner.run_matrix(
            [sys.executable, FAKE], jobs, self.tmp.name, self.ps, hang_sec=hang_sec, timeout_sec=timeout_sec, log=lambda *_: None
        )

    def test_player_args_cover_profile(self):
        c = self.ps.expand("full", only=["mac-m1-1600p"])[3]
        a = runner.player_args(runner.Job(c, "/s.json", 9, extra_args=["-farmSampleBlack"]), "/out", self.ps)
        d = dict(zip(a[::2], a[1::2]))
        self.assertEqual(d["-farmWidth"], "2560")
        self.assertEqual(d["-farmHeight"], "1600")
        self.assertEqual(d["-farmUiScale"], "2.0")
        self.assertEqual(d["-farmUnityQuality"], "Mobile")
        self.assertEqual(d["-farmRenderScale"], "0.75")
        self.assertEqual(d["-farmTheme"], c.theme)
        self.assertEqual(d["-farmLang"], c.lang)
        self.assertEqual(d["-farmSeed"], "9")
        self.assertEqual(d["-logFile"], "/out/player.log")
        self.assertIn("-farmSampleBlack", a)

    def test_pass_and_step_failure(self):
        r = self.run_jobs([self.job(self.combos[0], "pass"), self.job(self.combos[1], "fail")])
        self.assertEqual([x.status for x in r], ["passed", "failed"])
        self.assertIn("화면 없음", r[1].reason)

    def test_crash_isolated(self):
        r = self.run_jobs([self.job(c, "crash" if i == 1 else "pass") for i, c in enumerate(self.combos)])
        self.assertEqual([x.status for x in r], ["passed", "crashed", "passed"])
        self.assertIn("크래시", r[1].reason)

    def test_hang_30s_killed(self):
        r = self.run_jobs([self.job(self.combos[0], "hang"), self.job(self.combos[1], "pass")], hang_sec=1.0)
        self.assertEqual(r[0].status, "hung")
        self.assertIn("무응답", r[0].reason)
        self.assertEqual(r[1].status, "passed")

    def test_step_timeout_recorded(self):
        # heartbeat는 살아 있지만 끝나지 않으면 전체 타임아웃
        r = self.run_jobs([self.job(self.combos[0], "slow")], hang_sec=5, timeout_sec=1.5)
        self.assertEqual(r[0].status, "timeout")

    def test_nonzero_exit_fails(self):
        r = self.run_jobs([self.job(self.combos[0], "fail")])
        self.assertEqual(r[0].status, "failed")
        self.assertEqual(r[0].exit_code, 1)

    def test_out_dirs_per_combo(self):
        r = self.run_jobs([self.job(c, "pass") for c in self.combos])
        self.assertEqual(len({x.out_dir for x in r}), 3)
        for x in r:
            self.assertTrue(x.out_dir.endswith(x.combo.key))


if __name__ == "__main__":
    unittest.main()
