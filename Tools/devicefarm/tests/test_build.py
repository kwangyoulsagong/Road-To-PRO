"""빌드 캐시 키 테스트 (specs/000 T27)."""
import os
import sys
import tempfile
import time
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import build  # noqa: E402


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


if __name__ == "__main__":
    unittest.main()
