"""specs/062 화면 지표 단위 테스트 (T03)."""
import unittest

import numpy as np

from farm import imagechecks as ic
from farm import imagechecks_062 as ic62

RED, KIT, SKIN, HAIR = (255, 0, 0), (255, 128, 0), (128, 0, 255), (0, 128, 255)


def canvas(h=120, w=120, color=(0, 0, 0)):
    a = np.zeros((h, w, 3), dtype=np.uint8)
    a[:] = color
    return a


def masks(arr):
    return ic.masks_from(arr)


class OutlineTurn(unittest.TestCase):
    def test_circle_smooth(self):
        a = canvas()
        yy, xx = np.mgrid[0:120, 0:120]
        a[(xx - 60) ** 2 + (yy - 60) ** 2 <= 40 ** 2] = RED
        r = ic62.outline_turn(a, {}, masks(a))
        self.assertTrue(r["ok"], r["detail"])

    def test_square_corner_fails(self):
        a = canvas()
        a[30:90, 30:90] = RED
        r = ic62.outline_turn(a, {}, masks(a))
        self.assertFalse(r["ok"], r["detail"])
        self.assertGreater(r["value"], 60)

    def test_region_limits_to_smooth_part(self):
        a = canvas()
        a[30:90, 30:90] = RED
        r = ic62.outline_turn(a, {"edge": [40, 20, 40, 20]}, masks(a), region="edge")  # 위 변 가운데만 (모서리 제외)
        self.assertTrue(r["ok"], r["detail"])


class SeamStep(unittest.TestCase):
    def test_equal_bands_pass(self):
        a = canvas(color=(128, 128, 128))
        rng = np.random.default_rng(1)
        a = np.clip(a + rng.integers(-6, 7, a.shape), 0, 255).astype(np.uint8)
        r = ic62.seam_step(a, {"seamSkin": [10, 10, 40, 10], "seamKit": [10, 22, 40, 10]}, None)
        self.assertTrue(r["ok"], r["detail"])

    def test_step_fails(self):
        a = canvas(color=(128, 128, 128))
        rng = np.random.default_rng(1)
        a = np.clip(a + rng.integers(-3, 4, a.shape), 0, 255).astype(np.uint8)
        a[22:32] = (160, 160, 160)
        r = ic62.seam_step(a, {"seamSkin": [10, 10, 40, 10], "seamKit": [10, 22, 40, 10]}, None)
        self.assertFalse(r["ok"], r["detail"])


def hand(fingers):
    a = canvas()
    a[60:100, 30:90] = RED  # 손바닥
    xs = np.linspace(32, 82, fingers).astype(int) if fingers > 1 else [55]
    for x in xs:
        a[20:60, x:x + 6] = RED
    return a


class HandConcavity(unittest.TestCase):
    def test_five_fingers(self):
        a = hand(5)
        r = ic62.hand_concavity(a, {"hand": [0, 0, 120, 120]}, masks(a))
        self.assertTrue(r["ok"], r["detail"])

    def test_mitten_fails(self):
        a = canvas()
        a[20:100, 30:90] = RED
        r = ic62.hand_concavity(a, {"hand": [0, 0, 120, 120]}, masks(a))
        self.assertFalse(r["ok"], r["detail"])


class KitStats(unittest.TestCase):
    def test_flat_primary_fails(self):
        a = canvas(color=(200, 30, 30))
        m = canvas(color=KIT)
        r = ic62.kit_stats(a, {}, masks(m), team_rgb=(200, 30, 30))
        self.assertFalse(r["ok"], r["detail"])

    def test_shaded_passes(self):
        yy = np.linspace(0.45, 1.0, 120)[:, None, None]
        base = np.array([200, 30, 30], dtype=float)
        a = (base * yy * 0.8 + 30).astype(np.uint8)
        a = np.repeat(a, 120, axis=1)
        m = canvas(color=KIT)
        r = ic62.kit_stats(a, {}, masks(m), team_rgb=(200, 30, 30), baseline_l_std=2.5)
        self.assertTrue(r["ok"], r["detail"])


class SilhouetteDiff(unittest.TestCase):
    def test_same_fails_different_passes(self):
        a = canvas(); a[20:60, 40:80] = HAIR; a[60:100, 45:75] = SKIN
        b = a.copy()
        c = canvas(); c[5:60, 25:95] = HAIR; c[60:100, 45:75] = SKIN
        self.assertFalse(ic62.silhouette_diff([a, b])["ok"])
        r = ic62.silhouette_diff([a, c])
        self.assertTrue(r["ok"], r["detail"])
        self.assertTrue(ic62.silhouette_diff([a, b, c], exclude_pairs=[[0, 1]])["ok"])


class HighlightAnisotropy(unittest.TestCase):
    def _hair(self, streak):
        a = canvas(color=(40, 30, 20))
        for k in range(6):
            y = 15 + k * 16
            if streak:
                a[y:y + 2, 20:100] = (230, 220, 200)
            else:
                a[y:y + 6, 50:56] = (230, 220, 200)
        return a

    def test_streaks_pass_dots_fail(self):
        m = canvas(color=HAIR)
        self.assertTrue(ic62.highlight_anisotropy(self._hair(True), {}, masks(m))["ok"])
        self.assertFalse(ic62.highlight_anisotropy(self._hair(False), {}, masks(m))["ok"])


class TierMatch(unittest.TestCase):
    def test_same_colors_pass_shifted_fail(self):
        m = canvas(); m[:40] = SKIN; m[40:80] = KIT; m[80:] = HAIR
        a = canvas(); a[:40] = (200, 150, 120); a[40:80] = (200, 30, 30); a[80:] = (40, 30, 20)
        self.assertTrue(ic62.tier_match(a, masks(m), a.copy(), masks(m))["ok"])
        b = a.copy(); b[:40] = (170, 120, 95)
        self.assertFalse(ic62.tier_match(a, masks(m), b, masks(m))["ok"])


class TextSpots(unittest.TestCase):
    def test_plain_pass_letters_fail(self):
        m = canvas(); m[10:110, 10:110] = RED
        a = canvas(color=(60, 60, 70))
        self.assertTrue(ic62.text_spots(a, {}, masks(m))["ok"])
        a[50:58, 40:44] = 240; a[50:58, 50:54] = 240
        self.assertFalse(ic62.text_spots(a, {}, masks(m))["ok"])


class RunDispatch(unittest.TestCase):
    def test_run_routes_062(self):
        a = canvas(); a[30:90, 30:90] = RED
        out = ic.run([{"type": "outlineTurn", "capture": "x", "mask": "x"}], {"x": (a, {})})
        self.assertEqual(out[0]["type"], "outlineTurn")
        self.assertFalse(out[0]["ok"])
        self.assertIn("꺾임", out[0]["detail"])


if __name__ == "__main__":
    unittest.main()
