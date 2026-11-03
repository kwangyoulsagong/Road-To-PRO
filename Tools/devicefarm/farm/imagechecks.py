"""캡처 이미지 검사 (specs/044 AC-10~13). 시나리오 `imageChecks`가 캡처 이름과 검사 종류를 지정한다.

영역 좌표는 드라이버가 캡처 때 남긴 `regions`(PNG 픽셀, 왼쪽 위 원점):
- 사각형 [x, y, w, h], 선분 {"a": [x, y], "b": [x, y]}, 점 [x, y], 점 목록 [[x, y], ...].
"""
import re

import numpy as np

TYPES = ("notBlank", "bandFlip", "lineContinuity", "blobX")
PAIRED = ("bandFlip",)  # 캡처 두 장 (capture + other)


def _lum(a):
    a = a.astype(np.float64)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def _rect_mean(lum, r):
    x, y, w, h = (int(round(v)) for v in r)
    H, W = lum.shape
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        raise ValueError(f"영역이 화면 밖 {r}")
    return float(lum[y0:y1, x0:x1].mean())


def not_blank(arr, regions, min_std=8.0, min_colors=3):
    """AC-10: 빈 화면·검은 화면 아님 — 밝기 표준편차와 색 수."""
    lum = _lum(arr)
    std = float(lum.std())
    colors = len(np.unique(arr.reshape(-1, 3)[:: max(1, arr.shape[0] * arr.shape[1] // 50000)], axis=0))
    ok = std >= min_std and colors >= min_colors and float(lum.mean()) > 5.0
    return {"ok": ok, "detail": f"밝기 표준편차 {std:.1f} (≥ {min_std}), 색 {colors} (≥ {min_colors})", "std": std}


def band_flip(arr_a, reg_a, arr_b, reg_b, min_delta=2.0):
    """AC-11: 같은 띠 두 개의 밝기 순서가 반대 시점에서 뒤집힌다."""
    la, lb = _lum(arr_a), _lum(arr_b)
    a1, a2 = _rect_mean(la, reg_a["bandA"]), _rect_mean(la, reg_a["bandB"])
    b1, b2 = _rect_mean(lb, reg_b["bandA"]), _rect_mean(lb, reg_b["bandB"])
    da, db = a1 - a2, b1 - b2
    ok = abs(da) >= min_delta and abs(db) >= min_delta and (da > 0) != (db > 0)
    return {"ok": ok, "detail": f"시점 A: 띠A {a1:.1f} / 띠B {a2:.1f} (차 {da:+.1f}), 시점 B: {b1:.1f} / {b2:.1f} (차 {db:+.1f}), 최소 차 {min_delta}",
            "deltaA": da, "deltaB": db}


def line_continuity(arr, regions, samples=200, max_width_std=1.0, half_scan=14, min_contrast=25.0):
    """AC-12: 라인 중심선을 따라 `samples`칸 샘플 — 각 칸에서 수직으로 훑어 라인 픽셀이 없으면 끊김, 폭 표준편차."""
    lum = _lum(arr)
    H, W = lum.shape
    seg = regions["line"]
    a = np.array(seg["a"], dtype=np.float64)
    b = np.array(seg["b"], dtype=np.float64)
    d = b - a
    length = float(np.hypot(*d))
    if length < samples:
        return {"ok": False, "detail": f"라인 화면 길이 {length:.0f}px < 샘플 {samples}", "gaps": samples}
    t = d / length
    n = np.array([-t[1], t[0]])
    start = (length - samples) / 2
    gaps, widths = 0, []
    for k in range(samples):
        c = a + t * (start + k + 0.5)
        vals = []
        for s in range(-half_scan, half_scan + 1):
            x, y = int(np.floor(c[0] + n[0] * s + 0.5)), int(np.floor(c[1] + n[1] * s + 0.5))
            vals.append(lum[y, x] if 0 <= x < W and 0 <= y < H else np.nan)
        v = np.array(vals)
        if np.isnan(v).any() or v.max() - v.min() < min_contrast:
            gaps += 1
            continue
        thr = (v.max() + v.min()) / 2
        widths.append(int((v > thr).sum()))
    std = float(np.std(widths)) if widths else float("inf")
    ok = gaps == 0 and std <= max_width_std
    return {"ok": ok, "detail": f"샘플 {samples}칸 중 끊김 {gaps}, 폭 평균 {np.mean(widths) if widths else 0:.2f}px 표준편차 {std:.2f}px (≤ {max_width_std})",
            "gaps": gaps, "widthStd": std}


def blob_x(arr, regions, min_darkening=0.12, box=6, ring=18):
    """AC-13: 예상 위치 4곳이 주변 지면보다 어둡고, 발 기준 대각 두 쌍이 서로 반대 방향(X자)."""
    lum = _lum(arr)
    foot = np.array(regions["foot"], dtype=np.float64)
    blobs = [np.array(c, dtype=np.float64) for c in regions["blobs"]]
    if len(blobs) != 4:
        return {"ok": False, "detail": f"블롭 위치 {len(blobs)}개 (4개 필요)"}
    darks = []
    for c in blobs:
        inner = _rect_mean(lum, [c[0] - box, c[1] - box, 2 * box, 2 * box])
        # 바깥 기준: 드라이버가 준 블롭 옆 지면 점(regions.refs), 없으면 발 방향과 수직으로 ring px
        k = len(darks)
        if regions.get("refs"):
            pts = [np.array(q, dtype=np.float64) for q in regions["refs"][k]]
        else:
            v = c - foot
            nv = np.array([-v[1], v[0]]) / max(np.hypot(*v), 1e-6)
            pts = [c + nv * ring, c - nv * ring]
        refs = [_rect_mean(lum, [p[0] - box, p[1] - box, 2 * box, 2 * box]) for p in pts]
        ref = max(refs)
        darks.append(0.0 if ref <= 0 else (ref - inner) / ref)
    dirs = [(c - foot) / max(np.hypot(*(c - foot)), 1e-6) for c in blobs]
    # 가장 반대인 짝을 찾아 두 쌍이 모두 반대인지
    pairs = sorted(((float(np.dot(dirs[i], dirs[j])), i, j) for i in range(4) for j in range(i + 1, 4)))
    used, opp = set(), []
    for dot, i, j in pairs:
        if i in used or j in used:
            continue
        used |= {i, j}
        opp.append(dot)
    x_ok = len(opp) == 2 and all(d < -0.7 for d in opp)
    dark_ok = all(d >= min_darkening for d in darks)
    return {"ok": x_ok and dark_ok,
            "detail": f"어두워진 비율 {', '.join(f'{d:.2f}' for d in darks)} (≥ {min_darkening}), 대각 쌍 내적 {', '.join(f'{d:.2f}' for d in opp)} (< −0.7)"}


def run(checks, captures):
    """checks: 시나리오 imageChecks. captures: {이름: (RGB 배열, regions)}. → 결과 목록."""
    out = []
    for c in checks:
        t = c["type"]
        params = {re.sub(r"([A-Z])", lambda m: "_" + m.group(1).lower(), k): v
                  for k, v in c.items() if k not in ("type", "capture", "other", "ac")}
        row = {"type": t, "capture": c["capture"], "ac": c.get("ac"), "ok": False, "detail": ""}
        try:
            if c["capture"] not in captures:
                raise KeyError(f"캡처 없음: {c['capture']}")
            arr, reg = captures[c["capture"]]
            if t == "notBlank":
                r = not_blank(arr, reg, **params)
            elif t == "bandFlip":
                if c["other"] not in captures:
                    raise KeyError(f"캡처 없음: {c['other']}")
                arr2, reg2 = captures[c["other"]]
                r = band_flip(arr, reg, arr2, reg2, **params)
            elif t == "lineContinuity":
                r = line_continuity(arr, reg, **params)
            elif t == "blobX":
                r = blob_x(arr, reg, **params)
            else:
                raise KeyError(f"알 수 없는 검사 {t}")
            row.update(ok=bool(r["ok"]), detail=r["detail"])
        except (KeyError, ValueError, IndexError, TypeError) as e:
            row["detail"] = str(e).strip("'")
        out.append(row)
    return out
