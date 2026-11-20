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



# ---------------------------------------------------------------- specs/061
def _mask(h, w, layout):
    """layout: [(분류, (y0, y1, x0, x1))] → 분류 색 마스크 캡처."""
    m = np.zeros((h, w, 3), dtype=np.uint8)
    for k, (y0, y1, x0, x1) in layout:
        m[y0:y1, x0:x1] = ic.MASK_COLORS[k]
    return m


def _noise(h, w, base, amp, seed=1):
    rng = np.random.default_rng(seed)
    a = np.array(base, dtype=np.float64) + rng.normal(0, amp, (h, w, 1))
    return a.clip(0, 255).astype(np.uint8)


class ColorStats(unittest.TestCase):
    def test_muted_passes(self):
        a = _noise(100, 100, (80, 100, 70), 6)
        m = _mask(100, 100, [("grass", (0, 100, 0, 100))])
        r = ic.color_stats(a, {}, ic.masks_from(m), grass_sat=(0.2, 0.55), baseline_mean_sat=0.6)
        self.assertTrue(r["ok"], r)

    def test_primary_colors_fail(self):
        a = grass(100, 100, (230, 10, 10))
        r = ic.color_stats(a, {}, None)
        self.assertFalse(r["ok"], r)

    def test_grass_hue_out_of_range_fails(self):
        a = _noise(100, 100, (40, 90, 110), 4)  # 청록
        m = _mask(100, 100, [("grass", (0, 100, 0, 100))])
        self.assertFalse(ic.color_stats(a, {}, ic.masks_from(m))["ok"])


class Exposure(unittest.TestCase):
    def test_in_range(self):
        a = np.zeros((100, 100, 3), dtype=np.uint8)
        a[:, :] = np.linspace(20, 230, 100, dtype=np.uint8)[None, :, None]
        self.assertTrue(ic.exposure(a, {}, None, mean_range=(40, 65), width_range=(50, 90))["ok"])

    def test_too_dark(self):
        self.assertFalse(ic.exposure(grass(50, 50, (10, 10, 10)), {}, None)["ok"])

    def test_match(self):
        self.assertTrue(ic.exposure_match([grass(10, 10, (100, 100, 100)), grass(10, 10, (110, 110, 110))])["ok"])
        self.assertFalse(ic.exposure_match([grass(10, 10, (60, 60, 60)), grass(10, 10, (160, 160, 160))])["ok"])


class Sky(unittest.TestCase):
    def test_flat_gray_fails(self):
        a = grass(100, 100, (160, 160, 160))
        m = _mask(100, 100, [("sky", (0, 50, 0, 100))])
        self.assertFalse(ic.sky(a, {}, ic.masks_from(m))["ok"])

    def test_gradient_passes(self):
        a = np.zeros((100, 100, 3), dtype=np.uint8)
        for y in range(50):
            a[y] = (90 + y * 2, 140 + y, 220)
        m = _mask(100, 100, [("sky", (0, 50, 0, 100))])
        self.assertTrue(ic.sky(a, {}, ic.masks_from(m))["ok"])

    def test_small_sky_not_applicable(self):
        a = grass(100, 100, (160, 160, 160))
        m = _mask(100, 100, [("sky", (0, 2, 0, 100))])
        r = ic.sky(a, {}, ic.masks_from(m))
        self.assertTrue(r["ok"] and r.get("na"), r)


class RegionsAndGray(unittest.TestCase):
    def test_shade_ratio(self):
        a = grass(100, 100, (80, 120, 60))
        a[:, 50:] = (35, 55, 28)
        m = _mask(100, 100, [("sun", (0, 100, 0, 50)), ("shade", (0, 100, 50, 100))])
        self.assertTrue(ic.region_ratio(a, {}, ic.masks_from(m))["ok"])
        a[:, 50:] = (4, 6, 3)
        self.assertFalse(ic.region_ratio(a, {}, ic.masks_from(m))["ok"])

    def test_gray_outside(self):
        m = _mask(100, 100, [("outside", (0, 30, 0, 100))])
        self.assertFalse(ic.gray_ratio(grass(100, 100, (128, 128, 128)), {}, ic.masks_from(m))["ok"])
        self.assertTrue(ic.gray_ratio(_noise(100, 100, (90, 120, 160), 8), {}, ic.masks_from(m))["ok"])

    def test_clipping(self):
        self.assertTrue(ic.clipping(grass(50, 50, (120, 120, 120)), {}, None)["ok"])
        self.assertFalse(ic.clipping(grass(50, 50, (255, 255, 255)), {}, None)["ok"])


class PlayerLight(unittest.TestCase):
    def _scene(self, rim):
        a = grass(100, 100, (40, 70, 35))
        a[30:70, 40:60] = (150, 60, 60)
        a[30:70, 40:42] = rim
        a[30:70, 58:60] = rim
        a[30:32, 40:60] = rim
        a[68:70, 40:60] = rim
        m = _mask(100, 100, [("grass", (0, 100, 0, 100)), ("player", (30, 70, 40, 60))])
        return a, ic.masks_from(m)

    def test_rim_gain_vs_baseline(self):
        a0, m = self._scene((150, 60, 60))
        base = ic.rim_contrast(a0, {}, m)["contrast"]
        a1, _ = self._scene((180, 90, 90))
        self.assertTrue(ic.rim_contrast(a1, {}, m, baseline=base)["ok"])

    def test_glowing_rim_fails(self):
        a, m = self._scene((255, 255, 255))
        self.assertFalse(ic.rim_contrast(a, {}, m)["ok"])

    def test_contact_darkening(self):
        a = grass(100, 100, (80, 120, 60))
        yy, xx = np.mgrid[0:100, 0:100]
        a[np.hypot(xx - 50, yy - 50) <= 6] = (50, 80, 40)
        self.assertTrue(ic.contact_darkening(a, {"feet": [[50, 50, 6]]}, None)["ok"])
        self.assertFalse(ic.contact_darkening(grass(100, 100), {"feet": [[50, 50, 6]]}, None)["ok"])


class ModeGap(unittest.TestCase):
    def test_same_look_passes(self):
        a = _noise(80, 80, (90, 110, 80), 10)
        b = a.copy()
        blk = a[:, :40].astype(np.float64).reshape(10, 8, 5, 8, 3).mean(axis=(1, 3))
        b[:, :40] = np.repeat(np.repeat(blk, 8, axis=0), 8, axis=1).astype(np.uint8)  # 8px 상자 흐림 흉내
        self.assertTrue(ic.mode_gap(a, {}, b, {})["ok"])

    def test_brighter_fails(self):
        a = _noise(80, 80, (90, 110, 80), 10)
        b = (a.astype(np.int16) + 20).clip(0, 255).astype(np.uint8)
        self.assertFalse(ic.mode_gap(a, {}, b, {})["ok"])

    def test_frame_delta(self):
        seq = [grass(10, 10, (100 + i, 100, 100)) for i in range(4)]
        self.assertTrue(ic.frame_delta_e(seq)["ok"])
        self.assertFalse(ic.frame_delta_e([grass(10, 10, (100, 100, 100)), grass(10, 10, (160, 60, 60))])["ok"])


class AntiAliasing(unittest.TestCase):
    def _edge(self, scale, aa):
        n = 64 * scale
        a = np.zeros((n, n, 3), dtype=np.uint8)
        yy, xx = np.mgrid[0:n, 0:n]
        a[yy > xx * 0.37 + n * 0.2] = 230
        if aa:
            return a
        return a

    def test_supersample_reference(self):
        ref = self._edge(4, True)
        smooth = ic._down(ref, 4).astype(np.uint8)
        r = ic.aa_edge(smooth, {}, ref, {})
        self.assertTrue(r["ok"], r)
        jag = self._edge(1, False)
        r2 = ic.aa_edge(jag, {}, ref, {}, max_abs=6)
        self.assertGreater(r2["edgeDiff"], r["edgeDiff"])

    def test_pan_flicker(self):
        base = [self._edge(1, False) for _ in range(3)]
        noisy = [(f.astype(np.int16) + (i % 2) * 30).clip(0, 255).astype(np.uint8) for i, f in enumerate(base)]
        self.assertTrue(ic.pan_flicker(base, noisy)["ok"])
        self.assertFalse(ic.pan_flicker(noisy, base)["ok"])


class GrassDepth(unittest.TestCase):
    def _ground(self, rough):
        a = np.zeros((120, 200, 3), dtype=np.uint8)
        a[:] = (140, 170, 220)
        rng = np.random.default_rng(3)
        for x in range(200):
            top = 60 + (int(rng.integers(-4, 5)) if rough else 0)
            a[top:, x] = (50, 110, 40)
        return a

    def test_blades_raise_std(self):
        reg = {"groundBand": [0, 20, 200, 90]}
        self.assertTrue(ic.ground_line_std(self._ground(True), reg, None)["ok"])
        self.assertFalse(ic.ground_line_std(self._ground(False), reg, None)["ok"])

    def test_region_std(self):
        m = ic.masks_from(_mask(50, 50, [("grass", (0, 50, 0, 50))]))
        r = ic.region_std(_noise(50, 50, (60, 100, 50), 8), {}, m, baseline=2.0, min_ratio=1.5)
        self.assertTrue(r["ok"], r)


class Run061(unittest.TestCase):
    def test_dispatch_with_mask_and_others(self):
        a = grass(60, 60, (100, 100, 100))
        m = _mask(60, 60, [("sky", (0, 30, 0, 60))])
        caps = {"a": (a, {}), "b": (a.copy(), {}), "m": (m, {})}
        rows = ic.run([{"type": "sky", "capture": "a", "mask": "m"},
                       {"type": "exposureMatch", "capture": "a", "others": ["b"]},
                       {"type": "modeGap", "capture": "a", "other": "b"}], caps)
        self.assertEqual([r["ok"] for r in rows], [False, True, True], rows)


class SharpnessAndShade(unittest.TestCase):
    def test_sharpness_blurred_subject_fails(self):
        sharp = _noise(60, 60, (120, 120, 120), 30)
        blk = sharp.astype(np.float64).reshape(15, 4, 15, 4, 3).mean(axis=(1, 3))
        blur = np.repeat(np.repeat(blk, 4, axis=0), 4, axis=1).astype(np.uint8)
        reg = {"scorer": [5, 5, 50, 50]}
        self.assertTrue(ic.sharpness(sharp, reg, sharp.copy(), reg)["ok"])
        self.assertFalse(ic.sharpness(blur, reg, sharp, reg)["ok"])

    def test_grass_shade_ratio(self):
        a = grass(100, 100, (80, 130, 60))
        a[:, 50:] = (30, 50, 22)
        m = ic.masks_from(_mask(100, 100, [("grass", (0, 100, 0, 100))]))
        self.assertTrue(ic.grass_shade(a, {}, m)["ok"])
        a[:, 50:] = (5, 7, 4)
        self.assertFalse(ic.grass_shade(a, {}, m)["ok"])
        r = ic.grass_shade(grass(100, 100, (80, 130, 60)), {}, m)
        self.assertTrue(r["ok"] and r.get("na"), r)
