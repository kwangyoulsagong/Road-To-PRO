"""specs/044 이미지 검사: 합성 이미지로 통과·실패 (bandFlip, lineContinuity, blobX, notBlank)."""
import unittest

import numpy as np

from farm import imagechecks as ic


def grass(h=200, w=300, v=(60, 110, 50)):
    a = np.zeros((h, w, 3), dtype=np.uint8)
    a[:] = v
    return a


class BandFlip(unittest.TestCase):
    def _img(self, left, right):
        a = grass()
        a[:, :150] = (left, left + 40, left - 10)
        a[:, 150:] = (right, right + 40, right - 10)
        return a

    def test_flip_passes(self):
        regions = {"bandA": [10, 10, 100, 100], "bandB": [180, 10, 100, 100]}
        r = ic.band_flip(self._img(80, 60), regions, self._img(60, 80), regions, min_delta=2)
        self.assertTrue(r["ok"], r)

    def test_no_flip_fails(self):
        regions = {"bandA": [10, 10, 100, 100], "bandB": [180, 10, 100, 100]}
        r = ic.band_flip(self._img(80, 60), regions, self._img(82, 60), regions, min_delta=2)
        self.assertFalse(r["ok"])

    def test_too_small_difference_fails(self):
        regions = {"bandA": [10, 10, 100, 100], "bandB": [180, 10, 100, 100]}
        r = ic.band_flip(self._img(70, 70), regions, self._img(70, 70), regions, min_delta=2)
        self.assertFalse(r["ok"])


class LineContinuity(unittest.TestCase):
    def _line(self, gap=False, wobble=False):
        a = grass(200, 300)
        for x in range(20, 280):
            w = 3 + (2 * (x % 2) if wobble else 0)
            if gap and 140 <= x < 146:
                continue
            a[100:100 + w, x] = (235, 235, 230)
        return a

    def test_continuous(self):
        r = ic.line_continuity(self._line(), {"line": {"a": [30, 101], "b": [270, 101]}}, samples=200, max_width_std=1.0)
        self.assertTrue(r["ok"], r)
        self.assertEqual(r["gaps"], 0)

    def test_gap_fails(self):
        r = ic.line_continuity(self._line(gap=True), {"line": {"a": [30, 101], "b": [270, 101]}}, samples=200, max_width_std=1.0)
        self.assertFalse(r["ok"])
        self.assertGreater(r["gaps"], 0)

    def test_wobbly_width_fails(self):
        r = ic.line_continuity(self._line(wobble=True), {"line": {"a": [30, 101], "b": [270, 101]}}, samples=200, max_width_std=0.5)
        self.assertFalse(r["ok"])


class BlobX(unittest.TestCase):
    def _img(self, n_blobs):
        a = grass(200, 200, (90, 140, 80))
        centers = [(70, 70), (130, 70), (70, 130), (130, 130)]
        for cx, cy in centers[:n_blobs]:
            a[cy - 8:cy + 8, cx - 8:cx + 8] = (40, 60, 35)
        return a, {"foot": [100, 100], "blobs": [list(c) for c in centers]}

    def test_four_dark_blobs_in_x(self):
        a, reg = self._img(4)
        r = ic.blob_x(a, reg, min_darkening=0.15)
        self.assertTrue(r["ok"], r)

    def test_missing_blob_fails(self):
        a, reg = self._img(3)
        self.assertFalse(ic.blob_x(a, reg, min_darkening=0.15)["ok"])

    def test_not_x_shape_fails(self):
        a, reg = self._img(4)
        reg["blobs"] = [[70, 70], [130, 70], [70, 72], [130, 72]]
        self.assertFalse(ic.blob_x(a, reg, min_darkening=0.15)["ok"])


class NotBlank(unittest.TestCase):
    def test_solid_fails(self):
        self.assertFalse(ic.not_blank(grass(), {})["ok"])

    def test_varied_passes(self):
        a = grass()
        a[50:150, 50:250] = (200, 200, 200)
        a[0:30, :] = (20, 20, 80)
        self.assertTrue(ic.not_blank(a, {})["ok"])


class RunChecks(unittest.TestCase):
    def test_camel_case_params(self):
        a = grass()
        for x in range(20, 280):
            a[100:103, x] = (235, 235, 230)
        res = ic.run([{"type": "lineContinuity", "capture": "a", "samples": 200, "maxWidthStd": 1.0}],
                     {"a": (a, {"line": {"a": [30, 101], "b": [270, 101]}})})
        self.assertTrue(res[0]["ok"], res[0]["detail"])

    def test_dispatch_and_missing_capture(self):
        caps = {"a": (grass(), {})}
        res = ic.run([{"type": "notBlank", "capture": "a"}, {"type": "notBlank", "capture": "zz"}], caps)
        self.assertEqual(len(res), 2)
        self.assertFalse(res[0]["ok"])
        self.assertFalse(res[1]["ok"])
        self.assertIn("캡처 없음", res[1]["detail"])


if __name__ == "__main__":
    unittest.main()
