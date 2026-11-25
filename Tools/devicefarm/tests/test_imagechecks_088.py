"""specs/088 T30a 등급 전환 짝 검사(tierPair) 단위 테스트."""
import unittest

import numpy as np

from farm import imagechecks as ic
from farm import imagechecks_088 as ic88

BG = (165, 170, 185)
SKIN = (205, 160, 140)
HAIR = (60, 40, 30)


def head(shift=0, r=40, skin=SKIN, hair=HAIR, h=200, w=200):
    """배경 위 머리(원) — 위 35 %는 머리카락 색. 아래는 몸통(사각)."""
    a = np.zeros((h, w, 3), dtype=np.uint8)
    a[:] = BG
    yy, xx = np.mgrid[0:h, 0:w]
    cx, cy = 100 + shift, 70
    disk = (xx - cx) ** 2 + (yy - cy) ** 2 <= r ** 2
    a[disk] = skin
    a[disk & (yy < cy - r * 0.3)] = hair
    a[130:200, 60:140] = (200, 30, 30)
    return a


class TierPair(unittest.TestCase):
    def test_same_passes(self):
        a = head()
        r = ic88.tier_pair(a, head(), head_frac=0.6)
        self.assertTrue(r["ok"], r["detail"])

    def test_shifted_silhouette_fails(self):
        r = ic88.tier_pair(head(), head(shift=12), head_frac=0.6)
        self.assertFalse(r["ok"], r["detail"])
        self.assertIn("1−IoU", r["detail"])

    def test_color_change_fails(self):
        r = ic88.tier_pair(head(), head(hair=(150, 110, 60)), head_frac=0.6)
        self.assertFalse(r["ok"], r["detail"])

    def test_small_noise_passes(self):
        rng = np.random.default_rng(1)
        b = head().astype(np.int16) + rng.integers(-2, 3, size=(200, 200, 3))
        b = np.clip(b, 0, 255).astype(np.uint8)
        b[:] = np.where((head() == BG).all(-1, keepdims=True), BG, b)
        r = ic88.tier_pair(head(), b, head_frac=0.6)
        self.assertTrue(r["ok"], r["detail"])

    def test_empty_raises(self):
        a = np.zeros((50, 50, 3), dtype=np.uint8)
        a[:] = BG
        with self.assertRaises(ValueError):
            ic88.tier_pair(a, a)

    def test_dispatch(self):
        caps = {"x": (head(), {}), "y": (head(), {})}
        rows = ic.run([{"type": "tierPair", "capture": "x", "other": "y", "headFrac": 0.6}], caps)
        self.assertTrue(rows[0]["ok"], rows[0]["detail"])


if __name__ == "__main__":
    unittest.main()
