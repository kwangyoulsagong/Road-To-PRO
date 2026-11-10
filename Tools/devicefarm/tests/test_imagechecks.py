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


class DeltaE(unittest.TestCase):
    """specs/043 AC-13: ΔE2000 (Sharma 2005 표 일부) + 그룹 색 검사."""

    def test_sharma_pairs(self):
        for l1, l2, de in (((50, 2.6772, -79.7751), (50, 0, -82.7485), 2.0425),
                           ((50, 2.5, 0), (73, 25, -18), 27.1492),
                           ((60.2574, -34.0099, 36.2677), (60.4626, -34.1751, 39.4387), 1.2644),
                           ((2.0776, 0.0795, -1.1350), (0.9033, -0.0636, -0.5514), 0.9082)):
            self.assertAlmostEqual(ic.delta_e2000(l1, l2), de, places=4)

    def test_white_lab(self):
        lab = ic.srgb_to_lab([255, 255, 255])
        self.assertAlmostEqual(lab[0], 100.0, delta=0.01)

    def _img(self, colors):
        a = np.zeros((100, 500, 3), dtype=np.uint8)
        for i, c in enumerate(colors):
            a[:, i * 100:(i + 1) * 100] = c
        return a

    def _regions(self):
        names = ("home", "away", "homeGk", "awayGk", "referee")
        return {"colorGroups": {n: [[i * 100 + 20, 20, 60, 60]] for i, n in enumerate(names)}}

    def test_distinct_passes(self):
        r = ic.delta_e(self._img([(200, 20, 40), (30, 60, 150), (0, 200, 80), (255, 210, 0), (20, 20, 20)]), self._regions())
        self.assertTrue(r["ok"], r)

    def test_similar_fails(self):
        r = ic.delta_e(self._img([(200, 20, 40), (205, 25, 45), (0, 200, 80), (255, 210, 0), (20, 20, 20)]), self._regions())
        self.assertFalse(r["ok"])


class GlyphInside(unittest.TestCase):
    def _shirt(self):
        a = np.zeros((200, 200, 3), dtype=np.uint8)
        a[:] = (200, 20, 40)
        return a

    def test_inside_passes(self):
        a = self._shirt()
        a[60:100, 80:90] = (255, 255, 255)   # 높이 40px 글자
        reg = {"textAreas": [{"area": [60, 50, 80, 60], "text": [255, 255, 255], "base": [200, 20, 40], "kind": "number"}]}
        self.assertTrue(ic.glyph_inside(a, reg)["ok"])

    def test_overflow_fails(self):
        a = self._shirt()
        a[60:100, 80:90] = (255, 255, 255)
        a[60:70, 145:150] = (255, 255, 255)  # 영역 오른쪽 밖(주변 띠 안)
        reg = {"textAreas": [{"area": [60, 50, 80, 60], "text": [255, 255, 255], "base": [200, 20, 40], "kind": "number"}]}
        self.assertFalse(ic.glyph_inside(a, reg)["ok"])

    def test_too_small_fails(self):
        a = self._shirt()
        a[60:68, 80:84] = (255, 255, 255)    # 8px
        reg = {"textAreas": [{"area": [60, 50, 80, 60], "text": [255, 255, 255], "base": [200, 20, 40], "kind": "number"}]}
        self.assertFalse(ic.glyph_inside(a, reg, min_glyph_px=12)["ok"])


class GlyphInsideNeighbours(unittest.TestCase):
    def test_same_entity_neighbour_area_not_outside(self):
        a = np.zeros((200, 200, 3), dtype=np.uint8)
        a[:] = (200, 20, 40)
        a[60:100, 80:90] = (255, 255, 255)   # 번호
        a[40:48, 70:120] = (255, 255, 255)   # 바로 위 이름(번호 주변 띠 안)
        reg = {"textAreas": [
            {"area": [60, 50, 80, 60], "text": [255, 255, 255], "base": [200, 20, 40], "kind": "number", "entity": 3},
            {"area": [60, 38, 80, 12], "text": [255, 255, 255], "base": [200, 20, 40], "kind": "name", "entity": 3}]}
        self.assertTrue(ic.glyph_inside(a, reg)["ok"], ic.glyph_inside(a, reg))


class ContactAndSame(unittest.TestCase):
    """specs/043 AC-14·15."""

    def test_contact_distance(self):
        reg = {"contact": {"point": [100, 100], "ball": [104, 103], "ballDiameter": 20, "clip": "pass_inside_r"}}
        self.assertTrue(ic.contact_distance(None, reg)["ok"])
        reg["contact"]["ball"] = [100, 115]
        self.assertFalse(ic.contact_distance(None, reg)["ok"])

    def test_same_image(self):
        a = grass()
        b = a.copy()
        self.assertTrue(ic.same_image(a, {}, b, {})["ok"])
        b[50:90, 50:90] = (200, 30, 30)
        self.assertFalse(ic.same_image(a, {}, b, {})["ok"])

    def test_same_image_rect_ignores_outside(self):
        """048 AC-06: 영역만 비교 — 영역 밖 변화는 무시, 영역 안 변화는 실패."""
        a = grass()
        b = a.copy()
        b[50:90, 50:90] = (200, 30, 30)
        self.assertTrue(ic.same_image(a, {}, b, {}, rect=[0, 0, 40, 40])["ok"])
        self.assertFalse(ic.same_image(a, {}, b, {}, rect=[40, 40, 60, 60])["ok"])

    def test_different_image(self):
        """048 AC-05: 다른 픽셀 비율이 기준 이상이어야 통과."""
        a = grass()
        self.assertFalse(ic.different_image(a, {}, a.copy(), {})["ok"])
        b = a.copy()
        b[:, : b.shape[1] // 10] = (0, 0, 0)
        r = ic.different_image(a, {}, b, {}, min_diff_ratio=0.05)
        self.assertTrue(r["ok"], r["detail"])
        self.assertFalse(ic.different_image(a, {}, b, {}, min_diff_ratio=0.5)["ok"])


class GlyphBandMixedColors(unittest.TestCase):
    """specs/056 T19b: 번호 아래 띠가 검은 반바지에 걸쳐도 조명 배율을 셔츠 색조로만 추정해 글자를 놓치지 않는다."""

    def test_dark_shorts_in_band(self):
        import numpy as np
        from farm import imagechecks as IC
        H, W = 200, 120
        a = np.zeros((H, W, 3), dtype=np.uint8)
        a[:150] = (0, 200, 215)          # 하늘색 셔츠 (조명 0.85배)
        a[150:] = (5, 5, 5)              # 검은 반바지 — 띠 아래쪽 절반
        a[90:140, 55:62] = (230, 4, 218)  # 마젠타 번호 "1"
        reg = {"textAreas": [{"area": [40, 88, 36, 55], "text": [255, 0, 255], "base": [0, 229, 255], "kind": "number", "entity": 0}]}
        r = IC.glyph_inside(a, reg, min_glyph_px=12)
        self.assertTrue(r["ok"], r["detail"])

