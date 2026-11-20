"""specs/062 선수 비주얼 화면 지표 (T03). imagechecks.run이 TYPES_062 종류를 여기로 보낸다.

마스크: 061과 같은 분류 색 캡처(시나리오 `mask`). 062가 쓰는 분류 — 선수(player)·키트 주색(kit)·피부(skin)·머리카락(hair).
영역: 드라이버 `regions`의 사각형 [x, y, w, h] (손·어깨·정장 등).
"""
import math

import numpy as np

from . import imagechecks as ic

TYPES_062 = ("outlineTurn", "seamStep", "handConcavity", "kitStats", "silhouetteDiff", "highlightAnisotropy",
             "tierMatch", "textSpots")


def _rect(reg, name):
    if name not in reg:
        raise KeyError(f"영역 없음: {name}")
    x, y, w, h = (int(round(v)) for v in reg[name])
    return x, y, w, h


def _crop_mask(m, reg, name):
    if name is None:
        return m
    x, y, w, h = _rect(reg, name)
    out = np.zeros_like(m)
    out[y:y + h, x:x + w] = m[y:y + h, x:x + w]
    return out


def _cls(masks, name):
    if masks is None or name not in masks:
        raise KeyError(f"마스크 분류 없음: {name}")
    return masks[name]


# ---------------------------------------------------------------- 윤곽

def trace_boundary(m):
    """가장 큰 덩어리의 바깥 윤곽 (Moore 이웃 추적) → [(x, y), ...]."""
    ys, xs = np.nonzero(m)
    if len(xs) == 0:
        return []
    i = np.lexsort((xs, ys))[0]
    start = (int(xs[i]), int(ys[i]))
    H, W = m.shape
    nb = [(-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1)]

    def on(x, y):
        return 0 <= x < W and 0 <= y < H and m[y, x]

    pts, cur, back = [start], start, 0
    for _ in range(4 * m.sum() + 8):
        found = False
        for k in range(8):
            d = (back + 1 + k) % 8
            nx, ny = cur[0] + nb[d][0], cur[1] + nb[d][1]
            if on(nx, ny):
                back = (d + 4) % 8
                cur = (nx, ny)
                found = True
                break
        if not found or cur == start:
            break
        pts.append(cur)
    return pts


def outline_turn(arr, regions, masks, cls="player", region=None, step=6, max_deg=25.0, margin=2):
    """FR-05: 윤곽선 꺾임 각 최댓값. 1픽셀 윤곽을 창 step으로 원형 이동 평균한 뒤, 각 점에서 앞뒤 step 픽셀 현(chord) 사이
    방향 변화(°)를 잰다 (픽셀 계단 제거). 영역이 있으면 앞뒤 현까지 영역 안(가장자리 margin 제외)인 점만."""
    m = _cls(masks, cls)
    pts = trace_boundary(m)
    if len(pts) < 4 * step:
        raise ValueError("윤곽이 너무 짧다")
    P = np.array(pts, dtype=float)
    k = np.ones(step) / step
    S = np.stack([np.convolve(np.concatenate([P[-step:, d], P[:, d], P[:step, d]]), k, mode="same")[step:-step]
                  for d in (0, 1)], axis=1)
    a = S - np.roll(S, step, axis=0)
    b = np.roll(S, -step, axis=0) - S
    turn = np.degrees(np.arctan2(np.abs(a[:, 0] * b[:, 1] - a[:, 1] * b[:, 0]), (a * b).sum(axis=1)))
    if region is not None:
        x, y, w, h = _rect(regions, region)
        ins = (P[:, 0] >= x + margin) & (P[:, 0] < x + w - margin) & (P[:, 1] >= y + margin) & (P[:, 1] < y + h - margin)
        ok_pt = ins & np.roll(ins, step) & np.roll(ins, -step)
    else:
        ok_pt = np.ones(len(P), dtype=bool)
    sel = turn[ok_pt]
    if len(sel) == 0:
        raise ValueError("영역 안 윤곽 없음")
    mx = float(sel.max())
    return {"ok": mx <= max_deg, "detail": f"최대 꺾임 {mx:.1f}° (≤ {max_deg}°, 표본 {len(sel)})", "value": mx}


def seam_step(arr, regions, masks, a="seamSkin", b="seamKit", max_ratio=1.5):
    """FR-05 이음매: 음영 전용 캡처(알베도 흰색)에서 경계 양쪽 띠 평균 밝기 차 ≤ 같은 재질 안 변동(두 띠 표준편차 평균) × 1.5."""
    L = ic._lstar(arr)
    def band(n):
        x, y, w, h = _rect(regions, n)
        return L[y:y + h, x:x + w].ravel()
    A, B = band(a), band(b)
    step = abs(float(A.mean() - B.mean()))
    var = max(0.5 * (float(A.std()) + float(B.std())), 1e-6)
    return {"ok": step <= max_ratio * var, "detail": f"단차 {step:.2f} / 변동 {var:.2f} = {step / var:.2f} (≤ {max_ratio})",
            "value": step / var}


# ---------------------------------------------------------------- 손

def _hull(pts):
    pts = sorted(set(map(tuple, pts)))
    if len(pts) < 3:
        return pts
    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0:
            lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0:
            up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]


def _fill_convex(poly, shape):
    H, W = shape
    yy, xx = np.mgrid[0:H, 0:W]
    inside = np.ones(shape, dtype=bool)
    n = len(poly)
    for i in range(n):
        (x0, y0), (x1, y1) = poly[i], poly[(i + 1) % n]
        inside &= (x1 - x0) * (yy - y0) - (y1 - y0) * (xx - x0) >= -0.5
    return inside


def label(m):
    """4이웃 연결 성분 → (라벨 배열, 성분 수)."""
    lab = np.zeros(m.shape, dtype=np.int32)
    n = 0
    H, W = m.shape
    for y0, x0 in zip(*np.nonzero(m)):
        if lab[y0, x0]:
            continue
        n += 1
        stack = [(y0, x0)]
        lab[y0, x0] = n
        while stack:
            y, x = stack.pop()
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < H and 0 <= nx < W and m[ny, nx] and not lab[ny, nx]:
                    lab[ny, nx] = n
                    stack.append((ny, nx))
    return lab, n


def hand_concavity(arr, regions, masks, cls="player", region="hand", min_concave=4, min_px=6):
    """FR-10: 손 실루엣(영역 안) 볼록 껍질 − 손 = 오목 성분, 크기 ≥ min_px 성분 수 ≥ 4 (손가락 사이 틈 > 0)."""
    x, y, w, h = _rect(regions, region)
    m = _cls(masks, cls)[y:y + h, x:x + w]
    ys, xs = np.nonzero(m)
    if len(xs) < 10:
        raise ValueError("손 영역에 선수 픽셀 없음")
    hull = _hull(np.stack([xs, ys], axis=1))
    gap = _fill_convex(hull, m.shape) & ~m
    lab, n = label(gap)
    sizes = np.bincount(lab.ravel())[1:] if n else np.array([])
    k = int((sizes >= min_px).sum())
    return {"ok": k >= min_concave, "detail": f"오목 성분 {k}개 (≥ {min_concave}, 크기 ≥ {min_px}px)", "value": k}


# ---------------------------------------------------------------- 키트 · 헤어

def kit_stats(arr, regions, masks, cls="kit", team_rgb=(200, 30, 30), baseline_l_std=None, min_ratio=2.0,
              max_saturated=0.20, region=None):
    """FR-26: 키트 주색 영역 L* 표준편차 ≥ 기준 × 2, 채도가 입력 팀 색 채도의 98 % 이상인 픽셀 비율 ≤ 20 %."""
    m = _crop_mask(_cls(masks, cls), regions, region)
    if m.sum() < 50:
        raise ValueError("키트 픽셀 부족")
    L = ic._lstar(arr)[m]
    _, sat, _ = ic._hsv(arr)
    t = np.array(team_rgb, dtype=float) / 255.0
    s_in = (t.max() - t.min()) / max(t.max(), 1e-9)
    satr = float(np.mean(sat[m] >= 0.98 * s_in))
    lstd = float(L.std())
    ok = satr <= max_saturated and (baseline_l_std is None or lstd >= min_ratio * baseline_l_std)
    base = "" if baseline_l_std is None else f" (기준 {baseline_l_std:.2f} × {min_ratio})"
    return {"ok": ok, "detail": f"L* 표준편차 {lstd:.2f}{base}, 포화 {satr:.1%} (≤ {max_saturated:.0%})",
            "value": {"lStd": lstd, "saturated": satr}}


def silhouette_diff(mask_arrs, cls=("player", "skin", "hair", "kit"), min_diff=0.08, exclude_pairs=()):
    """FR-44: 같은 구도 마스크 캡처들의 분류 합집합 쌍별 1 − IoU 최솟값 ≥ min_diff (제외 쌍은 인덱스 [i, j])."""
    names = (cls,) if isinstance(cls, str) else tuple(cls)
    ms = [np.any([ic.masks_from(a)[n] for n in names], axis=0) for a in mask_arrs]
    ex = {tuple(sorted(p)) for p in exclude_pairs}
    worst, pair = 2.0, None
    for i in range(len(ms)):
        for j in range(i + 1, len(ms)):
            if (i, j) in ex:
                continue
            u = np.logical_or(ms[i], ms[j]).sum()
            d = 1.0 - (np.logical_and(ms[i], ms[j]).sum() / u if u else 1.0)
            if d < worst:
                worst, pair = d, (i, j)
    return {"ok": worst >= min_diff, "detail": f"최소 차이 {worst:.3f} (쌍 {pair}, ≥ {min_diff})", "value": worst}


def highlight_anisotropy(arr, regions, masks, cls="hair", top=0.10, min_ratio=2.0, min_px=8, region=None):
    """FR-34: 머리카락 영역 밝은 상위 10 % 픽셀 덩어리의 주축 길이 비(√(λ1/λ2)) 픽셀 가중 평균 ≥ 2."""
    m = _crop_mask(_cls(masks, cls), regions, region)
    if m.sum() < 50:
        raise ValueError("머리카락 픽셀 부족")
    L = ic._lstar(arr)
    thr = np.quantile(L[m], 1.0 - top)
    lab, n = label(m & (L >= thr) & (L > np.median(L[m])))
    tot, acc = 0, 0.0
    for k in range(1, n + 1):
        ys, xs = np.nonzero(lab == k)
        if len(xs) < min_px:
            continue
        c = np.cov(np.stack([xs, ys]).astype(float))
        ev = np.sort(np.linalg.eigvalsh(c))
        r = math.sqrt(max(ev[1], 1e-9) / max(ev[0], 1e-3))
        acc += r * len(xs)
        tot += len(xs)
    if tot == 0:
        raise ValueError("하이라이트 덩어리 없음")
    v = acc / tot
    return {"ok": v >= min_ratio, "detail": f"이방성 {v:.2f}:1 (≥ {min_ratio}:1)", "value": v}


def tier_match(arr_a, masks_a, arr_b, masks_b, classes=("skin", "kit", "hair"), max_de=3.0, max_l=3.0):
    """FR-88·89: 두 등급(같은 화면 크기) 부위별 평균 색 ΔE2000 ≤ 3, 평균 L* 차 ≤ 3."""
    rows, ok = [], True
    for c in classes:
        ma, mb = _cls(masks_a, c), _cls(masks_b, c)
        if ma.sum() < 20 or mb.sum() < 20:
            raise ValueError(f"{c} 픽셀 부족")
        la = ic.srgb_to_lab(arr_a[..., :3])[ma].mean(axis=0)
        lb = ic.srgb_to_lab(arr_b[..., :3])[mb].mean(axis=0)
        de = float(ic.delta_e2000(la, lb))
        dl = abs(float(la[0] - lb[0]))
        ok &= de <= max_de and dl <= max_l
        rows.append(f"{c} ΔE {de:.2f}·L* {dl:.2f}")
    return {"ok": bool(ok), "detail": ", ".join(rows) + f" (≤ {max_de}·{max_l})"}


def text_spots(arr, regions, masks, cls="player", region=None, edge=60.0, min_px=4, max_px=400, max_spots=0, inset=3):
    """FR-27 로고·문자 검출: 선수 마스크 안쪽(가장자리 inset px 제외)에서 강한 밝기 경계 덩어리(min_px~max_px) 수 ≤ 0."""
    m = _crop_mask(_cls(masks, cls), regions, region)
    inner = ic._erode(m, inset)
    L = ic._lstar(arr)
    gx = np.abs(np.diff(L, axis=1, prepend=L[:, :1]))
    gy = np.abs(np.diff(L, axis=0, prepend=L[:1, :]))
    e = inner & (np.maximum(gx, gy) * 2.55 >= edge)
    lab, n = label(e)
    sizes = np.bincount(lab.ravel())[1:] if n else np.array([])
    k = int(((sizes >= min_px) & (sizes <= max_px)).sum())
    return {"ok": k <= max_spots, "detail": f"경계 덩어리 {k}개 (≤ {max_spots})", "value": k}


_SINGLE = {"outlineTurn": outline_turn, "seamStep": seam_step, "handConcavity": hand_concavity, "kitStats": kit_stats,
           "highlightAnisotropy": highlight_anisotropy, "textSpots": text_spots}


def run_062(t, c, params, captures):
    def get(name):
        if name not in captures:
            raise KeyError(f"캡처 없음: {name}")
        return captures[name]
    mask_name = params.pop("mask", None)
    masks = ic.masks_from(get(mask_name)[0]) if mask_name else None
    for k in ("team_rgb", "classes", "cls"):
        if k == "cls" and isinstance(params.get(k), str):
            continue
        if k in params:
            params[k] = tuple(params[k])
    arr, reg = get(c["capture"])
    if t in _SINGLE:
        return _SINGLE[t](arr, reg, masks, **params)
    if t == "silhouetteDiff":
        return silhouette_diff([arr] + [get(n)[0] for n in c.get("others") or []], **params)
    if t == "tierMatch":
        arr2, _ = get(c["other"])
        mask2 = params.pop("mask_other")
        return tier_match(arr, masks, arr2, ic.masks_from(get(mask2)[0]), **params)
    raise KeyError(f"알 수 없는 검사 {t}")
