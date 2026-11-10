"""프로필 로드·조합 전개 테스트 (specs/000 T05, FR-10~13)."""
import copy
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import paths, profiles  # noqa: E402
from farm.errors import RunnerError  # noqa: E402


def _load_raw():
    with open(paths.PROFILES_JSON, encoding="utf-8") as f:
        return json.load(f)


def _write(tmp, data):
    p = os.path.join(tmp, "profiles.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(data, f)
    return p


class ProfileLoadTests(unittest.TestCase):
    def test_default_set_has_ten_profiles(self):
        ps = profiles.load()
        self.assertEqual(len(ps.profiles), 10)
        ids = [p.id for p in ps.profiles]
        for pid in ["low-igpu-1080p", "mac-m1-1600p", "compact-1366", "phone-portrait"]:
            self.assertIn(pid, ids)
        m1 = ps.get("mac-m1-1600p")
        self.assertEqual((m1.width, m1.height, m1.ui_scale), (2560, 1600, 2.0))
        self.assertEqual((m1.quality, m1.render_scale, m1.tier), ("Medium", 0.75, "smoke"))
        self.assertTrue(m1.is_reference_device)

    def test_only_mac_m1_is_reference(self):
        ps = profiles.load()
        self.assertEqual([p.id for p in ps.profiles if p.is_reference_device], ["mac-m1-1600p"])

    def test_quality_map_resolves(self):
        ps = profiles.load()
        # specs/048: Low는 새 Low 레벨, Medium/High는 후속 스펙까지 Mobile/PC 대역
        self.assertEqual(ps.unity_quality_level("Low"), "Low")
        self.assertEqual(ps.unity_quality_level("Medium"), "Mobile")
        self.assertEqual(ps.unity_quality_level("High"), "PC")

    def test_missing_field_rejected(self):
        raw = _load_raw()
        del raw["profiles"][0]["width"]
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(RunnerError) as cm:
                profiles.load(_write(tmp, raw))
        self.assertIn("width", str(cm.exception))
        self.assertIn(raw["profiles"][0]["id"], str(cm.exception))

    def test_duplicate_id_rejected(self):
        raw = _load_raw()
        raw["profiles"].append(copy.deepcopy(raw["profiles"][0]))
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(RunnerError) as cm:
                profiles.load(_write(tmp, raw))
        self.assertIn("중복", str(cm.exception))

    def test_bad_quality_or_tier_rejected(self):
        for field, value in [("quality", "Ultra"), ("tier", "nightly"), ("renderScale", 0)]:
            raw = _load_raw()
            raw["profiles"][1][field] = value
            with tempfile.TemporaryDirectory() as tmp:
                with self.assertRaises(RunnerError, msg=field):
                    profiles.load(_write(tmp, raw))


class ExpandTests(unittest.TestCase):
    def setUp(self):
        self.ps = profiles.load()

    def test_smoke_is_three_by_one(self):
        combos = self.ps.expand("smoke")
        self.assertEqual(len(combos), 3)
        self.assertEqual({(c.theme, c.lang) for c in combos}, {("light", "ko")})
        self.assertEqual(
            [c.profile.id for c in combos], ["low-igpu-1080p", "mac-m1-1600p", "compact-1366"]
        )

    def test_full_is_ten_by_four(self):
        combos = self.ps.expand("full")
        self.assertEqual(len(combos), 40)
        self.assertEqual(len({c.key for c in combos}), 40)
        self.assertEqual(
            {(c.theme, c.lang) for c in combos},
            {("light", "ko"), ("dark", "ko"), ("light", "en"), ("dark", "en")},
        )

    def test_scenario_selector_list_and_cli_filter(self):
        combos = self.ps.expand("full", selector=["compact-1366", "uhd-4k-200"])
        self.assertEqual({c.profile.id for c in combos}, {"compact-1366", "uhd-4k-200"})
        combos = self.ps.expand("smoke", only=["mac-m1-1600p"])
        self.assertEqual([c.key for c in combos], ["mac-m1-1600p-light-ko"])

    def test_selector_smoke_limits_full_run(self):
        combos = self.ps.expand("full", selector="smoke")
        self.assertEqual({c.profile.tier for c in combos}, {"smoke"})
        self.assertEqual(len(combos), 12)

    def test_unknown_profile_id_rejected(self):
        with self.assertRaises(RunnerError):
            self.ps.expand("smoke", only=["nope"])

    def test_forced_lang(self):
        combos = self.ps.expand("smoke", langs=["en"])
        self.assertEqual({c.lang for c in combos}, {"en"})


if __name__ == "__main__":
    unittest.main()
