"""specs/088 T30a 낙차 제한 지표 (FR-58·AC-14). imagechecks.run이 TYPES_088 종류를 여기로 보낸다.

tierPair: 같은 선수·같은 카메라·같은 포즈를 두 등급(클로즈업 머리 ↔ LOD0, LOD0 ↔ LOD1)으로 찍은 두 캡처.
배경은 단색(시안 조명 스튜디오) — 배경색(네 모서리 중간값)과 다른 픽셀이 실루엣이다.
머리 구역 = 두 실루엣 합집합의 맨 위 행부터 화면(근접) 또는 실루엣 높이(원거리)의 head_frac까지.
"""
import numpy as np

from . import imagechecks as ic

TYPES_088 = ("tierPair",)


def _bg(arr):
    c = np.array([arr[0, 0], arr[0, -1], arr[-1, 0], arr[-1, -1]], dtype=np.int16)
    return np.median(c, axis=0)


def _silhouette(arr, bg, tol):
    return np.abs(arr[..., :3].astype(np.int16) - bg).max(axis=-1) > tol


def tier_pair(arr_a, arr_b, head_frac=1.0, max_iou_diff=0.05, max_de=3.0, tol=6, min_px=200):
    """두 등급 머리 구역 1 − IoU ≤ max_iou_diff, 머리 구역 전체·위 35 %(헤어)·35~75 %(얼굴) 평균색 ΔE2000 ≤ max_de."""
    bg = _bg(arr_a)
    sa, sb = _silhouette(arr_a, bg, tol), _silhouette(arr_b, bg, tol)
    union = sa | sb
    rows = np.nonzero(union.any(axis=1))[0]
    if len(rows) == 0 or union.sum() < min_px:
        raise ValueError("실루엣 없음 (배경과 다른 픽셀 부족)")
    top, bottom = int(rows[0]), int(rows[-1]) + 1
    h = max(1, int(round((bottom - top) * head_frac)))
    band = slice(top, min(bottom, top + h))
    ha, hb = sa[band], sb[band]
    u, i = (ha | hb).sum(), (ha & hb).sum()
    iou_diff = 1.0 - i / max(1, u)
    ok = iou_diff <= max_iou_diff
    parts = [f"1−IoU {iou_diff:.3f} (≤ {max_iou_diff})"]
    la_all, lb_all = ic.srgb_to_lab(arr_a[band][..., :3]), ic.srgb_to_lab(arr_b[band][..., :3])
    for name, f0, f1 in (("머리", 0.0, 1.0), ("헤어 띠", 0.0, 0.35), ("얼굴 띠", 0.35, 0.75)):
        r0, r1 = int(h * f0), max(int(h * f0) + 1, int(h * f1))
        ma, mb = ha[r0:r1], hb[r0:r1]
        if ma.sum() < 20 or mb.sum() < 20:
            parts.append(f"{name} 픽셀 부족")
            continue
        la, lb = la_all[r0:r1][ma].mean(axis=0), lb_all[r0:r1][mb].mean(axis=0)
        de = float(ic.delta_e2000(la, lb))
        ok &= de <= max_de
        parts.append(f"{name} ΔE {de:.2f}")
    return {"ok": bool(ok), "iou_diff": float(iou_diff), "detail": ", ".join(parts) + f" (ΔE ≤ {max_de}, 머리 구역 {h}px)"}


def run_088(t, c, params, captures):
    def get(name):
        if name not in captures:
            raise KeyError(f"캡처 없음: {name}")
        return captures[name][0]
    if t == "tierPair":
        return tier_pair(get(c["capture"]), get(c["other"]), **params)
    raise KeyError(f"알 수 없는 검사 {t}")
