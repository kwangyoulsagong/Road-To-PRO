"""빌드 캐시 키 테스트 (specs/000 T27)."""
import os
import plistlib
import sys
import tempfile
import time
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import build  # noqa: E402
from farm.errors import RunnerError  # noqa: E402


class CacheKeyTests(unittest.TestCase):
    def test_key_stable_and_changes_on_edit(self):
        with tempfile.TemporaryDirectory() as root:
            os.makedirs(os.path.join(root, "Assets", "a"))
            p = os.path.join(root, "Assets", "a", "x.cs")
            with open(p, "w") as f:
                f.write("class X {}")
            k1 = build.cache_key(root, dirs=("Assets",))
            self.assertEqual(k1, build.cache_key(root, dirs=("Assets",)))
            time.sleep(0.01)
            with open(p, "w") as f:
                f.write("class X { int y; }")
            self.assertNotEqual(k1, build.cache_key(root, dirs=("Assets",)))

    def test_is_cached_needs_app_and_stamp(self):
        with tempfile.TemporaryDirectory() as tmp:
            app = os.path.join(tmp, "A.app")
            stamp = os.path.join(tmp, "stamp.txt")
            self.assertFalse(build.is_cached("k", app, stamp))
            os.makedirs(os.path.join(app, "Contents", "MacOS"))
            with open(os.path.join(app, "Contents", "MacOS", "Road To PRO"), "w") as f:
                f.write("")
            with open(stamp, "w") as f:
                f.write("k\n")
            self.assertTrue(build.is_cached("k", app, stamp))
            self.assertFalse(build.is_cached("other", app, stamp))
            self.assertTrue(build.executable(app).endswith("Road To PRO"))

class HideFromDockTests(unittest.TestCase):
    def test_sets_flag_and_resigns_once(self):
        with tempfile.TemporaryDirectory() as tmp:
            app = os.path.join(tmp, "A.app")
            os.makedirs(os.path.join(app, "Contents"))
            plist = os.path.join(app, "Contents", "Info.plist")
            with open(plist, "wb") as f:
                plistlib.dump({"CFBundleName": "A"}, f)
            with mock.patch.object(build.subprocess, "run", return_value=mock.Mock(returncode=0, stderr="")) as run:
                build.hide_from_dock(app)
                build.hide_from_dock(app)
            with open(plist, "rb") as f:
                info = plistlib.load(f)
            self.assertIs(info["LSUIElement"], True)
            self.assertEqual(info["CFBundleName"], "A")
            self.assertEqual(run.call_count, 1)
            self.assertEqual(run.call_args[0][0][:2], ["codesign", "--force"])

    def test_resign_failure_raises(self):
        with tempfile.TemporaryDirectory() as tmp:
            app = os.path.join(tmp, "A.app")
            os.makedirs(os.path.join(app, "Contents"))
            with open(os.path.join(app, "Contents", "Info.plist"), "wb") as f:
                plistlib.dump({}, f)
            with mock.patch.object(build.subprocess, "run", return_value=mock.Mock(returncode=1, stderr="no")):
                with self.assertRaises(RunnerError):
                    build.hide_from_dock(app)

class HiddenWindowEnvTests(unittest.TestCase):
    def test_compiles_once_and_injects(self):
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, "h.m")
            lib = os.path.join(tmp, "out", "h.dylib")
            with open(src, "w") as f:
                f.write("")

            def fake_clang(cmd, **_):
                with open(lib, "w") as f:
                    f.write("")
                return mock.Mock(returncode=0, stderr="")

            with mock.patch.dict(os.environ), mock.patch.object(build.subprocess, "run", side_effect=fake_clang) as run:
                os.environ.pop("FARM_SHOW_WINDOW", None)
                self.assertEqual(build.hidden_window_env(src, lib), {"DYLD_INSERT_LIBRARIES": lib})
                build.hidden_window_env(src, lib)
                self.assertEqual(run.call_count, 1)

    def test_show_window_opt_out(self):
        with mock.patch.dict(os.environ, {"FARM_SHOW_WINDOW": "1"}):
            self.assertEqual(build.hidden_window_env("/none.m", "/none.dylib"), {})

    def test_compile_failure_raises(self):
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, "h.m")
            with open(src, "w") as f:
                f.write("")
            failed = mock.Mock(returncode=1, stderr="boom")
            with mock.patch.dict(os.environ), mock.patch.object(build.subprocess, "run", return_value=failed):
                os.environ.pop("FARM_SHOW_WINDOW", None)
                with self.assertRaises(RunnerError):
                    build.hidden_window_env(src, os.path.join(tmp, "h.dylib"))


if __name__ == "__main__":
    unittest.main()
