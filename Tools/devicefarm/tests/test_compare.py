"""스크린샷 비교 테스트 (specs/000 T09, FR-30~34, AC-03·05·07·08)."""
import os
import sys
import tempfile
import unittest

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from farm import baseline, compare  # noqa: E402


def card_screen(w=400, h=240, button=(0, 100, 255)):
    """회색 바탕 + 흰 카드 3장 + 파란 버튼 (샘플 화면 흉내)."""
    a = np.full((h, w, 3), 242, np.uint8)
    for i in range(3):
        x0 = 20 + i * 125
        a[30:150, x0 : x0 + 110] = 255
        a[50:60, x0 + 10 : x0 + 90] = 30  # 카드 안 글자 흉내
    a[180:220, 140:260] = button
    return a


def save(tmp, name, arr):
    p = os.path.join(tmp, name)
    Image.fromarray(arr).save(p)
    return p


class CompareTests(unittest.TestCase):
    def test_identical_zero_diff(self):
        a = card_screen()
        r = compare.compare_arrays(a, a.copy())
        self.assertEqual(r.status, "pass")
        self.assertEqual(r.diff_pixels, 0)
        self.assertEqual(r.diff_ratio, 0.0)

    def test_threshold_ratio(self):
        base = card_screen()
        small = base.copy()
        small[0:10, 0:10] = np.clip(small[0:10, 0:10].astype(int) + 7, 0, 255)  # 임계값 8 이하 차이
        self.assertEqual(compare.compare_arrays(small, base).status, "pass")

        few = base.copy()
        few[0:2, 0:40] = 0  # 80px = 0.083% < 0.1%
        self.assertEqual(compare.compare_arrays(few, base).status, "pass")

        many = base.copy()
        many[0:4, 0:40] = 0  # 160px = 0.167% > 0.1%
        r = compare.compare_arrays(many, base)
        self.assertEqual(r.status, "fail")
        self.assertAlmostEqual(r.diff_ratio, 160 / (400 * 240))
        self.assertEqual(compare.compare_arrays(many, base, max_diff_ratio=0.005).status, "pass")

    def test_masks_excluded(self):
        base = card_screen()
        cur = base.copy()
        cur[0:20, 0:100] = 0  # 시계 영역 흉내
        self.assertEqual(compare.compare_arrays(cur, base).status, "fail")
        r = compare.compare_arrays(cur, base, masks=[(0, 0, 100, 20)])
        self.assertEqual(r.status, "pass")

    def test_mask_ratio_to_pixels(self):
        self.assertEqual(compare.mask_to_pixels({"rect": [0.5, 0.25, 0.25, 0.5]}, 400, 200), (200, 50, 100, 100))
        self.assertEqual(compare.mask_to_pixels({"pixels": [1, 2, 3, 4]}, 400, 200), (1, 2, 3, 4))

    def test_resolution_mismatch_fails(self):
        base = card_screen(400, 240)
        cur = card_screen(800, 480)
        r = compare.compare_arrays(cur, base)
        self.assertEqual(r.status, "fail")
        self.assertIn("해상도", r.reason)
        # 프로필 해상도와 캡처 해상도 불일치 (레티나 2배 등)
        r = compare.check_capture_size(cur, (400, 240))
        self.assertFalse(r[0])
        self.assertIn("800×480", r[1])
        self.assertTrue(compare.check_capture_size(base, (400, 240))[0])

    def test_diff_image_highlights_region(self):
        base = card_screen()
        cur = card_screen(button=(255, 80, 0))
        r = compare.compare_arrays(cur, base)
        self.assertEqual(r.status, "fail")
        d = r.diff_image
        self.assertEqual(d.shape, base.shape)
        # 버튼 영역은 빨강 강조, 그 밖은 강조 없음
        self.assertTrue((d[200, 200] == compare.HIGHLIGHT).all())
        self.assertFalse((d[100, 60] == compare.HIGHLIGHT).all())
        self.assertEqual(r.bbox, (140, 180, 120, 40))

    def test_blank_and_solid_fail_without_baseline(self):
        for arr in (np.zeros((240, 400, 3), np.uint8), np.full((240, 400, 3), 128, np.uint8)):
            r = compare.compare_arrays(arr, None)
            self.assertEqual(r.status, "fail")
            self.assertIn("단색", r.reason)
        # 기준이 있어도 단색이면 실패 (기준 자체가 단색이어도)
        blk = np.zeros((240, 400, 3), np.uint8)
        self.assertEqual(compare.compare_arrays(blk, blk.copy()).status, "fail")

    def test_card_screen_not_solid(self):
        self.assertFalse(compare.is_solid(card_screen()))

    def test_missing_baseline_is_candidate(self):
        r = compare.compare_arrays(card_screen(), None)
        self.assertEqual(r.status, "candidate")

    def test_compare_files_and_write_diff(self):
        with tempfile.TemporaryDirectory() as tmp:
            b = save(tmp, "b.png", card_screen())
            c = save(tmp, "c.png", card_screen(button=(255, 0, 0)))
            out = os.path.join(tmp, "diff.png")
            r = compare.compare_files(c, b, diff_path=out)
            self.assertEqual(r.status, "fail")
            self.assertTrue(os.path.exists(out))
            r = compare.compare_files(c, os.path.join(tmp, "none.png"))
            self.assertEqual(r.status, "candidate")

    def test_alpha_channel_ignored(self):
        a = card_screen()
        rgba = np.dstack([a, np.full(a.shape[:2], 255, np.uint8)])
        self.assertEqual(compare.compare_arrays(compare.to_rgb(rgba), a).status, "pass")


class BaselineTests(unittest.TestCase):
    def test_path_rule(self):
        p = baseline.baseline_path("000-sample-cards", "home", "mac-m1-1600p-light-ko", root="/r")
        self.assertEqual(p, "/r/000-sample-cards/home/mac-m1-1600p-light-ko.png")

    def test_approve_copies_candidates_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = os.path.join(tmp, "baselines")
            src = save(tmp, "cap.png", card_screen())
            items = [
                {"scenario": "s", "step": "home", "combo": "p-light-ko", "status": "candidate", "current": src},
                {"scenario": "s", "step": "x", "combo": "p-light-ko", "status": "fail", "current": src},
            ]
            done = baseline.approve(items, root=root)
            self.assertEqual(len(done), 1)
            self.assertTrue(os.path.exists(baseline.baseline_path("s", "home", "p-light-ko", root=root)))
            self.assertFalse(os.path.exists(baseline.baseline_path("s", "x", "p-light-ko", root=root)))
            # 실패 항목은 include_failed=True로 명시해야만 갱신
            done = baseline.approve(items, root=root, include_failed=True)
            self.assertEqual(len(done), 2)


if __name__ == "__main__":
    unittest.main()
