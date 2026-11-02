"""스크린샷 기준 비교 (specs/000 FR-30~34).

- 픽셀의 RGB 채널 중 하나라도 |차이| > pixelThreshold면 바뀐 픽셀.
- 바뀐 픽셀 비율 > maxDiffRatio면 실패. 마스크 영역은 분모·분자에서 뺀다.
- 해상도가 다르면 비교하지 않고 실패 (리사이즈 비교 금지).
- 단색(검은·빈) 화면은 기준과 무관하게 실패.
"""
import os
from dataclasses import dataclass

import numpy as np
from PIL import Image

HIGHLIGHT = np.array([255, 0, 64], np.uint8)
SOLID_STD_MAX = 2.0  # 휘도 표준편차가 이 이하면 단색으로 본다


@dataclass
class CompareResult:
    status: str  # pass | fail | candidate
    reason: str = ""
    diff_pixels: int = 0
    diff_ratio: float = 0.0
    bbox: tuple = None  # (x, y, w, h) 바뀐 영역
    diff_image: np.ndarray = None

    def to_dict(self):
        return {
            "status": self.status,
            "reason": self.reason,
            "diffPixels": self.diff_pixels,
            "diffRatio": self.diff_ratio,
            "bbox": list(self.bbox) if self.bbox else None,
        }


def to_rgb(arr):
    if arr.ndim == 2:
        return np.stack([arr] * 3, axis=-1)
    return arr[..., :3]


def load_rgb(path):
    with Image.open(path) as im:
        return to_rgb(np.asarray(im.convert("RGBA")))


def is_solid(arr):
    lum = arr[..., :3].astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)
    return float(lum.std()) <= SOLID_STD_MAX


def mask_to_pixels(mask, width, height):
    """시나리오 마스크(비율 rect) 또는 드라이버가 푼 픽셀 rect → (x, y, w, h) 픽셀."""
    if "pixels" in mask:
        return tuple(int(v) for v in mask["pixels"])
    x, y, w, h = mask["rect"]
    return (round(x * width), round(y * height), round(w * width), round(h * height))


def check_capture_size(arr, expected_wh):
    h, w = arr.shape[:2]
    ew, eh = expected_wh
    if (w, h) != (ew, eh):
        return False, f"캡처 해상도 {w}×{h} ≠ 프로필 해상도 {ew}×{eh}"
    return True, ""


def _diff_image(cur, changed):
    gray = (cur.astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)) * 0.35 + 140
    out = np.repeat(gray.clip(0, 255).astype(np.uint8)[..., None], 3, axis=-1)
    out[changed] = HIGHLIGHT
    return out


def compare_arrays(cur, base, pixel_threshold=8, max_diff_ratio=0.001, masks=()):
    cur = to_rgb(cur)
    if is_solid(cur):
        return CompareResult("fail", "단색 화면 (렌더 실패 의심: 검은·빈 화면)")
    if base is None:
        return CompareResult("candidate", "기준 이미지 없음 — 기준 후보")
    base = to_rgb(base)
    if cur.shape != base.shape:
        return CompareResult(
            "fail",
            f"기준과 해상도가 다름 ({cur.shape[1]}×{cur.shape[0]} vs {base.shape[1]}×{base.shape[0]})",
        )

    h, w = cur.shape[:2]
    delta = np.abs(cur.astype(np.int16) - base.astype(np.int16)).max(axis=-1)
    changed = delta > pixel_threshold
    valid = np.ones((h, w), bool)
    for m in masks:
        x, y, mw, mh = m
        valid[max(0, y) : y + mh, max(0, x) : x + mw] = False
    changed &= valid
    total = int(valid.sum())
    n = int(changed.sum())
    ratio = n / total if total else 0.0

    bbox = None
    if n:
        ys, xs = np.nonzero(changed)
        bbox = (int(xs.min()), int(ys.min()), int(xs.max() - xs.min() + 1), int(ys.max() - ys.min() + 1))
    status = "fail" if ratio > max_diff_ratio else "pass"
    reason = f"바뀐 픽셀 {ratio:.4%} > 허용 {max_diff_ratio:.4%}" if status == "fail" else ""
    return CompareResult(status, reason, n, ratio, bbox, _diff_image(cur, changed) if n else None)


def compare_files(cur_path, base_path, diff_path=None, pixel_threshold=8, max_diff_ratio=0.001, masks=()):
    cur = load_rgb(cur_path)
    base = load_rgb(base_path) if base_path and os.path.exists(base_path) else None
    r = compare_arrays(cur, base, pixel_threshold, max_diff_ratio, masks)
    if diff_path and r.diff_image is not None:
        os.makedirs(os.path.dirname(diff_path), exist_ok=True)
        Image.fromarray(r.diff_image).save(diff_path)
    return r
